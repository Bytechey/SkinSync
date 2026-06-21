using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using BepInEx.Logging;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace SkinSyncMod.Network
{
    /// <summary>反射封装 KrokoshaCasualtiesMP 的多人接口；未加载时 IsAvailable=false 由调用方守卫。</summary>
    public static class KrokoshaBridge
    {
        private static bool _initialized;
        public static bool IsAvailable { get; private set; }

        private static Type _netType;
        private static MethodInfo _netClientSend;
        private static MethodInfo _netServerSendToClients;
        private static MethodInfo _netInvokeServerMessage;
        private static MethodInfo _netInvokeClientMessage;

        private static Type _netBodyType;
        private static FieldInfo _netBodyAllInstances;
        private static MethodInfo _netBodyTryGetById;
        private static PropertyInfo _netBodyIsLocal;
        private static MemberInfo _netBodyNetId;
        private static PropertyInfo _netBodyChara;

        private static Type _netPlayerType;
        private static FieldInfo _netPlayerLocalPlayer;
        private static FieldInfo _netPlayerClientIdDict;
        private static EventInfo _netPlayerOnPlayerLeft;
        private static MethodInfo _netPlayerTryGetByClient;
        private static MethodInfo _netPlayerTryGetNetBody;
        private static FieldInfo _netPlayerSteamId;
        private static PropertyInfo _netPlayerClientId;
        private static PropertyInfo _netPlayerPlayerbody;

        private static Type _krokoshaScavMpType;
        private static FieldInfo _krokoshaScavMpIsServerField;
        private static PropertyInfo _krokoshaScavMpIsServerProp;
        private static FieldInfo _krokoshaScavMpRunningField;
        private static PropertyInfo _krokoshaScavMpRunningProp;

        private static Type _serverMainType;
        private static PropertyInfo _serverMainAllClientIdsProp;
        private static FieldInfo _serverMainAllClientIdsField;

        // 缓存 clientId/netId 的实际运行时类型，用于在 uint 与该类型之间装箱/拆箱互转。
        private static Type _clientIdType;

        public static MethodInfo InvokeServerMessageMethod => _netInvokeServerMessage;
        public static MethodInfo InvokeClientMessageMethod => _netInvokeClientMessage;
        public static Type NetPlayerType => _netPlayerType;

        /// <summary>启动时调用一次；探测 KrokoshaCasualtiesMP 程序集并缓存反射句柄。</summary>
        public static void Init()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                Assembly krokoshaAsm = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name == "KrokoshaCasualtiesMP") { krokoshaAsm = asm; break; }
                }
                if (krokoshaAsm == null)
                {
                    SkinSyncMod.ModLog.Info("KrokoshaBridge: KrokoshaCasualtiesMP not found, multiplayer features disabled.");
                    return;
                }

                _netType = krokoshaAsm.GetType("KrokoshaCasualtiesMP.Net");
                _netBodyType = krokoshaAsm.GetType("KrokoshaCasualtiesMP.NetBody");
                _netPlayerType = krokoshaAsm.GetType("KrokoshaCasualtiesMP.NetPlayer");
                _krokoshaScavMpType = krokoshaAsm.GetType("KrokoshaCasualtiesMP.KrokoshaScavMultiplayer");
                _serverMainType = krokoshaAsm.GetType("KrokoshaCasualtiesMP.ServerMain");

                if (_netType == null || _netBodyType == null || _netPlayerType == null)
                {
                    SkinSyncMod.ModLog.Warning("KrokoshaBridge: critical types missing, multiplayer features disabled.");
                    return;
                }

                const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                const BindingFlags AnyInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

                _netClientSend = FindStaticMethodByArity(_netType, "Client_Send", 2);
                _netInvokeServerMessage = FindStaticMethodByArity(_netType, "InvokeServerMessage", 2);
                _netInvokeClientMessage = FindStaticMethodByArity(_netType, "InvokeClientMessage", 2);

                _netBodyAllInstances = _netBodyType.GetField("all_instances", AnyStatic);
                _netBodyTryGetById = FindStaticMethodByArity(_netBodyType, "TryGetNetBodyFromId", 2);
                _netBodyIsLocal = _netBodyType.GetProperty("is_local", AnyInstance);

                _netBodyNetId = (MemberInfo)_netBodyType.GetProperty("netId", AnyInstance) ?? _netBodyType.GetField("netId", AnyInstance);
                _netBodyChara = _netBodyType.GetProperty("chara", AnyInstance);

                _netPlayerLocalPlayer = _netPlayerType.GetField("LOCAL_PLAYER", AnyStatic);
                _netPlayerClientIdDict = _netPlayerType.GetField("ClientIdToPlayerDict", AnyStatic);
                _netPlayerOnPlayerLeft = _netPlayerType.GetEvent("OnPlayerLeft", AnyStatic);
                _netPlayerTryGetByClient = FindStaticMethodByArity(_netPlayerType, "TryGetPlayerFromClientId", 2);
                _netPlayerTryGetNetBody = FindInstanceMethodByArity(_netPlayerType, "TryGetNetBody", 1);
                _netPlayerSteamId = _netPlayerType.GetField("steam_id", AnyInstance);
                _netPlayerClientId = _netPlayerType.GetProperty("clientId", AnyInstance);
                _netPlayerPlayerbody = _netPlayerType.GetProperty("playerbody", AnyInstance);

                // clientId/netId 的实际类型。
                _clientIdType = _netPlayerClientId != null ? _netPlayerClientId.PropertyType : null;
                // 按 clientId 元素类型匹配可枚举的发送重载。
                _netServerSendToClients = FindServerSendToClientsEnumerable(_netType, _clientIdType);

                if (_krokoshaScavMpType != null)
                {
                    _krokoshaScavMpIsServerField = _krokoshaScavMpType.GetField("is_server", AnyStatic);
                    if (_krokoshaScavMpIsServerField == null)
                        _krokoshaScavMpIsServerProp = _krokoshaScavMpType.GetProperty("is_server", AnyStatic);
                    _krokoshaScavMpRunningField = _krokoshaScavMpType.GetField("network_system_is_running", AnyStatic);
                    if (_krokoshaScavMpRunningField == null)
                        _krokoshaScavMpRunningProp = _krokoshaScavMpType.GetProperty("network_system_is_running", AnyStatic);
                }
                if (_serverMainType != null)
                {
                    _serverMainAllClientIdsProp = _serverMainType.GetProperty("AllClientIds", BindingFlags.Public | BindingFlags.Static);
                    _serverMainAllClientIdsField = _serverMainType.GetField("AllClientIds", BindingFlags.Public | BindingFlags.Static);
                }

                IsAvailable = true;
                SkinSyncMod.ModLog.Info("KrokoshaBridge: KrokoshaCasualtiesMP detected, multiplayer features enabled.");
            }
            catch (Exception ex)
            {
                SkinSyncMod.ModLog.Warning("KrokoshaBridge: init failed: " + ex);
                IsAvailable = false;
            }
        }

        /// <summary>本端发送：等价于 Net.Client_Send(method, writer)；KrokMP 不可用时静默跳过。</summary>
        public static void ClientSend(DeliveryMethod method, NetDataWriter writer)
        {
            if (!IsAvailable || _netClientSend == null) return;
            SafeInvoke(_netClientSend, new object[] { method, writer }, "Client_Send");
        }

        /// <summary>主机广播：等价于 Net.Server_SendToClients(method, writer, ServerMain.AllClientIds)；KrokMP 不可用时静默跳过。</summary>
        public static void ServerBroadcast(DeliveryMethod method, NetDataWriter writer)
        {
            if (!IsAvailable || _netServerSendToClients == null) return;
            object allClients = ResolveAllClientIds();
            if (allClients == null) return;
            SafeInvoke(_netServerSendToClients, new object[] { method, writer, allClients }, "Server_SendToClients");
        }

        /// <summary>主机广播但跳过指定 clientId（消息来源），避免回发给发送方。</summary>
        public static void ServerBroadcastExcept(uint excludeClientId, DeliveryMethod method, NetDataWriter writer)
        {
            if (!IsAvailable || _netServerSendToClients == null) return;
            var all = ResolveAllClientIds() as System.Collections.IEnumerable;
            if (all == null) return;
            var ids = new System.Collections.Generic.List<uint>();
            foreach (var c in all)
            {
                uint cid = UnboxId(c);
                if (cid == excludeClientId) continue;
                ids.Add(cid);
            }
            if (ids.Count == 0) return;
            object targets = BuildClientIdList(ids);
            SafeInvoke(_netServerSendToClients, new object[] { method, writer, targets }, "Server_SendToClients");
        }

        /// <summary>主机单播给指定 clientId（其它玩家不会收到）。</summary>
        public static void ServerSendToClient(uint clientId, DeliveryMethod method, NetDataWriter writer)
        {
            if (!IsAvailable || _netServerSendToClients == null) return;
            object single = BuildClientIdList(new uint[] { clientId });
            SafeInvoke(_netServerSendToClients, new object[] { method, writer, single }, "Server_SendToClients");
        }

        // 反射调用发送方法的统一入口：拆包 TargetInvocationException，任何异常只警告不上抛，
        // 避免发送失败击垮整个换肤流程。
        private static void SafeInvoke(MethodInfo m, object[] args, string label)
        {
            try
            {
                m.Invoke(null, args);
            }
            catch (TargetInvocationException tie)
            {
                SkinSyncMod.ModLog.Warning("KrokoshaBridge: " + label + " 发送失败：" + (tie.InnerException ?? tie));
            }
            catch (Exception ex)
            {
                SkinSyncMod.ModLog.Warning("KrokoshaBridge: " + label + " 发送失败：" + ex);
            }
        }

        /// <summary>当前是否处于 KrokMP 主机模式；KrokMP 不可用时返回 false。</summary>
        public static bool IsServer()
        {
            if (!IsAvailable) return false;
            try
            {
                if (_krokoshaScavMpIsServerField != null)
                    return (bool)_krokoshaScavMpIsServerField.GetValue(null);
                if (_krokoshaScavMpIsServerProp != null)
                    return (bool)_krokoshaScavMpIsServerProp.GetValue(null, null);
            }
            catch { }
            return false;
        }

        /// <summary>当前是否启用了多人会话（network_system_is_running）；KrokMP 不可用或字段缺失返回 false。</summary>
        public static bool IsNetworkRunning()
        {
            if (!IsAvailable) return false;
            try
            {
                if (_krokoshaScavMpRunningField != null)
                    return (bool)_krokoshaScavMpRunningField.GetValue(null);
                if (_krokoshaScavMpRunningProp != null)
                    return (bool)_krokoshaScavMpRunningProp.GetValue(null, null);
            }
            catch { }
            return false;
        }

        private static object ResolveAllClientIds()
        {
            try
            {
                if (_serverMainAllClientIdsProp != null)
                    return _serverMainAllClientIdsProp.GetValue(null, null);
                if (_serverMainAllClientIdsField != null)
                    return _serverMainAllClientIdsField.GetValue(null);
            }
            catch { }
            return null;
        }

        /// <summary>找本地 NetBody；返回装箱句柄与拆解后的 netId / chara / isLocal。KrokMP 不可用或没找到返回 false。</summary>
        public static bool TryGetLocalNetBody(out object box, out uint netId, out GameObject chara)
        {
            box = null;
            netId = 0;
            chara = null;
            if (!IsAvailable || _netBodyType == null) return false;
            try
            {
                IEnumerable list = _netBodyAllInstances?.GetValue(null) as IEnumerable;
                if (list != null)
                {
                    foreach (var nb in list)
                    {
                        if (nb == null) continue;
                        bool isLocal = (bool)(_netBodyIsLocal?.GetValue(nb) ?? false);
                        if (!isLocal) continue;
                        box = nb;
                        netId = ReadUInt(_netBodyNetId, nb);
                        chara = _netBodyChara?.GetValue(nb) as GameObject;
                        return true;
                    }
                }

                object localPlr = _netPlayerLocalPlayer?.GetValue(null);
                if (localPlr != null)
                {
                    object playerbody = _netPlayerPlayerbody?.GetValue(localPlr);
                    if (playerbody != null)
                    {
                        box = playerbody;
                        netId = ReadUInt(_netBodyNetId, playerbody);
                        chara = _netBodyChara?.GetValue(playerbody) as GameObject;
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>从已知装箱句柄读出 netId / chara / isLocal；调用方持有的 box 必为 NetBody 实例。</summary>
        public static bool TryReadNetBody(object box, out uint netId, out GameObject chara, out bool isLocal)
        {
            netId = 0;
            chara = null;
            isLocal = false;
            if (!IsAvailable || box == null) return false;
            try
            {
                netId = ReadUInt(_netBodyNetId, box);
                chara = _netBodyChara?.GetValue(box) as GameObject;
                isLocal = (bool)(_netBodyIsLocal?.GetValue(box) ?? false);
                return true;
            }
            catch { return false; }
        }

        /// <summary>NetBody.TryGetNetBodyFromId 反射版：返回装箱 NetBody。</summary>
        public static bool TryGetNetBodyFromId(uint netId, out object box, out GameObject chara, out bool isLocal)
        {
            box = null;
            chara = null;
            isLocal = false;
            if (!IsAvailable || _netBodyTryGetById == null) return false;
            try
            {
                Type idParamType = _netBodyTryGetById.GetParameters()[0].ParameterType;
                var args = new object[] { BoxId(netId, idParamType), null };
                bool ok = (bool)_netBodyTryGetById.Invoke(null, args);
                if (!ok || args[1] == null) return false;
                box = args[1];
                chara = _netBodyChara?.GetValue(box) as GameObject;
                isLocal = (bool)(_netBodyIsLocal?.GetValue(box) ?? false);
                return true;
            }
            catch { return false; }
        }

        private static uint ReadUInt(MemberInfo m, object instance)
        {
            if (m == null || instance == null) return 0;
            object v = null;
            try
            {
                if (m is FieldInfo fi) v = fi.GetValue(instance);
                else if (m is PropertyInfo pi) v = pi.GetValue(instance);
            }
            catch { return 0; }
            return UnboxId(v);
        }

        /// <summary>读 NetPlayer.LOCAL_PLAYER.clientId；KrokMP 不可用或本地玩家未就绪返回 false。</summary>
        public static bool TryGetLocalClientId(out uint clientId)
        {
            clientId = 0;
            if (!IsAvailable) return false;
            try
            {
                object localPlr = _netPlayerLocalPlayer?.GetValue(null);
                if (localPlr == null) return false;
                clientId = ReadUInt(_netPlayerClientId, localPlr);
                return clientId != 0;
            }
            catch { return false; }
        }

        /// <summary>读 NetPlayer.LOCAL_PLAYER.steam_id；KrokMP 不可用或本地玩家未就绪返回 0。</summary>
        public static ulong TryGetLocalSteamId()
        {
            if (!IsAvailable) return 0UL;
            try
            {
                object localPlr = _netPlayerLocalPlayer?.GetValue(null);
                if (localPlr == null) return 0UL;
                object v = _netPlayerSteamId?.GetValue(localPlr);
                if (v == null) return 0UL;
                return Convert.ToUInt64(v);
            }
            catch { return 0UL; }
        }

        /// <summary>NetPlayer.TryGetPlayerFromClientId 反射版，返回装箱 NetPlayer 与解出的 steam_id。</summary>
        public static bool TryGetPlayerSteamId(uint clientId, out ulong steamId)
        {
            steamId = 0UL;
            if (!IsAvailable || _netPlayerTryGetByClient == null) return false;
            try
            {
                Type idParamType = _netPlayerTryGetByClient.GetParameters()[0].ParameterType;
                var args = new object[] { BoxId(clientId, idParamType), null };
                bool ok = (bool)_netPlayerTryGetByClient.Invoke(null, args);
                if (!ok || args[1] == null) return false;
                object v = _netPlayerSteamId?.GetValue(args[1]);
                if (v == null) return false;
                steamId = Convert.ToUInt64(v);
                return true;
            }
            catch { return false; }
        }

        /// <summary>枚举每个 NetPlayer 的 (clientId, steamId, NetBody chara, NetBody netId)，跳过没有 NetBody 或 steam_id=0 的。</summary>
        public static IEnumerable<PlayerEntry> EnumeratePlayersWithBody()
        {
            if (!IsAvailable || _netPlayerClientIdDict == null) yield break;
            IDictionary dict = null;
            try { dict = _netPlayerClientIdDict.GetValue(null) as IDictionary; }
            catch { dict = null; }
            if (dict == null) yield break;

            foreach (DictionaryEntry kv in dict)
            {
                object plr = kv.Value;
                if (plr == null) continue;
                uint clientId = ReadUInt(_netPlayerClientId, plr);
                ulong steamId = 0UL;
                try
                {
                    object sv = _netPlayerSteamId?.GetValue(plr);
                    if (sv != null) steamId = Convert.ToUInt64(sv);
                }
                catch { steamId = 0UL; }

                object box = null;
                GameObject chara = null;
                uint netId = 0;
                if (_netPlayerTryGetNetBody != null)
                {
                    var args = new object[] { null };
                    bool ok = false;
                    try { ok = (bool)_netPlayerTryGetNetBody.Invoke(plr, args); }
                    catch { ok = false; }
                    if (ok && args[0] != null)
                    {
                        box = args[0];
                        chara = _netBodyChara?.GetValue(box) as GameObject;
                        netId = ReadUInt(_netBodyNetId, box);
                    }
                }

                yield return new PlayerEntry
                {
                    ClientId = clientId,
                    SteamId = steamId,
                    NetBodyBox = box,
                    Chara = chara,
                    NetId = netId,
                };
            }
        }

        public struct PlayerEntry
        {
            public uint ClientId;
            public ulong SteamId;
            public object NetBodyBox;
            public GameObject Chara;
            public uint NetId;
        }


        /// <summary>订阅 NetPlayer.OnPlayerLeft 静态事件，回调拿到 (clientId)；KrokMP 不可用时返回不可订阅句柄。</summary>
        public static IDisposable SubscribePlayerLeft(Action<uint> onLeft)
        {
            if (!IsAvailable || _netPlayerOnPlayerLeft == null || onLeft == null) return new NullDisposable();
            try
            {
                Type handlerType = _netPlayerOnPlayerLeft.EventHandlerType;
                MethodInfo invoke = handlerType.GetMethod("Invoke");
                ParameterInfo[] ps = invoke.GetParameters();
                if (ps.Length != 1) return new NullDisposable();

                Type plrType = ps[0].ParameterType;
                ParameterExpression plrParam = Expression.Parameter(plrType, "plr");
                Expression cidExpr;
                if (_netPlayerClientId != null && _netPlayerClientId.DeclaringType.IsAssignableFrom(plrType))
                {
                    // 统一装箱后经 ToUInt 取回 uint。
                    Expression prop = Expression.Property(plrParam, _netPlayerClientId);
                    Expression boxed = Expression.Convert(prop, typeof(object));
                    MethodInfo toUInt = typeof(KrokoshaBridge).GetMethod("ToUInt", BindingFlags.Public | BindingFlags.Static);
                    cidExpr = Expression.Call(toUInt, boxed);
                }
                else
                {
                    cidExpr = Expression.Constant(0u);
                }

                Expression call = Expression.Invoke(Expression.Constant(onLeft), cidExpr);
                Delegate handler = Expression.Lambda(handlerType, call, plrParam).Compile();

                _netPlayerOnPlayerLeft.AddEventHandler(null, handler);
                return new EventUnsubscribe(_netPlayerOnPlayerLeft, handler);
            }
            catch
            {
                return new NullDisposable();
            }
        }

        private sealed class NullDisposable : IDisposable
        {
            public void Dispose() { }
        }

        private sealed class EventUnsubscribe : IDisposable
        {
            private EventInfo _ev;
            private Delegate _handler;
            public EventUnsubscribe(EventInfo ev, Delegate handler) { _ev = ev; _handler = handler; }
            public void Dispose()
            {
                try { _ev?.RemoveEventHandler(null, _handler); }
                catch { }
                _ev = null;
                _handler = null;
            }
        }

        private static MethodInfo FindStaticMethodByArity(Type type, string name, int paramCount)
        {
            if (type == null) return null;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            MethodInfo found = null;
            foreach (var m in type.GetMethods(flags))
            {
                if (m.Name != name) continue;
                if (m.GetParameters().Length != paramCount) continue;
                if (found != null) return null;
                found = m;
            }
            return found;
        }

        private static MethodInfo FindInstanceMethodByArity(Type type, string name, int paramCount)
        {
            if (type == null) return null;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            MethodInfo found = null;
            foreach (var m in type.GetMethods(flags))
            {
                if (m.Name != name) continue;
                if (m.GetParameters().Length != paramCount) continue;
                if (found != null) return null;
                found = m;
            }
            return found;
        }

        // 找以「clientId 元素类型」为泛型参数的可枚举发送重载。
        private static MethodInfo FindServerSendToClientsEnumerable(Type netType, Type elemType)
        {
            if (netType == null) return null;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            MethodInfo legacyUintList = null;
            foreach (var m in netType.GetMethods(flags))
            {
                if (m.Name != "Server_SendToClients") continue;
                var ps = m.GetParameters();
                if (ps.Length != 3) continue;
                Type third = ps[2].ParameterType;
                if (third.IsByRef) third = third.GetElementType();
                if (third == null || !third.IsGenericType) continue;
                if (!typeof(System.Collections.IEnumerable).IsAssignableFrom(third)) continue;
                Type genArg = third.GetGenericArguments()[0];
                if (elemType != null && genArg == elemType) return m;
                if (genArg == typeof(uint)) legacyUintList = m;
            }
            return legacyUintList;
        }

        // 构造一个元素类型与当前 clientId 类型一致的 List，供发送方法使用。
        private static object BuildClientIdList(IEnumerable<uint> ids)
        {
            Type elem = _clientIdType ?? typeof(uint);
            Type listType = typeof(List<>).MakeGenericType(elem);
            var list = (IList)Activator.CreateInstance(listType);
            foreach (uint id in ids) list.Add(BoxId(id, elem));
            return list;
        }

        /// <summary>把 clientId/netId 值统一取成 uint。</summary>
        public static uint ToUInt(object v)
        {
            return UnboxId(v);
        }

        // 将 ID 值拆成 uint：原生整型直接转；结构体读其首个整型字段，或经隐式/显式转换运算符。
        internal static uint UnboxId(object v)
        {
            if (v == null) return 0;
            Type t = v.GetType();
            if (t.IsPrimitive)
            {
                try { return Convert.ToUInt32(v); } catch { return 0; }
            }
            try
            {
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (f.FieldType.IsPrimitive)
                        return Convert.ToUInt32(f.GetValue(v));
                }
            }
            catch { }
            try
            {
                foreach (var op in t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (op.Name != "op_Implicit" && op.Name != "op_Explicit") continue;
                    var op0 = op.GetParameters();
                    if (op.ReturnType.IsPrimitive && op0.Length == 1 && op0[0].ParameterType == t)
                        return Convert.ToUInt32(op.Invoke(null, new[] { v }));
                }
            }
            catch { }
            return 0;
        }

        // 将 uint 装回目标 ID 类型：整型直接转；结构体则用单参构造函数或隐式转换运算符。
        internal static object BoxId(uint v, Type t)
        {
            if (t == null || t == typeof(uint)) return v;
            if (t == typeof(ushort)) return (ushort)v;
            if (t == typeof(ulong)) return (ulong)v;
            if (t == typeof(int)) return (int)v;
            if (t == typeof(long)) return (long)v;
            try
            {
                foreach (var c in t.GetConstructors())
                {
                    var cp = c.GetParameters();
                    if (cp.Length == 1 && cp[0].ParameterType.IsPrimitive)
                        return c.Invoke(new object[] { Convert.ChangeType(v, cp[0].ParameterType) });
                }
            }
            catch { }
            try
            {
                foreach (var op in t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (op.Name != "op_Implicit") continue;
                    var op0 = op.GetParameters();
                    if (op.ReturnType == t && op0.Length == 1 && op0[0].ParameterType.IsPrimitive)
                        return op.Invoke(null, new object[] { Convert.ChangeType(v, op0[0].ParameterType) });
                }
            }
            catch { }
            return v;
        }
    }
}
