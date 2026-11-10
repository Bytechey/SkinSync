using SkinSyncMod.Interop;
using SkinSyncMod.Interop.Consumers;

namespace SkinSyncMod.Network
{
    /// <summary>皮肤同步的发送入口：全部发送经 Interop 通道契约，无活动会话时静默返回。</summary>
    /// <remarks>
    /// 发送一律以 SessionConsumer.IsRunning 为前提：无活动会话时贸然发送会在目标插件的网络层触发空引用崩溃。
    /// 消息体由 SkinMessageCodec 按字段写入 IInteropWriter，包名与子 ID 由 ChannelConsumer 统一处理。
    /// 字节数组负载受契约长度前缀限制（单包上限 65535 字节），超长皮肤包直接跳过并记一条警告，避免发出解析错位的数据包。
    /// </remarks>
    internal static class MultiplayerSender
    {
        public static void SendSkinChange(uint netId, string skinID)
        {
            if (!SessionConsumer.IsRunning) return;
            IInteropWriter writer = ChannelConsumer.CreateWriter(SkinNetworkIDs.SkinChangeMessageId);
            SkinMessageCodec.WriteSkinChange(writer, new SkinChangeMessage { skinID = skinID, netId = netId });
            DebugMessageTrace.Sent(writer, SkinNetworkIDs.SkinChangeMessageId, "服务端");
            ChannelConsumer.SendToServer(writer);
        }

        public static void SendAccessory(uint netId, string skinID, string accId,
            bool enabled, int offX, int offY, float rotation, int zOrder)
        {
            if (!SessionConsumer.IsRunning) return;
            IInteropWriter writer = ChannelConsumer.CreateWriter(SkinNetworkIDs.AccessorySyncMessageId);
            SkinMessageCodec.WriteAccessory(writer, new AccessorySyncMessage
            {
                netId = netId,
                skinID = skinID,
                accId = accId,
                enabled = enabled,
                offX = offX,
                offY = offY,
                rotation = rotation,
                zOrder = zOrder,
            });
            DebugMessageTrace.Sent(writer, SkinNetworkIDs.AccessorySyncMessageId, "服务端");
            ChannelConsumer.SendToServer(writer);
        }

        public static void SendTailFromConfig(uint netId, string skinID)
        {
            if (!SessionConsumer.IsRunning) return;
            IInteropWriter writer = ChannelConsumer.CreateWriter(SkinNetworkIDs.TailSyncMessageId);
            SkinMessageCodec.WriteTail(writer, new TailSyncMessage
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
            });
            DebugMessageTrace.Sent(writer, SkinNetworkIDs.TailSyncMessageId, "服务端");
            ChannelConsumer.SendToServer(writer);
        }

        public static void ServerBroadcastSkinChange(uint netId, string skinID)
        {
            if (!SessionConsumer.IsRunning) return;
            IInteropWriter writer = ChannelConsumer.CreateWriter(SkinNetworkIDs.SkinChangeMessageId);
            SkinMessageCodec.WriteSkinChange(writer, new SkinChangeMessage { skinID = skinID, netId = netId });
            DebugMessageTrace.Sent(writer, SkinNetworkIDs.SkinChangeMessageId, "全部客户端");
            ChannelConsumer.Broadcast(writer);
        }

        public static void ServerSendSkinChangeToClient(uint targetClientId, uint netId, string skinID)
        {
            if (!SessionConsumer.IsRunning) return;
            IInteropWriter writer = ChannelConsumer.CreateWriter(SkinNetworkIDs.SkinChangeMessageId);
            SkinMessageCodec.WriteSkinChange(writer, new SkinChangeMessage { skinID = skinID, netId = netId });
            DebugMessageTrace.Sent(writer, SkinNetworkIDs.SkinChangeMessageId, "客户端 " + targetClientId);
            ChannelConsumer.SendToClient((ushort)targetClientId, writer);
        }

        public static void SendSkinPackRequest(string skinID)
        {
            if (!SessionConsumer.IsRunning) return;
            IInteropWriter writer = ChannelConsumer.CreateWriter(SkinNetworkIDs.SkinPackRequestMessageId);
            SkinMessageCodec.WriteSkinPackRequest(writer, new SkinPackRequestMessage { skinID = skinID });
            DebugMessageTrace.Sent(writer, SkinNetworkIDs.SkinPackRequestMessageId, "服务端");
            ChannelConsumer.SendToServer(writer);
        }

        public static void ServerSendSkinPackRequestToClient(uint targetClientId, string skinID)
        {
            if (!SessionConsumer.IsRunning) return;
            IInteropWriter writer = ChannelConsumer.CreateWriter(SkinNetworkIDs.SkinPackRequestMessageId);
            SkinMessageCodec.WriteSkinPackRequest(writer, new SkinPackRequestMessage { skinID = skinID });
            DebugMessageTrace.Sent(writer, SkinNetworkIDs.SkinPackRequestMessageId, "客户端 " + targetClientId);
            ChannelConsumer.SendToClient((ushort)targetClientId, writer);
        }

        public static void SendSkinPackData(byte[] payload, bool allowPersist)
        {
            if (!SessionConsumer.IsRunning || payload == null || payload.Length == 0) return;
            if (!IsPayloadWithinLimit(payload)) return;
            IInteropWriter writer = ChannelConsumer.CreateWriter(SkinNetworkIDs.SkinPackDataMessageId);
            SkinMessageCodec.WriteSkinPackData(writer, new SkinPackDataMessage { payload = payload, allowPersist = allowPersist });
            DebugMessageTrace.Sent(writer, SkinNetworkIDs.SkinPackDataMessageId, "服务端");
            ChannelConsumer.SendToServer(writer);
        }

        public static void ServerSendSkinPackDataToClient(uint targetClientId, byte[] payload, bool allowPersist)
        {
            if (!SessionConsumer.IsRunning || payload == null || payload.Length == 0) return;
            if (!IsPayloadWithinLimit(payload)) return;
            IInteropWriter writer = ChannelConsumer.CreateWriter(SkinNetworkIDs.SkinPackDataMessageId);
            SkinMessageCodec.WriteSkinPackData(writer, new SkinPackDataMessage { payload = payload, allowPersist = allowPersist });
            DebugMessageTrace.Sent(writer, SkinNetworkIDs.SkinPackDataMessageId, "客户端 " + targetClientId);
            ChannelConsumer.SendToClient((ushort)targetClientId, writer);
        }

        /// <summary>检查皮肤包长度；超过契约写入器的 2 字节长度前缀上限（65535）时跳过发送。</summary>
        private static bool IsPayloadWithinLimit(byte[] payload)
        {
            if (payload.Length <= ushort.MaxValue) return true;
            SkinSyncMod.ModLog.Warning($"皮肤包 {payload.Length} bytes 超过单包上限 {ushort.MaxValue}，已跳过发送。");
            return false;
        }
    }
}
