using LiteNetLib;
using LiteNetLib.Utils;

namespace SkinSyncMod.Network
{
    /// <summary>把 LiteNetLib 类型的接触面收拢到一处；调用方仅传基础类型，单机不触发 LiteNetLib 类型解析。</summary>
    internal static class MultiplayerSender
    {
        public static void SendSkinChange(uint netId, string skinID)
        {
            if (!KrokoshaBridge.IsAvailable) return;
            var msg = new SkinChangeMessage { skinID = skinID, netId = netId };
            var writer = new NetDataWriter();
            msg.WriteTo(writer);
            KrokoshaBridge.ClientSend(DeliveryMethod.ReliableOrdered, writer);
        }

        public static void SendAccessory(uint netId, string skinID, string accId,
            bool enabled, int offX, int offY, float rotation, int zOrder)
        {
            if (!KrokoshaBridge.IsAvailable) return;
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
            if (!KrokoshaBridge.IsAvailable) return;
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
            if (!KrokoshaBridge.IsAvailable) return;
            var msg = new SkinChangeMessage { skinID = skinID, netId = netId };
            var writer = new NetDataWriter();
            msg.WriteTo(writer);
            KrokoshaBridge.ServerBroadcast(DeliveryMethod.ReliableOrdered, writer);
        }
    }
}
