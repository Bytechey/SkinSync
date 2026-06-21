using LiteNetLib;
using LiteNetLib.Utils;

namespace SkinSyncMod.Network
{
    /// <summary>把 LiteNetLib 类型的接触面收拢到一处；调用方仅传基础类型，单机不触发 LiteNetLib 类型解析。</summary>
    /// <remarks>所有发送一律以 IsNetworkRunning() 为前提：无活动会话时贸然发送会在底层网络层
    /// 触发空引用崩溃。IsNetworkRunning() 已隐含 IsAvailable。</remarks>
    internal static class MultiplayerSender
    {
        public static void SendSkinChange(uint netId, string skinID)
        {
            if (!KrokoshaBridge.IsNetworkRunning()) return;
            var msg = new SkinChangeMessage { skinID = skinID, netId = netId };
            var writer = new NetDataWriter();
            msg.WriteTo(writer);
            KrokoshaBridge.ClientSend(DeliveryMethod.ReliableOrdered, writer);
        }

        public static void SendAccessory(uint netId, string skinID, string accId,
            bool enabled, int offX, int offY, float rotation, int zOrder)
        {
            if (!KrokoshaBridge.IsNetworkRunning()) return;
            var msg = new AccessorySyncMessage
            {
                netId = netId,
                skinID = skinID,
                accId = accId,
                enabled = enabled,
                offX = offX,
                offY = offY,
                rotation = rotation,
                zOrder = zOrder,
            };
            var writer = new NetDataWriter();
            msg.WriteTo(writer);
            KrokoshaBridge.ClientSend(DeliveryMethod.ReliableOrdered, writer);
        }

        public static void SendTailFromConfig(uint netId, string skinID)
        {
            if (!KrokoshaBridge.IsNetworkRunning()) return;
            var msg = new TailSyncMessage
            {
                netId = netId,
                skinID = skinID,
                enabled = TailDeformConfig.Enabled,
                frontGuard = TailDeformConfig.FrontGuard,
                segments = TailDeformConfig.Segments,
                constraintIters = TailDeformConfig.ConstraintIters,
                damping = TailDeformConfig.Damping,
                speedDamping = TailDeformConfig.SpeedDamping,
                stiffness = TailDeformConfig.Stiffness,
                maxBendDeg = TailDeformConfig.MaxBendDeg,
                anchorFollow = TailDeformConfig.AnchorFollow,
                smoothness = TailDeformConfig.Smoothness,
                maxStep = TailDeformConfig.MaxStep,
                maxFixedDt = TailDeformConfig.MaxFixedDt,
                frontGuardMargin = TailDeformConfig.FrontGuardMargin,
                gravityX = TailDeformConfig.GravityX,
                gravityY = TailDeformConfig.GravityY,
                windFreq = TailDeformConfig.WindFreq,
                windAmp = TailDeformConfig.WindAmp,
                speedDisturb = TailDeformConfig.SpeedDisturb,
            };
            var writer = new NetDataWriter();
            msg.WriteTo(writer);
            KrokoshaBridge.ClientSend(DeliveryMethod.ReliableOrdered, writer);
        }

        public static void ServerBroadcastSkinChange(uint netId, string skinID)
        {
            if (!KrokoshaBridge.IsNetworkRunning()) return;
            var msg = new SkinChangeMessage { skinID = skinID, netId = netId };
            var writer = new NetDataWriter();
            msg.WriteTo(writer);
            KrokoshaBridge.ServerBroadcast(DeliveryMethod.ReliableOrdered, writer);
        }

        public static void ServerSendSkinChangeToClient(uint targetClientId, uint netId, string skinID)
        {
            if (!KrokoshaBridge.IsNetworkRunning()) return;
            var msg = new SkinChangeMessage { skinID = skinID, netId = netId };
            var writer = new NetDataWriter();
            msg.WriteTo(writer);
            KrokoshaBridge.ServerSendToClient(targetClientId, DeliveryMethod.ReliableOrdered, writer);
        }

        public static void SendSkinPackRequest(string skinID)
        {
            if (!KrokoshaBridge.IsNetworkRunning()) return;
            var msg = new SkinPackRequestMessage { skinID = skinID };
            var writer = new NetDataWriter();
            msg.WriteTo(writer);
            KrokoshaBridge.ClientSend(DeliveryMethod.ReliableOrdered, writer);
        }

        public static void ServerSendSkinPackRequestToClient(uint targetClientId, string skinID)
        {
            if (!KrokoshaBridge.IsNetworkRunning()) return;
            var msg = new SkinPackRequestMessage { skinID = skinID };
            var writer = new NetDataWriter();
            msg.WriteTo(writer);
            KrokoshaBridge.ServerSendToClient(targetClientId, DeliveryMethod.ReliableOrdered, writer);
        }

        public static void SendSkinPackData(byte[] payload, bool allowPersist)
        {
            if (!KrokoshaBridge.IsNetworkRunning() || payload == null || payload.Length == 0) return;
            var msg = new SkinPackDataMessage { payload = payload, allowPersist = allowPersist };
            var writer = new NetDataWriter();
            msg.WriteTo(writer);
            KrokoshaBridge.ClientSend(DeliveryMethod.ReliableOrdered, writer);
        }

        public static void ServerSendSkinPackDataToClient(uint targetClientId, byte[] payload, bool allowPersist)
        {
            if (!KrokoshaBridge.IsNetworkRunning() || payload == null || payload.Length == 0) return;
            var msg = new SkinPackDataMessage { payload = payload, allowPersist = allowPersist };
            var writer = new NetDataWriter();
            msg.WriteTo(writer);
            KrokoshaBridge.ServerSendToClient(targetClientId, DeliveryMethod.ReliableOrdered, writer);
        }
    }
}
