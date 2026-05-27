using KrokoshaCasualtiesMP;
using LiteNetLib;
using LiteNetLib.Utils;
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
            Debug.Log($"[SkinSync] Client received skin: {msg.skinID} for netId {msg.netId}");
            ApplySkinToPlayer(msg.netId, msg.skinID);

            // 收到的是本地玩家自己 → 写回 Settings.CurrentSkin，让主菜单 / 单机也用同一个值。
            if (NetBody.TryGetNetBodyFromId(msg.netId, out NetBody nb) && nb != null && nb.is_local)
            {
                if (SkinSync.Settings != null) SkinSync.Settings.CurrentSkin.Value = msg.skinID;
            }
        }

        public static void OnServerSkinMessage(uint callerId, ref NetDataReader reader)
        {
            SkinChangeMessage msg = new SkinChangeMessage();
            msg.Deserialize(reader);
            Debug.Log($"[SkinSync] Server received skin change from client {callerId}: {msg.skinID} for netId {msg.netId}");

            if (callerId != msg.netId)
            {
                Debug.LogWarning($"[SkinSync] Cheat attempt: client {callerId} tried to change skin for {msg.netId}");
                return;
            }

            ApplySkinToPlayer(msg.netId, msg.skinID);

            NetDataWriter writer = new NetDataWriter();
            writer.Put(SkinNetworkIDs.SkinChangeMessageId);
            msg.Serialize(writer);
            var allClients = ServerMain.AllClientIds;
            Net.Server_SendToClients(DeliveryMethod.ReliableOrdered, writer, allClients);

            if (NetPlayer.TryGetPlayerFromClientId(callerId, out NetPlayer plr) && plr.steam_id != 0UL)
                SkinCacheStore.Set(plr.steam_id, msg.skinID);
        }

        private static void ApplySkinToPlayer(uint netId, string characterName)
        {
            if (!NetBody.TryGetNetBodyFromId(netId, out NetBody nb))
            {
                Debug.LogWarning($"[SkinSync] NetBody with netId {netId} not found");
                return;
            }
            GameObject playerObj = nb.chara;
            if (playerObj == null)
            {
                Debug.LogWarning($"[SkinSync] Player GameObject for netId {netId} is null");
                return;
            }
            SkinApplier.ApplySkinToPlayer(playerObj, characterName);
        }

        // —— Accessory sync —— //

        public static void OnClientAccessorySync(uint callerId, ref NetDataReader reader)
        {
            var msg = new AccessorySyncMessage();
            msg.Deserialize(reader);
            ApplyAccessoryOverride(msg);
        }

        public static void OnServerAccessorySync(uint callerId, ref NetDataReader reader)
        {
            var msg = new AccessorySyncMessage();
            msg.Deserialize(reader);
            if (callerId != msg.netId)
            {
                Debug.LogWarning($"[SkinSync] Cheat attempt: client {callerId} tried accessory sync for {msg.netId}");
                return;
            }
            ApplyAccessoryOverride(msg);

            var writer = new NetDataWriter();
            writer.Put(SkinNetworkIDs.AccessorySyncMessageId);
            msg.Serialize(writer);
            Net.Server_SendToClients(DeliveryMethod.ReliableOrdered, writer, ServerMain.AllClientIds);
        }

        private static void ApplyAccessoryOverride(AccessorySyncMessage msg)
        {
            if (SkinSync.Settings == null) return;
            // 写覆盖：5 字段都写。enabled 单独 + 4 个 transform 一并写。
            SkinSync.Settings.SetAccessoryEnabled(msg.skinID, msg.accId, msg.enabled);
            SkinSync.Settings.SetAccessoryTransform(msg.skinID, msg.accId, msg.offX, msg.offY, msg.rotation, msg.zOrder);
            // 把目标玩家的 chara 重新装配一遍——AccessoryAttacher.Apply 内部会读最新 settings 覆盖。
            if (NetBody.TryGetNetBodyFromId(msg.netId, out NetBody nb) && nb != null && nb.chara != null)
            {
                SkinApplier.ApplySkinToPlayer(nb.chara, msg.skinID);
            }
        }

        // —— Tail sync —— //

        public static void OnClientTailSync(uint callerId, ref NetDataReader reader)
        {
            var msg = new TailSyncMessage();
            msg.Deserialize(reader);
            ApplyTailOverride(msg);
        }

        public static void OnServerTailSync(uint callerId, ref NetDataReader reader)
        {
            var msg = new TailSyncMessage();
            msg.Deserialize(reader);
            if (callerId != msg.netId)
            {
                Debug.LogWarning($"[SkinSync] Cheat attempt: client {callerId} tried tail sync for {msg.netId}");
                return;
            }
            ApplyTailOverride(msg);

            var writer = new NetDataWriter();
            writer.Put(SkinNetworkIDs.TailSyncMessageId);
            msg.Serialize(writer);
            Net.Server_SendToClients(DeliveryMethod.ReliableOrdered, writer, ServerMain.AllClientIds);
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
                Enabled = msg.enabled, FrontGuard = msg.frontGuard,
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
    }
}
