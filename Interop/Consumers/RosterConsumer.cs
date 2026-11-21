// SkinSync Interop
// Copyright (c) 2026 Bytechey
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace SkinSyncMod.Interop.Consumers
{
    /// <summary>查询联机名册中的玩家身份、NetBody 实体编号与角色对象。</summary>
    internal static class RosterConsumer
    {
        private static readonly object Sync = new object();

        private static volatile bool _resolved;
        private static Type _netBodyType;
        private static Type _knetidType;
        private static MethodInfo _tryGetLocalNetBody;
        private static MethodInfo _tryGetNetBodyFromId;
        private static MethodInfo _tryGetPlayerAndNetBodyFromClientId;
        private static PropertyInfo _netBodyNetId;
        private static PropertyInfo _netBodyBody;
        private static FieldInfo _knetidId;
        private static ConstructorInfo _knetidCtor;

        private static uint _localNetId;

        internal struct PlayerEntry
        {
            internal uint ClientId;
            internal ulong SteamId;
            internal object NetBodyBox;
            internal GameObject Chara;
            internal uint NetId;
        }

        internal static void ResetLocalCache()
        {
            _localNetId = 0;
        }

        /// <summary>获取本地玩家的 NetBody 实例、实体 netId 与角色对象，联机运行中按本地 ClientId 强校验归属以防观战错绑队友。</summary>
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
            if (InteropReflection.TryInvoke(_tryGetLocalNetBody, null, args, out object result)
                && result is bool found && found && args[0] != null)
            {
                object candidateBox = args[0];
                uint candidateNetId = ToClientId(InteropReflection.ReadMember(_netBodyNetId, candidateBox));
                GameObject candidateChara = ReadChara(candidateBox);
                if (candidateNetId != 0 && candidateChara != null)
                {
                    if (SessionConsumer.IsRunning && TryGetLocalClientId(out uint authLocalCid))
                    {
                        if (TryGetNetBodyOfClient(authLocalCid, out uint authNetId) && authNetId != 0)
                        {
                            if (candidateNetId != authNetId)
                            {
                                if (TryGetNetBody(authNetId, out object authBox) && authBox != null)
                                {
                                    GameObject authChara = ReadChara(authBox);
                                    if (authChara != null)
                                    {
                                        box = authBox;
                                        netId = authNetId;
                                        chara = authChara;
                                        return true;
                                    }
                                }

                                return false;
                            }
                        }
                        else if (InteropBootstrap.Initialize().Roster.TryGetPlayerByActor(candidateBox, out InteropPlayerRef owner)
                            && owner.IsValid && !owner.IsLocal)
                        {
                            return false;
                        }
                    }

                    box = candidateBox;
                    netId = candidateNetId;
                    chara = candidateChara;
                    return true;
                }
            }

            // 换物种重建网格早于相机重绑触发钩子时，旧 NetBody 已被剥离 syncBody 导致 netId 读为 0，需按本地 ClientId 回退反查新实例。
            if (TryGetLocalClientId(out uint localCid)
                && TryGetNetBodyOfClient(localCid, out uint fallbackNetId)
                && fallbackNetId != 0
                && TryGetNetBody(fallbackNetId, out object fallbackBox)
                && fallbackBox != null)
            {
                GameObject fallbackChara = ReadChara(fallbackBox);
                if (fallbackChara != null)
                {
                    box = fallbackBox;
                    netId = fallbackNetId;
                    chara = fallbackChara;
                    return true;
                }
            }

            box = null;
            netId = 0;
            chara = null;
            return false;
        }

        /// <summary>从 NetBody 实例提取实体 netId、角色对象与本机归属标记。</summary>
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

        /// <summary>按客户端连接 ID 反查其当前身体实体的 netId。</summary>
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

        /// <summary>按实体 netId 查找对应的 NetBody 与角色对象。</summary>
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

        /// <summary>按 Body 实例反查所属联机玩家的实体 netId、角色对象与本机归属标记。</summary>
        internal static bool TryResolveBodyIdentity(Body body, out uint netId, out GameObject chara, out bool isLocal)
        {
            netId = 0;
            chara = null;
            isLocal = false;
            if (body == null || !SessionConsumer.IsAvailable)
            {
                return false;
            }

            if (!InteropBootstrap.Initialize().Roster.TryGetPlayerByActor(body, out InteropPlayerRef player) || !player.IsValid)
            {
                return false;
            }

            isLocal = player.IsLocal;
            if (!TryGetNetBodyOfClient(player.ClientId, out netId) || netId == 0)
            {
                chara = body.gameObject;
                return false;
            }

            if (!TryGetNetBodyFromId(netId, out _, out chara, out _))
            {
                chara = body.gameObject;
            }

            return true;
        }

        /// <summary>获取本地玩家的客户端连接 ID（房主为 0）。</summary>
        internal static bool TryGetLocalClientId(out uint clientId)
        {
            clientId = 0;
            if (!SessionConsumer.IsAvailable)
            {
                return false;
            }

            InteropPlayerRef local = InteropBootstrap.Initialize().Roster.LocalPlayer;
            if (!local.IsValid)
            {
                return false;
            }

            clientId = local.ClientId;
            return true;
        }

        /// <summary>获取本地玩家的 SteamID，非 Steam 会话或未就绪返回 0。</summary>
        internal static ulong TryGetLocalSteamId()
        {
            if (!SessionConsumer.IsAvailable)
            {
                return 0UL;
            }

            InteropPlayerRef local = InteropBootstrap.Initialize().Roster.LocalPlayer;
            return local.IsValid ? local.SteamId : 0UL;
        }

        /// <summary>按客户端连接 ID 查询玩家的 SteamID。</summary>
        internal static bool TryGetPlayerSteamId(uint clientId, out ulong steamId)
        {
            steamId = 0UL;
            if (!SessionConsumer.IsAvailable)
            {
                return false;
            }

            if (!InteropBootstrap.Initialize().Roster.TryGetPlayer((ushort)clientId, out InteropPlayerRef player))
            {
                return false;
            }

            steamId = player.SteamId;
            return steamId != 0UL;
        }

        /// <summary>枚举当前已绑定身体实体的全部在线玩家。</summary>
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

        /// <summary>订阅远端玩家离开会话事件。</summary>
        internal static bool SubscribePlayerLeft(Action<uint> onLeft)
        {
            if (onLeft == null || !SessionConsumer.IsAvailable)
            {
                return false;
            }

            return InteropBootstrap.Initialize().Events.TrySubscribePlayerLeft(player => onLeft(player.ClientId));
        }

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

        private static uint LocalNetId()
        {
            if (TryGetLocalNetBody(out _, out uint netId, out _) && netId != 0)
            {
                _localNetId = netId;
                return netId;
            }

            return _localNetId;
        }

        private static bool TryMakeKnetid(uint clientId, out object knetid)
        {
            knetid = null;
            if (_knetidCtor == null)
            {
                return false;
            }

            return InteropReflection.TryConstruct(_knetidCtor, new object[] { (ushort)clientId }, out knetid) && knetid != null;
        }

        private static GameObject ReadChara(object netBody)
        {
            object body = InteropReflection.ReadMember(_netBodyBody, netBody);
            Component component = body as Component;
            return component != null ? component.gameObject : null;
        }

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

        private static bool EnsureResolved()
        {
            if (_resolved)
            {
                return _netBodyType != null;
            }

            if (!SessionConsumer.IsAvailable)
            {
                return false;
            }

            lock (Sync)
            {
                if (_resolved)
                {
                    return _netBodyType != null;
                }

                if (!SessionConsumer.IsAvailable)
                {
                    return false;
                }

                IInteropTypeRegistry types = InteropBootstrap.Initialize().Types;
                if (!types.TryResolveType("Together.NetBody", out Type netBodyType)
                    || !types.TryResolveType("Together.knetid", out Type knetidType)
                    || !types.TryResolveType("Together.ScavPlayer", out Type scavPlayerType))
                {
                    InteropDiagnostics.WarnOnce("RosterConsumer.Types", "Together.NetBody / Together.knetid / Together.ScavPlayer 解析失败，netId 相关功能禁用。");
                    _resolved = true;
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
                    _resolved = true;
                    return false;
                }

                _netBodyType = netBodyType;
                _knetidType = knetidType;
                _resolved = true;
                return true;
            }
        }
    }
}
