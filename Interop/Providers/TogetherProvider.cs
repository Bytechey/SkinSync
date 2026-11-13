// SkinSync Interop
// Copyright (c) 2026 Bytechey
// SPDX-License-Identifier: MIT

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace SkinSyncMod.Interop
{
    /// <summary>Casualties: Together 的提供方适配器，全程通过反射访问目标程序集。</summary>
    /// <remarks>
    /// 目标程序集：CasualtiesMP.dll，BepInEx 插件 GUID "CasualtiesMP"，版本 5.5.0。
    /// 依赖的目标类型：Together.Net、Together.Multiplayer、Together.NetBody、Together.ScavPlayer、Together.knetid。
    /// 依赖的传输类型：NetDataWriter、NetDataReader、DeliveryMethod（三者均属传输库自身的 Utils / 根命名空间）。
    /// 发送方法形参带 in，反射得到的是 by-ref 类型，因此成员一律按「名称 + 形参个数 + 剥掉 in/ref 后的元素类型」匹配。
    /// 目标程序集尚未加载时不计为解析失败，下一次访问重试；程序集已加载但成员缺失时整个提供方判为不可用，缺失项只警告一次。
    /// 16 位原语（写入器 Put(ushort)、读取器 GetUShort()）例外：缺失时只降级该原语，提供方与其余能力保持可用。
    /// </remarks>
    internal sealed class TogetherProvider : IInteropProvider, IInteropSession, IInteropRoster, IInteropChannel, IInteropEvents, IInteropTypeRegistry
    {
        private const string TargetAssemblyName = "CasualtiesMP";

        private readonly object _sync = new object();
        private readonly List<Delegate> _callbacks = new List<Delegate>();

        // 三个标志在锁内写、锁外读（EnsureResolved 的双检锁），必须带 volatile：
        // 锁外读取只做 acquire，否则看到 _resolved 为 true 时其余成员字段仍可能不可见，表现为静默丢包。
        private volatile bool _resolved;
        private volatile bool _available;
        private volatile bool _ushortDegraded;

        private Assembly _assembly;
        private Type _knetidType;
        private Type _writerType;

        private PropertyInfo _netIsRunning;
        private PropertyInfo _netIsServer;
        private PropertyInfo _netIsRunningSteam;
        private MethodInfo _netClientSend;
        private MethodInfo _netServerSendTo;
        private MethodInfo _netServerSendToClients;

        private MethodInfo _createNamedWriter;
        private MethodInfo _registerServerReceiver;
        private MethodInfo _registerClientReceiver;

        private PropertyInfo _netBodyBody;
        private MethodInfo _tryGetPlayerAndNetBody;

        private FieldInfo _scavLocalPlayer;
        private PropertyInfo _scavPlayerId;
        private FieldInfo _scavSteamId;
        private FieldInfo _scavClientIdToPlayerDict;
        private EventInfo _scavPlayerLeft;

        private FieldInfo _knetidId;
        private ConstructorInfo _knetidCtor;

        private MethodInfo _putInt;
        private MethodInfo _putUInt;
        private MethodInfo _putUShort;
        private MethodInfo _putBool;
        private MethodInfo _putFloat;
        private MethodInfo _putString;
        private MethodInfo _putBytes;
        private MethodInfo _getInt;
        private MethodInfo _getUInt;
        private MethodInfo _getUShort;
        private MethodInfo _getBool;
        private MethodInfo _getFloat;
        private MethodInfo _getString;
        private MethodInfo _getBytes;
        private PropertyInfo _readerAvailableBytes;

        private object _deliveryReliable;
        private object _deliveryUnreliable;

        /// <inheritdoc/>
        /// <remarks>16 位原语降级时名称附标记，启动自检行据此输出 Together(ushort降级):ok。</remarks>
        string IInteropProvider.Name => _ushortDegraded ? "Together(ushort降级)" : "Together";

        /// <inheritdoc/>
        bool IInteropProvider.IsAvailable => EnsureResolved();

        /// <inheritdoc/>
        InteropCapability IInteropProvider.Capabilities =>
            InteropCapability.Session
            | InteropCapability.Roster
            | InteropCapability.Channel
            | InteropCapability.Events
            | InteropCapability.TypeRegistry;

        /// <inheritdoc/>
        int IInteropProvider.Priority => 0;

        /// <inheritdoc/>
        InteropSessionInfo IInteropSession.Info
        {
            get
            {
                if (!EnsureResolved())
                {
                    return InteropSessionInfo.None;
                }

                bool running = ReadBool(_netIsRunning);
                bool server = ReadBool(_netIsServer);
                InteropTransport transport = running
                    ? new InteropTransport(ReadBool(_netIsRunningSteam) ? "Steam" : "Direct")
                    : InteropTransport.None;
                return new InteropSessionInfo(running, server, transport);
            }
        }

        /// <inheritdoc/>
        InteropPlayerRef IInteropRoster.LocalPlayer
        {
            get
            {
                if (!EnsureResolved())
                {
                    return InteropPlayerRef.None;
                }

                object local = InteropReflection.ReadMember(_scavLocalPlayer, null);
                return local == null ? InteropPlayerRef.None : ToRef(local, true);
            }
        }

        /// <inheritdoc/>
        bool IInteropRoster.TryGetPlayer(ushort clientId, out InteropPlayerRef player)
        {
            player = InteropPlayerRef.None;
            if (!EnsureResolved() || !TryReadPlayer(clientId, out object raw))
            {
                return false;
            }

            player = ToRef(raw, clientId, IsLocalPlayer(raw));
            return true;
        }

        /// <inheritdoc/>
        IReadOnlyList<InteropPlayerRef> IInteropRoster.GetPlayers()
        {
            List<InteropPlayerRef> players = new List<InteropPlayerRef>();
            if (!EnsureResolved())
            {
                return players;
            }

            IDictionary dictionary = ReadClientIdToPlayerDict();
            if (dictionary == null)
            {
                return players;
            }

            object local = InteropReflection.ReadMember(_scavLocalPlayer, null);
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Value == null)
                {
                    continue;
                }

                players.Add(ToRef(entry.Value, ReadClientId(entry.Key), ReferenceEquals(entry.Value, local)));
            }

            return players;
        }

        /// <remarks>角色对象可以是目标插件的 NetBody，也可以是 NetBody.body 指向的身体对象。</remarks>
        bool IInteropRoster.TryGetPlayerByActor(object actor, out InteropPlayerRef player)
        {
            player = InteropPlayerRef.None;
            if (actor == null || !EnsureResolved())
            {
                return false;
            }

            IDictionary dictionary = ReadClientIdToPlayerDict();
            if (dictionary == null)
            {
                return false;
            }

            object local = InteropReflection.ReadMember(_scavLocalPlayer, null);
            foreach (DictionaryEntry entry in dictionary)
            {
                object[] args = { entry.Key, null, null };
                if (!InteropReflection.TryInvoke(_tryGetPlayerAndNetBody, null, args, out object result))
                {
                    continue;
                }

                if (!(result is bool found) || !found || args[2] == null)
                {
                    continue;
                }

                if (!ReferenceEquals(args[2], actor) && !ReferenceEquals(InteropReflection.ReadMember(_netBodyBody, args[2]), actor))
                {
                    continue;
                }

                player = ToRef(args[1], ReadClientId(entry.Key), ReferenceEquals(args[1], local));
                return true;
            }

            return false;
        }

        /// <inheritdoc/>
        IInteropWriter IInteropChannel.CreateWriter(string channel)
        {
            if (string.IsNullOrEmpty(channel) || !EnsureResolved())
            {
                return NullInteropWriter.Instance;
            }

            if (!InteropReflection.TryInvoke(_createNamedWriter, null, new object[] { channel }, out object writer) || writer == null)
            {
                return NullInteropWriter.Instance;
            }

            return new TogetherWriter(this, writer);
        }

        /// <remarks>可靠标志映射到 DeliveryMethod.ReliableOrdered 与 DeliveryMethod.Unreliable。</remarks>
        void IInteropChannel.SendToServer(IInteropWriter writer, bool reliable)
        {
            object native = ReadWriter(writer);
            if (native == null || !EnsureResolved())
            {
                return;
            }

            object delivery = reliable ? _deliveryReliable : _deliveryUnreliable;
            InteropReflection.TryInvoke(_netClientSend, null, new object[] { delivery, native }, out object ignored);
        }

        /// <remarks>广播目标取自 Together.ScavPlayer.ClientIdToPlayerDict 的全部 clientId。</remarks>
        void IInteropChannel.Broadcast(IInteropWriter writer, bool reliable)
        {
            object native = ReadWriter(writer);
            if (native == null || !EnsureResolved())
            {
                return;
            }

            Array clientIds = BuildClientIdArray();
            if (clientIds == null)
            {
                return;
            }

            object delivery = reliable ? _deliveryReliable : _deliveryUnreliable;
            InteropReflection.TryInvoke(_netServerSendToClients, null, new object[] { delivery, native, clientIds }, out object ignored);
        }

        /// <inheritdoc/>
        void IInteropChannel.SendToClient(ushort clientId, IInteropWriter writer, bool reliable)
        {
            object native = ReadWriter(writer);
            if (native == null || !EnsureResolved())
            {
                return;
            }

            object id = MakeKnetid(clientId);
            if (id == null)
            {
                return;
            }

            object delivery = reliable ? _deliveryReliable : _deliveryUnreliable;
            InteropReflection.TryInvoke(_netServerSendTo, null, new object[] { delivery, native, id }, out object ignored);
        }

        /// <inheritdoc/>
        bool IInteropChannel.Register(string channel, Action<InteropPlayerRef, IInteropReader> onServer, Action<IInteropReader> onClient)
        {
            if (string.IsNullOrEmpty(channel) || (onServer == null && onClient == null) || !EnsureResolved())
            {
                return false;
            }

            bool registered = true;
            if (onServer != null)
            {
                registered &= RegisterServerReceiver(channel, onServer);
            }

            if (onClient != null)
            {
                registered &= RegisterClientReceiver(channel, onClient);
            }

            return registered;
        }

        /// <remarks>目标插件 5.5.0 的玩家加入事件 Together.ScavPlayer.OnPlayerJoined 不在本适配器允许访问的成员清单内，固定返回 false。</remarks>
        bool IInteropEvents.TrySubscribePlayerJoined(Action<InteropPlayerRef> handler)
        {
            return false;
        }

        /// <inheritdoc/>
        bool IInteropEvents.TrySubscribePlayerLeft(Action<InteropPlayerRef> handler)
        {
            if (handler == null || !EnsureResolved())
            {
                return false;
            }

            PlayerLeftReceiver receiver = new PlayerLeftReceiver(this, handler);
            MethodInfo callback = InteropReflection.FindMethod(typeof(PlayerLeftReceiver), "Invoke", typeof(object));
            Delegate bound = InteropReflection.BindDelegate(_scavPlayerLeft.EventHandlerType, receiver, callback);
            if (bound == null)
            {
                return false;
            }

            try
            {
                _scavPlayerLeft.AddEventHandler(null, bound);
            }
            catch (Exception)
            {
                return false;
            }

            _callbacks.Add(bound);
            return true;
        }

        /// <remarks>只用 Assembly.GetType 查目标程序集，不扫描 AppDomain；类型成员缺失时仍可解析类型。</remarks>
        bool IInteropTypeRegistry.TryResolveType(string fullName, out Type type)
        {
            type = null;
            EnsureResolved();
            if (_assembly == null)
            {
                return false;
            }

            type = InteropReflection.FindType(_assembly, fullName);
            return type != null;
        }

        /// <summary>首次访问时解析目标程序集及全部依赖成员并缓存；目标程序集尚未加载时不置缓存，下次访问重试。</summary>
        /// <remarks>目标程序集不存在属于可选场景，不计失败也不记警告；成员解析失败后缓存并只警告一次，重新解析走 <see cref="TryRefresh"/> 显式入口。</remarks>
        private bool EnsureResolved()
        {
            if (_resolved)
            {
                return _available;
            }

            lock (_sync)
            {
                if (_resolved)
                {
                    return _available;
                }

                Assembly assembly = FindTargetAssembly();
                if (assembly == null)
                {
                    return false;
                }

                List<string> missing = new List<string>();
                _available = Resolve(assembly, missing);
                if (!_available)
                {
                    InteropDiagnostics.WarnOnce("TogetherProvider", "CasualtiesMP 成员缺失：" + string.Join(", ", missing));
                }
                else
                {
                    ReportPrimitiveDegradation();
                    ReportResolvedMembers();
                }

                // 标志最后发布：锁外的读者看到 _resolved 为 true 时，此前写入的成员字段与降级标志均已可见。
                _resolved = true;
            }

            return _available;
        }

        /// <summary>丢弃已缓存的解析结果并重新解析，用于目标插件晚加载或成员变化后的显式重试。</summary>
        internal bool TryRefresh()
        {
            lock (_sync)
            {
                _resolved = false;
                _available = false;
            }

            return EnsureResolved();
        }

        /// <summary>16 位原语缺失时各警告一次；降级只影响该原语，提供方整体仍可用。</summary>
        private void ReportPrimitiveDegradation()
        {
            _ushortDegraded = _putUShort == null || _getUShort == null;
            if (_putUShort == null)
            {
                InteropDiagnostics.WarnOnce("TogetherProvider.PutUShort", "目标写入器缺少 Put(ushort)：PutUShort 降级为 32 位写入");
            }

            if (_getUShort == null)
            {
                InteropDiagnostics.WarnOnce("TogetherProvider.GetUShort", "目标读取器缺少 GetUShort()：GetUShort 返回 0");
            }
        }

        /// <summary>Debug 构建用：统计本次解析后非空缓存的反射成员数量。</summary>
        [System.Diagnostics.Conditional("DEBUG")]
        private void ReportResolvedMembers()
        {
            // 成员全员都是 System.Reflection 下的 MemberInfo 字段，按此筛选避免把锁、回调列表、程序集等非成员字段算进去。
            int count = 0;
            FieldInfo[] fields = typeof(TogetherProvider).GetFields(BindingFlags.Instance | BindingFlags.NonPublic);
            for (int i = 0; i < fields.Length; i++)
            {
                if (typeof(MemberInfo).IsAssignableFrom(fields[i].FieldType) && fields[i].GetValue(this) != null)
                {
                    count++;
                }
            }

            SkinSyncMod.ModLog.Debug("TogetherProvider: 已解析 " + count + " 个成员");
        }

        /// <summary>解析目标程序集的全部依赖成员，并把缺失项写进 <paramref name="missing"/>。</summary>
        private bool Resolve(Assembly assembly, List<string> missing)
        {
            _assembly = assembly;
            Type netType = Require(InteropReflection.FindType(_assembly, "Together.Net"), "Together.Net", missing);
            Type multiplayerType = Require(InteropReflection.FindType(_assembly, "Together.Multiplayer"), "Together.Multiplayer", missing);
            Type netBodyType = Require(InteropReflection.FindType(_assembly, "Together.NetBody"), "Together.NetBody", missing);
            Type scavPlayerType = Require(InteropReflection.FindType(_assembly, "Together.ScavPlayer"), "Together.ScavPlayer", missing);
            _knetidType = Require(InteropReflection.FindType(_assembly, "Together.knetid"), "Together.knetid", missing);
            if (netType == null || multiplayerType == null || netBodyType == null || scavPlayerType == null || _knetidType == null)
            {
                return false;
            }

            _createNamedWriter = Require(InteropReflection.FindMethod(multiplayerType, "CreateNamedWriter", typeof(string)), "Together.Multiplayer.CreateNamedWriter(string)", missing);
            if (_createNamedWriter != null)
            {
                _writerType = _createNamedWriter.ReturnType;
            }

            _registerServerReceiver = Require(InteropReflection.FindMethodByCount(multiplayerType, "RegisterCustomServerReceiver", 2), "Together.Multiplayer.RegisterCustomServerReceiver", missing);
            Type readerType = ReceiverReaderType(_registerServerReceiver);
            if (readerType == null)
            {
                missing.Add("Together.Multiplayer.RegisterCustomServerReceiver 的 NetDataReader 形参");
            }
            else
            {
                _registerClientReceiver = Require(InteropReflection.FindMethod(multiplayerType, "RegisterCustomClientReceiver", typeof(string), typeof(Action<>).MakeGenericType(readerType)), "Together.Multiplayer.RegisterCustomClientReceiver(string, Action<NetDataReader>)", missing);
            }

            if (_writerType != null)
            {
                _netClientSend = Require(InteropReflection.FindMethod(netType, "Client_Send", null, _writerType), "Together.Net.Client_Send(DeliveryMethod, NetDataWriter)", missing);
                _netServerSendTo = Require(InteropReflection.FindMethod(netType, "Server_SendTo", null, _writerType, _knetidType), "Together.Net.Server_SendTo(DeliveryMethod, NetDataWriter, knetid)", missing);
                _netServerSendToClients = Require(InteropReflection.FindMethod(netType, "Server_SendToClients", null, _writerType, typeof(IEnumerable<>).MakeGenericType(_knetidType)), "Together.Net.Server_SendToClients(DeliveryMethod, NetDataWriter, IEnumerable<knetid>)", missing);
            }

            Type deliveryType = _netClientSend == null ? null : InteropReflection.StripByRef(_netClientSend.GetParameters()[0].ParameterType);
            _deliveryReliable = InteropReflection.EnumValue(deliveryType, "ReliableOrdered");
            _deliveryUnreliable = InteropReflection.EnumValue(deliveryType, "Unreliable");
            if (_deliveryReliable == null || _deliveryUnreliable == null)
            {
                missing.Add("DeliveryMethod.ReliableOrdered/Unreliable");
            }

            _netIsRunning = Require(InteropReflection.FindProperty(netType, "IsRunning"), "Together.Net.IsRunning", missing);
            _netIsServer = Require(InteropReflection.FindProperty(netType, "IsServer"), "Together.Net.IsServer", missing);
            _netIsRunningSteam = Require(InteropReflection.FindProperty(netType, "IsRunningSteam"), "Together.Net.IsRunningSteam", missing);

            _netBodyBody = Require(InteropReflection.FindProperty(netBodyType, "body"), "Together.NetBody.body", missing);
            _tryGetPlayerAndNetBody = Require(InteropReflection.FindMethod(scavPlayerType, "TryGetPlayerAndNetBodyFromClientId", _knetidType, scavPlayerType, netBodyType), "Together.ScavPlayer.TryGetPlayerAndNetBodyFromClientId(knetid, out ScavPlayer, out NetBody)", missing);

            _scavLocalPlayer = Require(InteropReflection.FindField(scavPlayerType, "LocalPlayer"), "Together.ScavPlayer.LocalPlayer", missing);
            _scavPlayerId = Require(InteropReflection.FindProperty(scavPlayerType, "playerId"), "Together.ScavPlayer.playerId", missing);
            _scavSteamId = Require(InteropReflection.FindField(scavPlayerType, "SteamId"), "Together.ScavPlayer.SteamId", missing);
            _scavClientIdToPlayerDict = Require(InteropReflection.FindField(scavPlayerType, "ClientIdToPlayerDict"), "Together.ScavPlayer.ClientIdToPlayerDict", missing);
            _scavPlayerLeft = Require(InteropReflection.FindEvent(scavPlayerType, "OnPlayerLeft"), "Together.ScavPlayer.OnPlayerLeft", missing);

            _knetidId = Require(InteropReflection.FindField(_knetidType, "id"), "Together.knetid.id", missing);
            _knetidCtor = Require(InteropReflection.FindConstructor(_knetidType, typeof(ushort)), "Together.knetid(ushort)", missing);

            if (_writerType != null)
            {
                _putInt = Require(InteropReflection.FindMethod(_writerType, "Put", typeof(int)), "NetDataWriter.Put(int)", missing);
                _putUInt = Require(InteropReflection.FindMethod(_writerType, "Put", typeof(uint)), "NetDataWriter.Put(uint)", missing);
                // 16 位原语可选：缺失时只降级该原语，不把提供方判为不可用。
                _putUShort = InteropReflection.FindMethod(_writerType, "Put", typeof(ushort));
                _putBool = Require(InteropReflection.FindMethod(_writerType, "Put", typeof(bool)), "NetDataWriter.Put(bool)", missing);
                _putFloat = Require(InteropReflection.FindMethod(_writerType, "Put", typeof(float)), "NetDataWriter.Put(float)", missing);
                _putString = Require(InteropReflection.FindMethod(_writerType, "Put", typeof(string), typeof(int)), "NetDataWriter.Put(string, int)", missing);
                _putBytes = Require(InteropReflection.FindMethod(_writerType, "PutBytesWithLength", typeof(byte[])), "NetDataWriter.PutBytesWithLength(byte[])", missing);
            }

            if (readerType != null)
            {
                _getInt = Require(InteropReflection.FindMethod(readerType, "GetInt"), "NetDataReader.GetInt()", missing);
                _getUInt = Require(InteropReflection.FindMethod(readerType, "GetUInt"), "NetDataReader.GetUInt()", missing);
                // 16 位原语可选：缺失时只降级该原语，不把提供方判为不可用。
                _getUShort = InteropReflection.FindMethod(readerType, "GetUShort");
                _getBool = Require(InteropReflection.FindMethod(readerType, "GetBool"), "NetDataReader.GetBool()", missing);
                _getFloat = Require(InteropReflection.FindMethod(readerType, "GetFloat"), "NetDataReader.GetFloat()", missing);
                _getString = Require(InteropReflection.FindMethod(readerType, "GetString"), "NetDataReader.GetString()", missing);
                _getBytes = Require(InteropReflection.FindMethod(readerType, "GetBytesWithLength"), "NetDataReader.GetBytesWithLength()", missing);
                _readerAvailableBytes = Require(InteropReflection.FindProperty(readerType, "AvailableBytes"), "NetDataReader.AvailableBytes", missing);
            }

            return missing.Count == 0;
        }

        /// <summary>在已加载程序集里按简单名查找目标程序集；不加载新程序集。</summary>
        private static Assembly FindTargetAssembly()
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                if (string.Equals(assemblies[i].GetName().Name, TargetAssemblyName, StringComparison.Ordinal))
                {
                    return assemblies[i];
                }
            }

            return null;
        }

        /// <summary>从 RegisterCustomServerReceiver 的第 2 个形参 Action&lt;ScavPlayer, NetDataReader&gt; 取出 NetDataReader 类型。</summary>
        private static Type ReceiverReaderType(MethodInfo register)
        {
            if (register == null)
            {
                return null;
            }

            ParameterInfo[] parameters = register.GetParameters();
            if (parameters.Length != 2 || !parameters[1].ParameterType.IsGenericType)
            {
                return null;
            }

            Type[] arguments = parameters[1].ParameterType.GetGenericArguments();
            return arguments.Length == 2 ? arguments[1] : null;
        }

        /// <summary>记录缺失成员并把成员原样返回。</summary>
        private static T Require<T>(T member, string name, List<string> missing) where T : class
        {
            if (member == null)
            {
                missing.Add(name);
            }

            return member;
        }

        private static bool ReadBool(MemberInfo member)
        {
            object value = InteropReflection.ReadMember(member, null);
            return value is bool flag && flag;
        }

        private IDictionary ReadClientIdToPlayerDict()
        {
            return InteropReflection.ReadMember(_scavClientIdToPlayerDict, null) as IDictionary;
        }

        private bool TryReadPlayer(ushort clientId, out object player)
        {
            player = null;
            IDictionary dictionary = ReadClientIdToPlayerDict();
            if (dictionary == null)
            {
                return false;
            }

            object key = MakeKnetid(clientId);
            if (key == null || !dictionary.Contains(key))
            {
                return false;
            }

            player = dictionary[key];
            return player != null;
        }

        private object MakeKnetid(ushort clientId)
        {
            InteropReflection.TryConstruct(_knetidCtor, new object[] { clientId }, out object knetid);
            return knetid;
        }

        private ushort ReadClientId(object knetid)
        {
            object id = InteropReflection.ReadMember(_knetidId, knetid);
            return id is ushort value ? value : (ushort)0;
        }

        private bool IsLocalPlayer(object player)
        {
            object local = InteropReflection.ReadMember(_scavLocalPlayer, null);
            return local != null && ReferenceEquals(local, player);
        }

        /// <remarks>玩家或其 playerId 读取失败时返回 <see cref="InteropPlayerRef.None"/>，不产出 clientId=0 的幽灵引用。</remarks>
        private InteropPlayerRef ToRef(object player, bool isLocal)
        {
            if (player == null || !InteropReflection.TryReadMember(_scavPlayerId, player, out object playerId) || playerId == null)
            {
                return InteropPlayerRef.None;
            }

            return ToRef(player, ReadClientId(playerId), isLocal);
        }

        /// <remarks>名称恒为 null：允许访问的成员清单未包含 Together.ScavPlayer.playerName。</remarks>
        private InteropPlayerRef ToRef(object player, ushort clientId, bool isLocal)
        {
            object steamId = InteropReflection.ReadMember(_scavSteamId, player);
            return new InteropPlayerRef(clientId, steamId is ulong id ? id : 0UL, null, isLocal);
        }

        private object ReadWriter(IInteropWriter writer)
        {
            if (writer == null)
            {
                return null;
            }

            object native = writer.NativeHandle;
            return native != null && _writerType != null && _writerType.IsInstanceOfType(native) ? native : null;
        }

        private Array BuildClientIdArray()
        {
            IDictionary dictionary = ReadClientIdToPlayerDict();
            if (dictionary == null || dictionary.Count == 0)
            {
                return null;
            }

            Array ids = Array.CreateInstance(_knetidType, dictionary.Count);
            int index = 0;
            foreach (object key in dictionary.Keys)
            {
                ids.SetValue(key, index);
                index++;
            }

            return ids;
        }

        /// <summary>把写入器交给目标接收器登记方法；委托类型取自目标方法的形参。</summary>
        private static Delegate BindCallback(MethodInfo register, object receiver, MethodInfo callback)
        {
            if (register == null || callback == null)
            {
                return null;
            }

            ParameterInfo[] parameters = register.GetParameters();
            if (parameters.Length != 2)
            {
                return null;
            }

            return InteropReflection.BindDelegate(parameters[1].ParameterType, receiver, callback);
        }

        private bool RegisterServerReceiver(string channel, Action<InteropPlayerRef, IInteropReader> handler)
        {
            ServerReceiver receiver = new ServerReceiver(this, handler);
            MethodInfo callback = InteropReflection.FindMethod(typeof(ServerReceiver), "Invoke", typeof(object), typeof(object));
            return InvokeRegister(_registerServerReceiver, channel, BindCallback(_registerServerReceiver, receiver, callback));
        }

        private bool RegisterClientReceiver(string channel, Action<IInteropReader> handler)
        {
            ClientReceiver receiver = new ClientReceiver(this, handler);
            MethodInfo callback = InteropReflection.FindMethod(typeof(ClientReceiver), "Invoke", typeof(object));
            return InvokeRegister(_registerClientReceiver, channel, BindCallback(_registerClientReceiver, receiver, callback));
        }

        private bool InvokeRegister(MethodInfo register, string channel, Delegate callback)
        {
            if (callback == null)
            {
                return false;
            }

            if (!InteropReflection.TryInvoke(register, null, new object[] { channel, callback }, out object ignored))
            {
                InteropDiagnostics.WarnOnce("TogetherProvider.Register." + channel, "通道接收器登记失败：" + channel);
                return false;
            }

            _callbacks.Add(callback);
            return true;
        }

        private void Put(object writer, MethodInfo method, object value)
        {
            InteropReflection.TryInvoke(method, writer, new object[] { value }, out object ignored);
        }

        /// <summary>写入 16 位无符号整数；目标写入器缺少该原语时降级为 32 位无符号写入。</summary>
        /// <remarks>降级只影响该原语：会话、名册与通道能力保持可用，降级在解析阶段警告一次。</remarks>
        private void PutUShort(object writer, ushort value)
        {
            if (_putUShort != null)
            {
                Put(writer, _putUShort, value);
                return;
            }

            Put(writer, _putUInt, (uint)value);
        }

        /// <remarks>NetDataWriter.Put(string, int) 的 maxLength 形参传 0，表示不限长度。</remarks>
        private void PutString(object writer, string value)
        {
            InteropReflection.TryInvoke(_putString, writer, new object[] { value, 0 }, out object ignored);
        }

        /// <remarks>NetDataWriter.PutBytesWithLength(byte[]) 的长度前缀是 2 字节，超过 65535 字节的负载不写入。</remarks>
        private void PutBytes(object writer, byte[] value)
        {
            if (value == null || value.Length > ushort.MaxValue)
            {
                return;
            }

            InteropReflection.TryInvoke(_putBytes, writer, new object[] { value }, out object ignored);
        }

        private T Get<T>(object reader, MethodInfo method, T fallback)
        {
            return InteropReflection.TryInvoke(method, reader, Array.Empty<object>(), out object result) && result is T value ? value : fallback;
        }

        /// <summary>读取 16 位无符号整数；目标读取器缺少该原语时返回 0，且不推进读取位置。</summary>
        /// <remarks>降级只影响该原语：会话、名册与通道能力保持可用，降级在解析阶段警告一次。</remarks>
        private ushort GetUShort(object reader)
        {
            return _getUShort == null ? (ushort)0 : Get(reader, _getUShort, (ushort)0);
        }

        private int ReadRemainingBytes(object reader)
        {
            object value = InteropReflection.ReadMember(_readerAvailableBytes, reader);
            return value is int count ? count : 0;
        }

        /// <summary>把 NetDataWriter 包成契约写入器，写操作转发给一次性解析好的 Put 重载。</summary>
        /// <remarks>字节数组走 NetDataWriter.PutBytesWithLength(byte[])：先写 2 字节无符号长度，再写原始字节，与 NetDataReader.GetBytesWithLength() 对应。
        /// 字符串走 NetDataWriter.Put(string, int)，maxLength 传 0 表示不限长度。</remarks>
        private sealed class TogetherWriter : IInteropWriter
        {
            private readonly TogetherProvider _provider;
            private readonly object _native;

            internal TogetherWriter(TogetherProvider provider, object native)
            {
                _provider = provider;
                _native = native;
            }

            /// <inheritdoc/>
            object IInteropWriter.NativeHandle => _native;

            /// <inheritdoc/>
            void IInteropWriter.PutInt(int value) => _provider.Put(_native, _provider._putInt, value);

            /// <inheritdoc/>
            void IInteropWriter.PutUInt(uint value) => _provider.Put(_native, _provider._putUInt, value);

            /// <inheritdoc/>
            /// <remarks>目标写入器缺少 16 位原语时降级为 32 位无符号写入。</remarks>
            void IInteropWriter.PutUShort(ushort value) => _provider.PutUShort(_native, value);

            /// <inheritdoc/>
            void IInteropWriter.PutBool(bool value) => _provider.Put(_native, _provider._putBool, value);

            /// <inheritdoc/>
            void IInteropWriter.PutFloat(float value) => _provider.Put(_native, _provider._putFloat, value);

            /// <inheritdoc/>
            void IInteropWriter.PutString(string value) => _provider.PutString(_native, value);

            /// <inheritdoc/>
            void IInteropWriter.PutBytes(byte[] value) => _provider.PutBytes(_native, value);

            /// <inheritdoc/>
            void IInteropWriter.PutVector2(float x, float y)
            {
                _provider.Put(_native, _provider._putFloat, x);
                _provider.Put(_native, _provider._putFloat, y);
            }
        }

        /// <summary>把 NetDataReader 包成契约读取器，读操作转发给一次性解析好的 Get 重载。</summary>
        /// <remarks>包装器不改变读取位置，读位置由底层 NetDataReader 维护；底层读取失败时返回默认值。</remarks>
        private sealed class TogetherReader : IInteropReader
        {
            private readonly TogetherProvider _provider;
            private readonly object _native;

            internal TogetherReader(TogetherProvider provider, object native)
            {
                _provider = provider;
                _native = native;
            }

            /// <inheritdoc/>
            object IInteropReader.NativeHandle => _native;

            /// <inheritdoc/>
            int IInteropReader.RemainingBytes => _provider.ReadRemainingBytes(_native);

            /// <inheritdoc/>
            int IInteropReader.GetInt() => _provider.Get(_native, _provider._getInt, 0);

            /// <inheritdoc/>
            uint IInteropReader.GetUInt() => _provider.Get(_native, _provider._getUInt, 0u);

            /// <inheritdoc/>
            /// <remarks>目标读取器缺少 16 位原语时返回 0，且不推进读取位置。</remarks>
            ushort IInteropReader.GetUShort() => _provider.GetUShort(_native);

            /// <inheritdoc/>
            bool IInteropReader.GetBool() => _provider.Get(_native, _provider._getBool, false);

            /// <inheritdoc/>
            float IInteropReader.GetFloat() => _provider.Get(_native, _provider._getFloat, 0f);

            /// <inheritdoc/>
            string IInteropReader.GetString() => _provider.Get(_native, _provider._getString, string.Empty);

            /// <inheritdoc/>
            byte[] IInteropReader.GetBytes() => _provider.Get(_native, _provider._getBytes, Array.Empty<byte>());

            /// <inheritdoc/>
            (float x, float y) IInteropReader.GetVector2()
            {
                float x = _provider.Get(_native, _provider._getFloat, 0f);
                float y = _provider.Get(_native, _provider._getFloat, 0f);
                return (x, y);
            }
        }

        /// <summary>把 Action&lt;ScavPlayer, NetDataReader&gt; 转发到契约处理器。</summary>
        private sealed class ServerReceiver
        {
            private readonly TogetherProvider _provider;
            private readonly Action<InteropPlayerRef, IInteropReader> _handler;

            internal ServerReceiver(TogetherProvider provider, Action<InteropPlayerRef, IInteropReader> handler)
            {
                _provider = provider;
                _handler = handler;
            }

            private void Invoke(object player, object reader)
            {
                _handler(_provider.ToRef(player, _provider.IsLocalPlayer(player)), new TogetherReader(_provider, reader));
            }
        }

        /// <summary>把 Action&lt;NetDataReader&gt; 转发到契约处理器。</summary>
        private sealed class ClientReceiver
        {
            private readonly TogetherProvider _provider;
            private readonly Action<IInteropReader> _handler;

            internal ClientReceiver(TogetherProvider provider, Action<IInteropReader> handler)
            {
                _provider = provider;
                _handler = handler;
            }

            private void Invoke(object reader)
            {
                _handler(new TogetherReader(_provider, reader));
            }
        }

        /// <summary>把 Action&lt;ScavPlayer&gt; 转发到契约处理器。</summary>
        private sealed class PlayerLeftReceiver
        {
            private readonly TogetherProvider _provider;
            private readonly Action<InteropPlayerRef> _handler;

            internal PlayerLeftReceiver(TogetherProvider provider, Action<InteropPlayerRef> handler)
            {
                _provider = provider;
                _handler = handler;
            }

            private void Invoke(object player)
            {
                _handler(_provider.ToRef(player, _provider.IsLocalPlayer(player)));
            }
        }
    }
}
