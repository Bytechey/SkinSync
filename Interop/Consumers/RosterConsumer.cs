// SkinSync Interop
// Copyright (c) 2026 Bytechey
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace SkinSyncMod.Interop.Consumers
{
    /// <summary>名册消费者：把契约名册与目标插件的身体对象解析成 SkinSync 需要的 netId 与角色对象。</summary>
    /// <remarks>
    /// 目标程序集：CasualtiesMP.dll，BepInPlugin GUID "CasualtiesMP"（Together 5.5.0）。
    /// 依赖的目标类型全名：Together.ScavPlayer、Together.NetBody、Together.knetid。
    /// 名册查询（本地玩家、按 clientId 查、枚举）走契约 IInteropRoster；
    /// 依赖的目标成员全名：Together.ScavPlayer.LocalPlayer（static field）、
    /// Together.ScavPlayer.ClientIdToPlayerDict（static field）、
    /// Together.ScavPlayer.TryGetPlayerAndNetBodyFromClientId(knetid, out ScavPlayer, out NetBody)（static method）、
    /// Together.ScavPlayer.playerId（property）、Together.ScavPlayer.SteamId（field）、
    /// Together.ScavPlayer.OnPlayerLeft（static event）、
    /// Together.NetBody.TryGetLocalNetBody(out NetBody)（static method）、
    /// Together.NetBody.TryGetNetBodyFromId(knetid, out NetBody)（static method）、
    /// Together.NetBody.netId（property，值类型 Together.knetid）、Together.NetBody.body（property，类型 Body）、
    /// Together.knetid(ushort)（constructor）、Together.knetid.id（field）。
    /// 契约名册只承载 clientId / SteamId / 名称，不暴露「netId ↔ 角色对象」；
    /// 该解析由本消费者经 IInteropTypeRegistry 取目标类型、经 InteropReflection 取成员完成，业务代码仍只依赖契约与本类型。
    /// 类型与成员在首次访问时解析并缓存，缺失时只警告一次，之后全部方法返回 false / 空集合。
    /// </remarks>
    internal static class RosterConsumer
    {
        private static readonly object Sync = new object();

        private static bool _resolved;
        private static Type _netBodyType;
        private static Type _knetidType;
        private static MethodInfo _tryGetLocalNetBody;
        private static MethodInfo _tryGetNetBodyFromId;
        private static MethodInfo _tryGetPlayerAndNetBodyFromClientId;
        private static PropertyInfo _netBodyNetId;
        private static PropertyInfo _netBodyBody;
        private static FieldInfo _knetidId;
        private static ConstructorInfo _knetidCtor;

        /// <summary>最后已知的本机 netId；由 LocalNetId() 每次求值后刷新，仅作取不到时的兵底。</summary>
        private static uint _localNetId;

        /// <summary>名册内玩家及其角色对象。</summary>
        internal struct PlayerEntry
        {
            /// <summary>目标插件的客户端 ID，对应 Together.ScavPlayer.playerId。</summary>
            internal uint ClientId;

            /// <summary>Steam ID；非 Steam 会话为 0。</summary>
            internal ulong SteamId;

            /// <summary>Together.NetBody 实例；不参与类型判断，只用于回传。</summary>
            internal object NetBodyBox;

            /// <summary>角色对象，取自 Together.NetBody.body 的 GameObject。</summary>
            internal GameObject Chara;

            /// <summary>netId，取自 Together.NetBody.netId 的 knetid.id。</summary>
            internal uint NetId;
        }

        /// <summary>本机玩家的 NetBody 句柄、netId 与角色对象；未入局或目标成员缺失时返回 false。</summary>
        internal static bool TryGetLocalNetBody(out object box, out uint netId, out GameObject chara)
        {
            box = null;
            netId = 0;
            chara = null;
            if (!EnsureResolved())
            {
                return false;
            }

            object[] args = { null };
            if (!InteropReflection.TryInvoke(_tryGetLocalNetBody, null, args, out object result) || !(result is bool found) || !found)
            {
                return false;
            }

            if (args[0] == null)
            {
                return false;
            }

            box = args[0];
            netId = ToClientId(InteropReflection.ReadMember(_netBodyNetId, box));
            chara = ReadChara(box);
            return true;
        }

        /// <summary>拆解一个 NetBody 句柄为 netId、角色对象与本端标记。</summary>
        internal static bool TryReadNetBody(object box, out uint netId, out GameObject chara, out bool isLocal)
        {
            netId = 0;
            chara = null;
            isLocal = false;
            if (box == null || !EnsureResolved())
            {
                return false;
            }

            netId = ToClientId(InteropReflection.ReadMember(_netBodyNetId, box));
            chara = ReadChara(box);
            isLocal = netId != 0 && netId == LocalNetId();
            return true;
        }

        /// <summary>按客户端 ID 反查该玩家当前 NetBody 的 netId。</summary>
        /// <remarks>
        /// 对应目标插件成员（static method）：Together.ScavPlayer.TryGetPlayerAndNetBodyFromClientId(Together.knetid, out Together.ScavPlayer, out Together.NetBody)。
        /// clientId 属于客户端 ID 空间（主机为 0，客户端依次为 1、2…），netId 由实体系统分配、属于实体 ID 空间，
        /// 两套编号互不相通，因此不能用 netId 直接当作 clientId，也不能把两者直接比较。
        /// </remarks>
        internal static bool TryGetNetBodyOfClient(uint clientId, out uint netId)
        {
            netId = 0;
            if (!EnsureResolved() || !TryMakeKnetid(clientId, out object id))
            {
                return false;
            }

            object[] args = { id, null, null };
            if (!InteropReflection.TryInvoke(_tryGetPlayerAndNetBodyFromClientId, null, args, out object result)
                || !(result is bool found) || !found || args[2] == null)
            {
                return false;
            }

            netId = ToClientId(InteropReflection.ReadMember(_netBodyNetId, args[2]));
            return netId != 0;
        }

        /// <summary>按 netId 反查角色对象；未注册时返回 false。</summary>
        internal static bool TryGetNetBodyFromId(uint netId, out object box, out GameObject chara, out bool isLocal)
        {
            box = null;
            chara = null;
            isLocal = false;
            if (netId == 0 || !TryGetNetBody(netId, out box))
            {
                return false;
            }

            return TryReadNetBody(box, out _, out chara, out isLocal);
        }

        /// <summary>本地玩家 clientId，对应 Together.ScavPlayer.LocalPlayer.playerId；未入局或 ID 为 0 时返回 false。</summary>
        internal static bool TryGetLocalClientId(out uint clientId)
        {
            clientId = 0;
            InteropPlayerRef local = InteropBootstrap.Initialize().Roster.LocalPlayer;
            if (!local.IsValid)
            {
                return false;
            }

            clientId = local.ClientId;
            return clientId != 0;
        }

        /// <summary>本地玩家 SteamId；非 Steam 会话或未入局返回 0。</summary>
        internal static ulong TryGetLocalSteamId()
        {
            InteropPlayerRef local = InteropBootstrap.Initialize().Roster.LocalPlayer;
            return local.IsValid ? local.SteamId : 0UL;
        }

        /// <summary>按 clientId 取 SteamId；未知玩家或 SteamId 为 0 时返回 false。</summary>
        internal static bool TryGetPlayerSteamId(uint clientId, out ulong steamId)
        {
            steamId = 0UL;
            if (!InteropBootstrap.Initialize().Roster.TryGetPlayer((ushort)clientId, out InteropPlayerRef player))
            {
                return false;
            }

            steamId = player.SteamId;
            return steamId != 0UL;
        }

        /// <summary>枚举名册内每个已注册身体对象的玩家；没有身体的玩家跳过。</summary>
        internal static IEnumerable<PlayerEntry> EnumeratePlayersWithBody()
        {
            if (!EnsureResolved())
            {
                yield break;
            }

            IReadOnlyList<InteropPlayerRef> players = InteropBootstrap.Initialize().Roster.GetPlayers();
            for (int i = 0; i < players.Count; i++)
            {
                InteropPlayerRef player = players[i];
                if (!player.IsValid || !TryGetNetBodyOfClient(player.ClientId, out uint playerNetId)
                    || !TryGetNetBody(playerNetId, out object box))
                {
                    continue;
                }

                if (!TryReadNetBody(box, out uint netId, out GameObject chara, out _))
                {
                    continue;
                }

                yield return new PlayerEntry
                {
                    ClientId = player.ClientId,
                    SteamId = player.SteamId,
                    NetBodyBox = box,
                    Chara = chara,
                    NetId = netId,
                };
            }
        }

        /// <summary>订阅玩家离开事件，回调参数为离开者的 clientId。</summary>
        /// <remarks>事件订阅为进程级且无退订 API；目标插件不可用时返回 false。</remarks>
        internal static bool SubscribePlayerLeft(Action<uint> onLeft)
        {
            if (onLeft == null)
            {
                return false;
            }

            return InteropBootstrap.Initialize().Events.TrySubscribePlayerLeft(player => onLeft(player.ClientId));
        }

        /// <summary>按 netId 取 NetBody 句柄，对应 Together.NetBody.TryGetNetBodyFromId(knetid, out NetBody)。</summary>
        private static bool TryGetNetBody(uint netId, out object box)
        {
            box = null;
            if (!EnsureResolved() || !TryMakeKnetid(netId, out object id))
            {
                return false;
            }

            object[] args = { id, null };
            if (!InteropReflection.TryInvoke(_tryGetNetBodyFromId, null, args, out object result) || !(result is bool found) || !found)
            {
                return false;
            }

            box = args[1];
            return box != null;
        }

        /// <summary>
        /// 本机当前 netId。每次调用都重新向目标插件求值，不做永久缓存：
        /// 重连或同场景内角色重建后 netId 会变，旧值会让 isLocal 判定与皮肤上报去重全部失准。
        /// 调用点只有消息处理路径（低频），一次反射开销可忽。
        /// </summary>
        private static uint LocalNetId()
        {
            if (TryGetLocalNetBody(out _, out uint netId, out _))
            {
                _localNetId = netId;
                return netId;
            }

            // 取不到时保留上一次已知值：场景切换的瞬间 NetBody 可能还没就绪，
            // 这时归零会把本地玩家误判成远端。
            return _localNetId;
        }

        /// <summary>构造 Together.knetid；客户端 ID 为 16 位，超出范围时按截断处理。</summary>
        private static bool TryMakeKnetid(uint clientId, out object knetid)
        {
            knetid = null;
            if (_knetidCtor == null)
            {
                return false;
            }

            return InteropReflection.TryConstruct(_knetidCtor, new object[] { (ushort)clientId }, out knetid) && knetid != null;
        }

        /// <summary>读取 Together.NetBody.body 指向的角色对象。</summary>
        private static GameObject ReadChara(object netBody)
        {
            object body = InteropReflection.ReadMember(_netBodyBody, netBody);
            Component component = body as Component;
            return component != null ? component.gameObject : null;
        }

        /// <summary>把 knetid 或整型统一成 clientId；无法转换时返回 0。</summary>
        private static uint ToClientId(object value)
        {
            if (value == null)
            {
                return 0u;
            }

            if (_knetidType != null && _knetidType.IsInstanceOfType(value))
            {
                object raw = InteropReflection.ReadMember(_knetidId, value);
                return ToUInt32(raw);
            }

            return ToUInt32(value);
        }

        private static uint ToUInt32(object value)
        {
            if (value == null)
            {
                return 0u;
            }

            try
            {
                return Convert.ToUInt32(value, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return 0u;
            }
        }

        /// <summary>首次访问时解析目标类型与成员；失败后不再重试，也只警告一次。</summary>
        private static bool EnsureResolved()
        {
            if (_resolved)
            {
                return _netBodyType != null;
            }

            lock (Sync)
            {
                if (_resolved)
                {
                    return _netBodyType != null;
                }

                _resolved = true;
                IInteropTypeRegistry types = InteropBootstrap.Initialize().Types;
                if (!types.TryResolveType("Together.NetBody", out Type netBodyType)
                    || !types.TryResolveType("Together.knetid", out Type knetidType)
                    || !types.TryResolveType("Together.ScavPlayer", out Type scavPlayerType))
                {
                    InteropDiagnostics.WarnOnce("RosterConsumer.Types", "Together.NetBody / Together.knetid / Together.ScavPlayer 解析失败，netId 相关功能禁用。");
                    return false;
                }

                _tryGetLocalNetBody = InteropReflection.FindMethod(netBodyType, "TryGetLocalNetBody", netBodyType);
                _tryGetNetBodyFromId = InteropReflection.FindMethod(netBodyType, "TryGetNetBodyFromId", knetidType, netBodyType);
                _tryGetPlayerAndNetBodyFromClientId = InteropReflection.FindMethod(scavPlayerType, "TryGetPlayerAndNetBodyFromClientId", knetidType, scavPlayerType, netBodyType);
                _netBodyNetId = InteropReflection.FindProperty(netBodyType, "netId");
                _netBodyBody = InteropReflection.FindProperty(netBodyType, "body");
                _knetidId = InteropReflection.FindField(knetidType, "id");
                _knetidCtor = InteropReflection.FindConstructor(knetidType, typeof(ushort));
                if (_tryGetLocalNetBody == null || _tryGetNetBodyFromId == null || _tryGetPlayerAndNetBodyFromClientId == null
                    || _netBodyNetId == null || _netBodyBody == null || _knetidId == null || _knetidCtor == null)
                {
                    InteropDiagnostics.WarnOnce("RosterConsumer.Members", "Together.NetBody / Together.knetid / Together.ScavPlayer 成员缺失，netId 相关功能禁用。");
                    return false;
                }

                _netBodyType = netBodyType;
                _knetidType = knetidType;
                return true;
            }
        }
    }
}
