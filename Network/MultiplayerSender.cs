using SkinSyncMod.Interop;
using SkinSyncMod.Interop.Consumers;
using UnityEngine;

namespace SkinSyncMod.Network
{
    /// <summary>向服务端或指定客户端发送各类皮肤同步数据包。</summary>
    internal static class MultiplayerSender
    {
        /// <summary>向服务端上报本机当前皮肤与角色类型。</summary>
        public static void SendSkinChange(uint netId, string skinID)
        {
            if (!SessionConsumer.IsRunning)
            {
                CommsTrace.SendSkipped(skinID, netId, "无活动会话（Together 未就绪或已断开）");
                return;
            }

            if (!IsNetIdReady(netId, skinID, "皮肤切换"))
            {
                return;
            }

            int charType = CharTypeOf(netId);
            IInteropWriter writer = ChannelConsumer.CreateWriter(SkinNetworkIDs.SkinChangeMessageId);
            SkinMessageCodec.WriteSkinChange(writer, new SkinChangeMessage
            {
                skinID = skinID,
                netId = netId,
                charType = charType,
            });
            CommsTrace.ClientSend(skinID, netId, charType);
            DebugMessageTrace.Sent(writer, SkinNetworkIDs.SkinChangeMessageId, "服务端");
            ChannelConsumer.SendToServer(writer);
        }

        private static bool IsNetIdReady(uint netId, string skinID, string what)
        {
            if (netId != 0)
            {
                return true;
            }

            CommsTrace.SendSkipped(skinID, netId, what + "：本地 netId 尚未分配（0），本次不上报");
            return false;
        }

        private static int CharTypeOf(uint netId)
        {
            if (!RosterConsumer.TryGetNetBodyFromId(netId, out _, out GameObject chara, out _) || chara == null) return 0;
            var body = chara.GetComponent<Body>();
            return body != null ? body.charType : 0;
        }

