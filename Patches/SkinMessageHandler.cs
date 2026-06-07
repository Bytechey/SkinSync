using LiteNetLib;
using LiteNetLib.Utils;
using SkinSyncMod.Network;
using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>
    /// 处理皮肤切换网络消息：客户端收到广播后应用、服务器收到请求后校验并广播。
    /// </summary>
    public static class SkinMessageHandlers
    {
        public static void OnClientSkinMessage(uint callerId, ref NetDataReader reader)
        {
            SkinChangeMessage msg = new SkinChangeMessage();
            msg.Deserialize(reader);

            bool hasLocal = KrokoshaBridge.TryGetLocalNetBody(out _, out uint localNetId, out _);
            bool hasCid = KrokoshaBridge.TryGetLocalClientId(out uint localCid);
            bool isLocalEcho = (hasLocal && localNetId == msg.netId) || (hasCid && localCid == msg.netId);
            SkinSyncMod.ModLog.Info($"客户端收到皮肤切换：{msg.skinID} (msg.netId {msg.netId}, local netId {(hasLocal ? localNetId.ToString() : "n/a")}, local clientId {(hasCid ? localCid.ToString() : "n/a")}, echo={isLocalEcho})");

            if (!isLocalEcho)
            {
                ApplySkinToPlayer(msg.netId, msg.skinID);
            }

            // 收到的是本地玩家自己 → 写回 Settings.CurrentSkin，让主菜单 / 单机也用同一个值。
            if (KrokoshaBridge.TryGetNetBodyFromId(msg.netId, out _, out _, out bool isLocal) && isLocal)
            {
                if (SkinSync.Settings != null) SkinSync.Settings.CurrentSkin.Value = msg.skinID;
            }
        }

        public static void OnServerSkinMessage(uint callerId, ref NetDataReader reader)
        {
            SkinChangeMessage msg = new SkinChangeMessage();
            msg.Deserialize(reader);
            SkinSyncMod.ModLog.Info($"服务端收到客户端 {callerId} 的皮肤切换请求：{msg.skinID} (netId {msg.netId})");

            if (callerId != msg.netId)
            {
                SkinSyncMod.ModLog.Warning($"Cheat attempt: client {callerId} tried to change skin for {msg.netId}");
                return;
            }

            ApplySkinToPlayer(msg.netId, msg.skinID);

            NetDataWriter writer = new NetDataWriter();
            writer.Put(SkinNetworkIDs.SkinChangeMessageId);
            msg.Serialize(writer);
            KrokoshaBridge.ServerBroadcastExcept(callerId, DeliveryMethod.ReliableOrdered, writer);

            Patches.SkinCacheBroadcaster.RecordSkin(msg.netId, msg.skinID);

            if (KrokoshaBridge.TryGetPlayerSteamId(callerId, out ulong steamId) && steamId != 0UL)
                SkinCacheStore.Set(steamId, msg.skinID);
        }

        private static void ApplySkinToPlayer(uint netId, string characterName)
        {
            if (!KrokoshaBridge.TryGetNetBodyFromId(netId, out _, out GameObject playerObj, out _))
            {
                Patches.PendingSkinApplier.Enqueue(netId, characterName);
                SkinSyncMod.ModLog.Info($"NetBody netId {netId} 暂未注册，已加入待应用队列：{characterName}");
                return;
            }
            if (playerObj == null)
            {
                Patches.PendingSkinApplier.Enqueue(netId, characterName);
                return;
            }
            SkinApplier.ApplySkinToPlayer(playerObj, characterName);
        }

        // —— Accessory sync —— //

        public static void OnClientAccessorySync(uint callerId, ref NetDataReader reader)
        {
            var msg = new AccessorySyncMessage();
            msg.Deserialize(reader);
            if (SkinSync.Settings != null && SkinSync.Settings.SyncMode.Value == "Passive")
            {
                return;
            }
            ApplyAccessoryOverride(msg);
        }

        public static void OnServerAccessorySync(uint callerId, ref NetDataReader reader)
        {
            var msg = new AccessorySyncMessage();
            msg.Deserialize(reader);
            if (callerId != msg.netId)
            {
                SkinSyncMod.ModLog.Warning($"Cheat attempt: client {callerId} tried accessory sync for {msg.netId}");
                return;
            }
            ApplyAccessoryOverride(msg);

            var writer = new NetDataWriter();
            writer.Put(SkinNetworkIDs.AccessorySyncMessageId);
            msg.Serialize(writer);
            KrokoshaBridge.ServerBroadcastExcept(callerId, DeliveryMethod.ReliableOrdered, writer);
        }

        private static void ApplyAccessoryOverride(AccessorySyncMessage msg)
        {
            if (SkinSync.Settings == null) return;
            // 写覆盖：5 字段都写。enabled 单独 + 4 个 transform 一并写。
            SkinSync.Settings.SetAccessoryEnabled(msg.skinID, msg.accId, msg.enabled);
            SkinSync.Settings.SetAccessoryTransform(msg.skinID, msg.accId, msg.offX, msg.offY, msg.rotation, msg.zOrder);
            // 把目标玩家的 chara 重新装配一遍——AccessoryAttacher.Apply 内部会读最新 settings 覆盖。
            if (KrokoshaBridge.TryGetNetBodyFromId(msg.netId, out _, out GameObject chara, out _) && chara != null)
            {
                SkinApplier.ApplySkinToPlayer(chara, msg.skinID);
            }
        }

        // —— Tail sync —— //

        public static void OnClientTailSync(uint callerId, ref NetDataReader reader)
        {
            var msg = new TailSyncMessage();
            msg.Deserialize(reader);
            if (SkinSync.Settings != null && SkinSync.Settings.SyncMode.Value == "Passive")
            {
                return;
            }
            ApplyTailOverride(msg);
        }

        public static void OnServerTailSync(uint callerId, ref NetDataReader reader)
        {
            var msg = new TailSyncMessage();
            msg.Deserialize(reader);
            if (callerId != msg.netId)
            {
                SkinSyncMod.ModLog.Warning($"Cheat attempt: client {callerId} tried tail sync for {msg.netId}");
                return;
            }
            ApplyTailOverride(msg);

            var writer = new NetDataWriter();
            writer.Put(SkinNetworkIDs.TailSyncMessageId);
            msg.Serialize(writer);
            KrokoshaBridge.ServerBroadcastExcept(callerId, DeliveryMethod.ReliableOrdered, writer);
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

        public static void OnServerSkinPackRequest(uint callerId, ref NetDataReader reader)
        {
            var msg = new SkinPackRequestMessage();
            msg.Deserialize(reader);
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

            var writer = new NetDataWriter();
            writer.Put(SkinNetworkIDs.SkinPackRequestMessageId);
            msg.Serialize(writer);
            KrokoshaBridge.ServerBroadcastExcept(callerId, DeliveryMethod.ReliableOrdered, writer);
            SkinSyncMod.ModLog.Info($"服务端:转发皮肤包请求 {msg.skinID} 给其他 client");
        }

        public static void OnClientSkinPackRequest(uint callerId, ref NetDataReader reader)
        {
            var msg = new SkinPackRequestMessage();
            msg.Deserialize(reader);
            if (SkinSync.Settings == null || !SkinSync.Settings.EnableSkinPackSync.Value) return;

            byte[] local = SkinPackCodec.PackLocalSkin(msg.skinID);
            if (local == null) return;
            bool myAllow = SkinSync.Settings.AllowPeersPersistMyPack.Value;
            MultiplayerSender.SendSkinPackData(local, myAllow);
            SkinSyncMod.ModLog.Info($"客户端：响应皮肤包请求 {msg.skinID}（{local.Length} bytes, allowPersist={myAllow}）");
        }

        public static void OnServerSkinPackData(uint callerId, ref NetDataReader reader)
        {
            var msg = new SkinPackDataMessage();
            msg.Deserialize(reader);
            if (SkinSync.Settings == null || !SkinSync.Settings.EnableSkinPackSync.Value) return;
            if (msg.payload == null || msg.payload.Length == 0) return;

            bool persist = SkinSync.Settings.SkinPackPersistOnDisk.Value && msg.allowPersist;
            if (!SkinPackCodec.UnpackAndCache(msg.payload, persist, out string skinID)) return;
            SkinSyncMod.ModLog.Info($"服务端：收到 client {callerId} 的皮肤包 {skinID}（allowPersist={msg.allowPersist}），本机 persist={persist}");
            if (_broadcastedPackIds.Contains(skinID)) return;
            _broadcastedPackIds.Add(skinID);

            var writer = new NetDataWriter();
            writer.Put(SkinNetworkIDs.SkinPackDataMessageId);
            msg.Serialize(writer);
            KrokoshaBridge.ServerBroadcastExcept(callerId, DeliveryMethod.ReliableOrdered, writer);

            SkinApplier.ReapplyForSkin(skinID);
        }

        public static void OnClientSkinPackData(uint callerId, ref NetDataReader reader)
        {
            var msg = new SkinPackDataMessage();
            msg.Deserialize(reader);
            if (SkinSync.Settings == null || !SkinSync.Settings.EnableSkinPackSync.Value) return;
            if (msg.payload == null || msg.payload.Length == 0) return;

            bool persist = SkinSync.Settings.SkinPackPersistOnDisk.Value && msg.allowPersist;
            if (!SkinPackCodec.UnpackAndCache(msg.payload, persist, out string skinID)) return;
            SkinSyncMod.ModLog.Info($"客户端：收到皮肤包 {skinID}（{msg.payload.Length} bytes, allowPersist={msg.allowPersist}），本机 persist={persist}");

            SkinApplier.ReapplyForSkin(skinID);
        }
    }
}
