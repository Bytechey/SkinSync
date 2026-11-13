using SkinSyncMod.Interop;
using SkinSyncMod.Interop.Consumers;
using SkinSyncMod.Network;
using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>
    /// 处理皮肤切换网络消息：客户端收到广播后应用、服务器收到请求后校验并广播。
    /// </summary>
    /// <remarks>消息体由 SkinMessageCodec 在契约读取器/写入器上按字段编解码，发送经 ChannelConsumer 的具名通道。</remarks>
    internal static class SkinMessageHandlers
    {
        internal static void OnClientSkinMessage(uint callerId, IInteropReader reader)
        {
            SkinChangeMessage msg = SkinMessageCodec.ReadSkinChange(reader);

            bool hasLocal = RosterConsumer.TryGetLocalNetBody(out _, out uint localNetId, out _);
            bool hasCid = RosterConsumer.TryGetLocalClientId(out uint localCid);
            bool isLocalEcho = hasLocal && localNetId == msg.netId;
            CommsTrace.ClientReceive(msg.skinID, msg.netId, msg.charType, isLocalEcho);
            // 附加本端上下文：排查"拿到消息却应用错人"时需要对照本地 netId / clientId。
            SkinSyncMod.ModLog.Debug($"客户端接收上下文：local netId {(hasLocal ? localNetId.ToString() : "n/a")}，local clientId {(hasCid ? localCid.ToString() : "n/a")}，echo={isLocalEcho}");

            if (!isLocalEcho)
            {
                ApplySkinToPlayer(msg.netId, msg.skinID, msg.charType);
            }

            // 收到的是本地玩家自己 → 写回 Settings.CurrentSkin，让主菜单 / 单机也用同一个值。
            if (RosterConsumer.TryGetNetBodyFromId(msg.netId, out _, out _, out bool isLocal) && isLocal)
            {
                if (SkinSync.Settings != null) SkinSync.Settings.CurrentSkin.Value = msg.skinID;
            }
        }

        internal static void OnServerSkinMessage(uint callerId, IInteropReader reader)
        {
            SkinChangeMessage msg = SkinMessageCodec.ReadSkinChange(reader);
            CommsTrace.HostReceive(callerId, msg.skinID, msg.netId, msg.charType);

            if (!RosterConsumer.TryGetNetBodyOfClient(callerId, out uint callerNetId))
            {
                CommsTrace.HostRejected(callerId, msg.netId, msg.skinID, "无法反查该 client 的 netId");
                InteropDiagnostics.WarnOnce("SkinMessageHandler.SkinCallerNetId", $"服务端无法反查客户端 {callerId} 的 netId，皮肤切换请求已拒绝。");
                return;
            }

            if (callerNetId != msg.netId)
            {
                CommsTrace.HostRejected(callerId, msg.netId, msg.skinID, $"冒充他人（实际 netId {callerNetId}）");
                SkinSyncMod.ModLog.Warning($"Cheat attempt: client {callerId} (netId {callerNetId}) tried to change skin for {msg.netId}");
                return;
            }

            ApplySkinToPlayer(msg.netId, msg.skinID, msg.charType);

            IInteropWriter writer = ChannelConsumer.CreateWriter(SkinNetworkIDs.SkinChangeMessageId);
            SkinMessageCodec.WriteSkinChange(writer, msg);
            int recipients = ChannelConsumer.ForwardExcept((ushort)callerId, writer, SkinNetworkIDs.SkinChangeMessageId);
            CommsTrace.HostBroadcast(msg.skinID, msg.netId, recipients, callerId);

            Patches.SkinCacheBroadcaster.RecordSkin(msg.netId, msg.skinID);

            if (RosterConsumer.TryGetPlayerSteamId(callerId, out ulong steamId) && steamId != 0UL)
                SkinCacheStore.Set(steamId, msg.skinID);
        }

        /// <param name="charType">
        /// 发送者上报的真实角色类型，0 表示未知。
        /// 远端 Body 的 charType 只是本机 prefab 的角色类型（Together 不同步 characterSelected），
        /// 所以翅膀缩放优先用消息里的值。
        /// </param>
        private static void ApplySkinToPlayer(uint netId, string characterName, int charType = 0)
        {
            if (!RosterConsumer.TryGetNetBodyFromId(netId, out _, out GameObject playerObj, out _))
            {
                Patches.PendingSkinApplier.Enqueue(netId, characterName, "NetBody 未注册");
                SkinSyncMod.ModLog.Info($"NetBody netId {netId} 暂未注册，已加入待应用队列：{characterName}");
                return;
            }
            if (playerObj == null)
            {
                Patches.PendingSkinApplier.Enqueue(netId, characterName, "NetBody 已注册但角色对象为空");
                return;
            }
            SkinApplier.ApplySkinToPlayer(playerObj, characterName, charType);
        }

        // —— Accessory sync —— //

        internal static void OnClientAccessorySync(uint callerId, IInteropReader reader)
        {
            var msg = SkinMessageCodec.ReadAccessory(reader);
            if (SkinSync.Settings != null && SkinSync.Settings.SyncMode.Value == "Passive")
            {
                return;
            }
            ApplyAccessoryOverride(msg);
        }

        internal static void OnServerAccessorySync(uint callerId, IInteropReader reader)
        {
            var msg = SkinMessageCodec.ReadAccessory(reader);
            if (!RosterConsumer.TryGetNetBodyOfClient(callerId, out uint callerNetId))
            {
                InteropDiagnostics.WarnOnce("SkinMessageHandler.AccessoryCallerNetId", $"服务端无法反查客户端 {callerId} 的 netId，配件同步请求已拒绝。");
                return;
            }

            if (callerNetId != msg.netId)
            {
                SkinSyncMod.ModLog.Warning($"Cheat attempt: client {callerId} (netId {callerNetId}) tried accessory sync for {msg.netId}");
                return;
            }
            ApplyAccessoryOverride(msg);

            IInteropWriter writer = ChannelConsumer.CreateWriter(SkinNetworkIDs.AccessorySyncMessageId);
            SkinMessageCodec.WriteAccessory(writer, msg);
            ChannelConsumer.ForwardExcept((ushort)callerId, writer, SkinNetworkIDs.AccessorySyncMessageId);
        }

        private static void ApplyAccessoryOverride(AccessorySyncMessage msg)
        {
            if (SkinSync.Settings == null) return;
            // 写覆盖：5 字段都写。enabled 单独 + 4 个 transform 一并写。
            SkinSync.Settings.SetAccessoryEnabled(msg.skinID, msg.accId, msg.enabled);
            SkinSync.Settings.SetAccessoryTransform(msg.skinID, msg.accId, msg.offX, msg.offY, msg.rotation, msg.zOrder);
            // 把目标玩家的 chara 重新装配一遍——AccessoryAttacher.Apply 内部会读最新 settings 覆盖。
            if (RosterConsumer.TryGetNetBodyFromId(msg.netId, out _, out GameObject chara, out _) && chara != null)
            {
                SkinApplier.ApplySkinToPlayer(chara, msg.skinID);
            }
        }

        // —— Tail sync —— //

        internal static void OnClientTailSync(uint callerId, IInteropReader reader)
        {
            var msg = SkinMessageCodec.ReadTail(reader);
            if (SkinSync.Settings != null && SkinSync.Settings.SyncMode.Value == "Passive")
            {
                return;
            }
            ApplyTailOverride(msg);
        }

        internal static void OnServerTailSync(uint callerId, IInteropReader reader)
        {
            var msg = SkinMessageCodec.ReadTail(reader);
            if (!RosterConsumer.TryGetNetBodyOfClient(callerId, out uint callerNetId))
            {
                InteropDiagnostics.WarnOnce("SkinMessageHandler.TailCallerNetId", $"服务端无法反查客户端 {callerId} 的 netId，尾巴同步请求已拒绝。");
                return;
            }

            if (callerNetId != msg.netId)
            {
                SkinSyncMod.ModLog.Warning($"Cheat attempt: client {callerId} (netId {callerNetId}) tried tail sync for {msg.netId}");
                return;
            }
            ApplyTailOverride(msg);

            IInteropWriter writer = ChannelConsumer.CreateWriter(SkinNetworkIDs.TailSyncMessageId);
            SkinMessageCodec.WriteTail(writer, msg);
            ChannelConsumer.ForwardExcept((ushort)callerId, writer, SkinNetworkIDs.TailSyncMessageId);
        }

        private static void ApplyTailOverride(TailSyncMessage msg)
        {
            if (SkinSync.Settings == null) return;
            // TailDeformConfig 是全局 static——直接覆盖立即生效（所有玩家共享一份尾巴形变参数）。
            TailDeformConfig.Enabled = msg.enabled;
            TailDeformConfig.FrontGuard = msg.frontGuard;
            TailDeformConfig.Segments = msg.segments;
            TailDeformConfig.ConstraintIters = msg.constraintIters;
            TailDeformConfig.Damping = msg.damping;
            TailDeformConfig.SpeedDamping = msg.speedDamping;
            TailDeformConfig.Stiffness = msg.stiffness;
            TailDeformConfig.MaxBendDeg = msg.maxBendDeg;
            TailDeformConfig.AnchorFollow = msg.anchorFollow;
            TailDeformConfig.Smoothness = msg.smoothness;
            TailDeformConfig.MaxStep = msg.maxStep;
            TailDeformConfig.MaxFixedDt = msg.maxFixedDt;
            TailDeformConfig.FrontGuardMargin = msg.frontGuardMargin;
            TailDeformConfig.GravityX = msg.gravityX;
            TailDeformConfig.GravityY = msg.gravityY;
            TailDeformConfig.WindFreq = msg.windFreq;
            TailDeformConfig.WindAmp = msg.windAmp;
            TailDeformConfig.SpeedDisturb = msg.speedDisturb;
            // 同时写到该 skin 的 settings 覆盖，让本端下次切回该皮肤继续保持参数。
            var ov = new SkinSyncSettings.TailDeformOverride
            {
                FrontGuard = msg.frontGuard,
                Segments = msg.segments, ConstraintIters = msg.constraintIters,
                Damping = msg.damping, Stiffness = msg.stiffness,
                SpeedDamping = msg.speedDamping,
                MaxBendDeg = msg.maxBendDeg, AnchorFollow = msg.anchorFollow,
                Smoothness = msg.smoothness, MaxStep = msg.maxStep,
                MaxFixedDt = msg.maxFixedDt, FrontGuardMargin = msg.frontGuardMargin,
                GravityX = msg.gravityX, GravityY = msg.gravityY,
                WindFreq = msg.windFreq, WindAmp = msg.windAmp, SpeedDisturb = msg.speedDisturb,
            };
            SkinSync.Settings.SetTailOverride(msg.skinID, ov);
        }

        // —— Skin pack sync (实验性) —— //

        private static readonly System.Collections.Generic.HashSet<string> _broadcastedPackIds = new System.Collections.Generic.HashSet<string>();

        internal static void OnServerSkinPackRequest(uint callerId, IInteropReader reader)
        {
            var msg = SkinMessageCodec.ReadSkinPackRequest(reader);
            if (SkinSync.Settings == null || !SkinSync.Settings.EnableSkinPackSync.Value) return;

            byte[] local = SkinPackCodec.PackLocalSkin(msg.skinID);
            if (local != null)
            {
                bool myAllow = SkinSync.Settings.AllowPeersPersistMyPack.Value;
                MultiplayerSender.ServerSendSkinPackDataToClient(callerId, local, myAllow);
                _broadcastedPackIds.Add(msg.skinID);
                SkinSyncMod.ModLog.Info($"服务端：已把皮肤包 {msg.skinID} 直接回给 client {callerId}（allowPersist={myAllow}）");
                return;
            }

            IInteropWriter writer = ChannelConsumer.CreateWriter(SkinNetworkIDs.SkinPackRequestMessageId);
            SkinMessageCodec.WriteSkinPackRequest(writer, msg);
            ChannelConsumer.ForwardExcept((ushort)callerId, writer, SkinNetworkIDs.SkinPackRequestMessageId);
            SkinSyncMod.ModLog.Info($"服务端:转发皮肤包请求 {msg.skinID} 给其他 client");
        }

        internal static void OnClientSkinPackRequest(uint callerId, IInteropReader reader)
        {
            var msg = SkinMessageCodec.ReadSkinPackRequest(reader);
            if (SkinSync.Settings == null || !SkinSync.Settings.EnableSkinPackSync.Value) return;

            byte[] local = SkinPackCodec.PackLocalSkin(msg.skinID);
            if (local == null) return;
            bool myAllow = SkinSync.Settings.AllowPeersPersistMyPack.Value;
            MultiplayerSender.SendSkinPackData(local, myAllow);
            SkinSyncMod.ModLog.Info($"客户端：响应皮肤包请求 {msg.skinID}（{local.Length} bytes, allowPersist={myAllow}）");
        }

        internal static void OnServerSkinPackData(uint callerId, IInteropReader reader)
        {
            var msg = SkinMessageCodec.ReadSkinPackData(reader);
            if (SkinSync.Settings == null || !SkinSync.Settings.EnableSkinPackSync.Value) return;
            if (msg.payload == null || msg.payload.Length == 0) return;

            bool persist = SkinSync.Settings.SkinPackPersistOnDisk.Value && msg.allowPersist;
            if (!SkinPackCodec.UnpackAndCache(msg.payload, persist, out string skinID)) return;
            SkinSyncMod.ModLog.Info($"服务端：收到 client {callerId} 的皮肤包 {skinID}（allowPersist={msg.allowPersist}），本机 persist={persist}");
            if (_broadcastedPackIds.Contains(skinID)) return;
            _broadcastedPackIds.Add(skinID);

            IInteropWriter writer = ChannelConsumer.CreateWriter(SkinNetworkIDs.SkinPackDataMessageId);
            SkinMessageCodec.WriteSkinPackData(writer, msg);
            ChannelConsumer.ForwardExcept((ushort)callerId, writer, SkinNetworkIDs.SkinPackDataMessageId);

            SkinApplier.ReapplyForSkin(skinID);
        }

        internal static void OnClientSkinPackData(uint callerId, IInteropReader reader)
        {
            var msg = SkinMessageCodec.ReadSkinPackData(reader);
            if (SkinSync.Settings == null || !SkinSync.Settings.EnableSkinPackSync.Value) return;
            if (msg.payload == null || msg.payload.Length == 0) return;

            bool persist = SkinSync.Settings.SkinPackPersistOnDisk.Value && msg.allowPersist;
            if (!SkinPackCodec.UnpackAndCache(msg.payload, persist, out string skinID)) return;
            SkinSyncMod.ModLog.Info($"客户端：收到皮肤包 {skinID}（{msg.payload.Length} bytes, allowPersist={msg.allowPersist}），本机 persist={persist}");

            SkinApplier.ReapplyForSkin(skinID);
        }
    }
}