        /// <summary>向服务端上报单个配件的显隐与变换覆盖。</summary>
        public static void SendAccessory(uint netId, string skinID, string accId,
            bool enabled, int offX, int offY, float rotation, int zOrder)
        {
            if (!SessionConsumer.IsRunning) return;
            if (!IsNetIdReady(netId, skinID, "配件同步")) return;
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

        /// <summary>按当前本地配置生成指定实体与皮肤的尾巴参数同步消息体。</summary>
        internal static TailSyncMessage BuildLocalTailMessage(uint netId, string skinID)
        {
            return new TailSyncMessage
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
        }

        /// <summary>从当前 TailDeformConfig 读取快照并上报给服务端。</summary>
        public static void SendTailFromConfig(uint netId, string skinID)
        {
            if (!SessionConsumer.IsRunning) return;
            if (!IsNetIdReady(netId, skinID, "尾巴同步")) return;
            IInteropWriter writer = ChannelConsumer.CreateWriter(SkinNetworkIDs.TailSyncMessageId);
            SkinMessageCodec.WriteTail(writer, BuildLocalTailMessage(netId, skinID));
            DebugMessageTrace.Sent(writer, SkinNetworkIDs.TailSyncMessageId, "服务端");
            ChannelConsumer.SendToServer(writer);
        }

        public static void ServerBroadcastSkinChange(uint netId, string skinID)
        {
            if (!SessionConsumer.IsRunning)
            {
                CommsTrace.SendSkipped(skinID, netId, "无活动会话（主机广播自身切换）");
                return;
            }

            int charType = CharTypeOf(netId);
            IInteropWriter writer = ChannelConsumer.CreateWriter(SkinNetworkIDs.SkinChangeMessageId);
            SkinMessageCodec.WriteSkinChange(writer, new SkinChangeMessage
            {
                skinID = skinID,
                netId = netId,
                charType = charType,
            });
            CommsTrace.HostBroadcast(skinID, netId, -1, 0);
            DebugMessageTrace.Sent(writer, SkinNetworkIDs.SkinChangeMessageId, "全部客户端");
            ChannelConsumer.Broadcast(writer);
        }

        /// <summary>服务端向单个客户端单播指定实体的皮肤状态。</summary>
        public static void ServerSendSkinChangeToClient(uint targetClientId, uint netId, string skinID)
        {
            if (!SessionConsumer.IsRunning) return;
            int charType = CharTypeOf(netId);
            IInteropWriter writer = ChannelConsumer.CreateWriter(SkinNetworkIDs.SkinChangeMessageId);
            SkinMessageCodec.WriteSkinChange(writer, new SkinChangeMessage
            {
                skinID = skinID,
                netId = netId,
                charType = charType,
            });
            CommsTrace.HostUnicast(targetClientId, skinID, netId, charType);
            DebugMessageTrace.Sent(writer, SkinNetworkIDs.SkinChangeMessageId, "客户端 " + targetClientId);
            ChannelConsumer.SendToClient((ushort)targetClientId, writer);
        }

        /// <summary>服务端向单个客户端单播指定实体的配件覆盖状态。</summary>
        public static void ServerSendAccessoryToClient(uint targetClientId, AccessorySyncMessage msg)
        {
            if (!SessionConsumer.IsRunning) return;
            IInteropWriter writer = ChannelConsumer.CreateWriter(SkinNetworkIDs.AccessorySyncMessageId);
            SkinMessageCodec.WriteAccessory(writer, msg);
            DebugMessageTrace.Sent(writer, SkinNetworkIDs.AccessorySyncMessageId, "客户端 " + targetClientId);
            ChannelConsumer.SendToClient((ushort)targetClientId, writer);
        }

        /// <summary>服务端向单个客户端单播指定实体的尾巴物理参数。</summary>
        public static void ServerSendTailToClient(uint targetClientId, TailSyncMessage msg)
        {
            if (!SessionConsumer.IsRunning) return;
            IInteropWriter writer = ChannelConsumer.CreateWriter(SkinNetworkIDs.TailSyncMessageId);
            SkinMessageCodec.WriteTail(writer, msg);
            DebugMessageTrace.Sent(writer, SkinNetworkIDs.TailSyncMessageId, "客户端 " + targetClientId);
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

        private static uint _nextTransferId = 1;

        /// <summary>客户端向服务端上传皮肤包字节流，超过单包上限时自动分片发送。</summary>
        public static bool SendSkinPackData(byte[] payload, bool allowPersist)
        {
            if (!SessionConsumer.IsRunning || payload == null || payload.Length == 0) return false;
            if (!IsPayloadWithinLimit(payload)) return false;

            if (payload.Length <= SkinPackCodec.SINGLE_PACKET_LIMIT)
            {
                IInteropWriter writer = ChannelConsumer.CreateWriter(SkinNetworkIDs.SkinPackDataMessageId);
                SkinMessageCodec.WriteSkinPackData(writer, new SkinPackDataMessage { payload = payload, allowPersist = allowPersist });
                DebugMessageTrace.Sent(writer, SkinNetworkIDs.SkinPackDataMessageId, "服务端");
                ChannelConsumer.SendToServer(writer);
                return true;
            }

            SendChunked(payload, allowPersist, "服务端", writer => ChannelConsumer.SendToServer(writer));
            return true;
        }

        /// <summary>服务端向指定客户端单播皮肤包字节流，超过单包上限时自动分片发送。</summary>
        public static bool ServerSendSkinPackDataToClient(uint targetClientId, byte[] payload, bool allowPersist)
        {
            if (!SessionConsumer.IsRunning || payload == null || payload.Length == 0) return false;
            if (!IsPayloadWithinLimit(payload)) return false;

            string targetLabel = "客户端 " + targetClientId;
            if (payload.Length <= SkinPackCodec.SINGLE_PACKET_LIMIT)
            {
                IInteropWriter writer = ChannelConsumer.CreateWriter(SkinNetworkIDs.SkinPackDataMessageId);
                SkinMessageCodec.WriteSkinPackData(writer, new SkinPackDataMessage { payload = payload, allowPersist = allowPersist });
                DebugMessageTrace.Sent(writer, SkinNetworkIDs.SkinPackDataMessageId, targetLabel);
                ChannelConsumer.SendToClient((ushort)targetClientId, writer);
                return true;
            }

            SendChunked(payload, allowPersist, targetLabel, writer => ChannelConsumer.SendToClient((ushort)targetClientId, writer));
            return true;
        }

        /// <summary>服务端将已校验的皮肤包转发给除来源客户端外的其他远端客户端。</summary>
        public static void ServerForwardSkinPackExcept(uint excludeClientId, byte[] payload, bool allowPersist)
        {
            if (!SessionConsumer.IsRunning || payload == null || payload.Length == 0) return;
            if (!IsPayloadWithinLimit(payload)) return;

            if (payload.Length <= SkinPackCodec.SINGLE_PACKET_LIMIT)
            {
                IInteropWriter writer = ChannelConsumer.CreateWriter(SkinNetworkIDs.SkinPackDataMessageId);
                SkinMessageCodec.WriteSkinPackData(writer, new SkinPackDataMessage { payload = payload, allowPersist = allowPersist });
                ChannelConsumer.ForwardExcept((ushort)excludeClientId, writer, SkinNetworkIDs.SkinPackDataMessageId);
                return;
            }

            SendChunked(payload, allowPersist, "其他客户端", writer =>
                ChannelConsumer.ForwardExcept((ushort)excludeClientId, writer, SkinNetworkIDs.SkinPackChunkMessageId));
        }

        private static void SendChunked(byte[] payload, bool allowPersist, string targetLabel, System.Action<IInteropWriter> send)
        {
            uint transferId = _nextTransferId++;
            if (_nextTransferId == 0) _nextTransferId = 1;

            int totalBytes = payload.Length;
            int chunkSize = SkinPackCodec.CHUNK_PAYLOAD_SIZE;
            ushort totalChunks = (ushort)((totalBytes + chunkSize - 1) / chunkSize);
            uint crc = SkinPackChunkAssembler.ComputeCrc32(payload);

            for (ushort idx = 0; idx < totalChunks; idx++)
            {
                int offset = idx * chunkSize;
                int len = System.Math.Min(chunkSize, totalBytes - offset);
                var slice = new byte[len];
                System.Buffer.BlockCopy(payload, offset, slice, 0, len);

                IInteropWriter writer = ChannelConsumer.CreateWriter(SkinNetworkIDs.SkinPackChunkMessageId);
                SkinMessageCodec.WriteSkinPackChunk(writer, new SkinPackChunkMessage
                {
                    transferId = transferId,
                    chunkIndex = idx,
                    totalChunks = totalChunks,
                    totalBytes = totalBytes,
                    crc32 = crc,
                    allowPersist = allowPersist,
                    data = slice,
                });
                DebugMessageTrace.Sent(writer, SkinNetworkIDs.SkinPackChunkMessageId, targetLabel);
                send(writer);
            }
        }

        private static bool IsPayloadWithinLimit(byte[] payload)
        {
            if (payload.Length <= SkinPackCodec.MAX_PACK_SIZE) return true;
            SkinSyncMod.ModLog.Warning($"皮肤包 {payload.Length} bytes 超过总上限 {SkinPackCodec.MAX_PACK_SIZE}，已跳过发送。");
            return false;
        }
    }
}
