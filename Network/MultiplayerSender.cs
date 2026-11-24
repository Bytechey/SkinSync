using System.Collections.Generic;
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
                // 内容哈希让接收端区分"同名但内容不同"；取不到皮肤目录时留空，退回按名字。
                contentHash = SkinPackCodec.ComputeContentHash(skinID),
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
                // 内容哈希让接收端区分"同名但内容不同"；取不到皮肤目录时留空，退回按名字。
                contentHash = SkinPackCodec.ComputeContentHash(skinID),
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

        /// <summary>客户端完成关卡场景加载后通知服务端单播补发当前局内皮肤状态表。</summary>
        public static void SendClientWorldReady()
        {
            if (!SessionConsumer.IsRunning || SessionConsumer.IsServer) return;
            var writer = ChannelConsumer.CreateWriter(SkinNetworkIDs.ClientWorldReadyMessageId);
            writer.PutUInt(1u);
            DebugMessageTrace.Sent(writer, SkinNetworkIDs.ClientWorldReadyMessageId, "服务端");
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

        private const float BytesPerSecondBudget = 512 * 1024f;
        private const float MaxBurstBytes = 128 * 1024f;
        private const ulong TargetKeyServer = 1UL << 32;

        private sealed class OutboundChunkJob
        {
            public ulong TargetKey;
            public uint TransferId;
            public ushort TotalChunks;
            public int TotalBytes;
            public uint Crc32;
            public bool AllowPersist;
            public byte[] Payload;
            /// <summary>整包内容哈希；建 job 时算一次，随每个分片带上，避免逐片重复解析整包。</summary>
            public string ContentHash;
            public ushort NextChunkIndex;
            public System.Func<IInteropWriter, int> SendAction;
        }

        private static readonly Queue<OutboundChunkJob> _outboundQueue = new Queue<OutboundChunkJob>();
        private static uint _nextTransferId = 1;
        private static float _availableBytes = MaxBurstBytes;
        private static int _lastBudgetFrame = -1;

        private static ulong TargetKeyClient(uint clientId) => (2UL << 32) | clientId;
        private static ulong TargetKeyForwardExcept(uint excludeClientId) => (3UL << 32) | excludeClientId;

        /// <summary>清空尚未发送完毕的大型皮肤包分片队列并恢复发送字节预算。</summary>
        internal static void ClearOutboundQueue()
        {
            _outboundQueue.Clear();
            _availableBytes = MaxBurstBytes;
            _lastBudgetFrame = -1;
        }

        /// <summary>按时间字节令牌桶与实际扇出人数排空待发送分片队列，与帧率解耦并保护 Steamworks 1 MB 发送缓冲区。</summary>
        internal static void TickOutboundChunks()
        {
            if (!SessionConsumer.IsRunning)
            {
                ClearOutboundQueue();
                return;
            }

            int frame = Time.frameCount;
            if (frame != _lastBudgetFrame)
            {
                _lastBudgetFrame = frame;
                float dt = Mathf.Clamp(Time.unscaledDeltaTime, 0f, 0.25f);
                _availableBytes = Mathf.Min(MaxBurstBytes, _availableBytes + dt * BytesPerSecondBudget);
            }

            if (_outboundQueue.Count == 0) return;

            int chunkSize = SkinPackCodec.CHUNK_PAYLOAD_SIZE;
            while (_availableBytes > 0f && _outboundQueue.Count > 0)
            {
                OutboundChunkJob job = _outboundQueue.Peek();
                ushort idx = job.NextChunkIndex++;
                int offset = idx * chunkSize;
                int len = System.Math.Min(chunkSize, job.TotalBytes - offset);
                var slice = new byte[len];
                System.Buffer.BlockCopy(job.Payload, offset, slice, 0, len);

                IInteropWriter writer = ChannelConsumer.CreateWriter(SkinNetworkIDs.SkinPackChunkMessageId);
                SkinMessageCodec.WriteSkinPackChunk(writer, new SkinPackChunkMessage
                {
                    transferId = job.TransferId,
                    chunkIndex = idx,
                    totalChunks = job.TotalChunks,
                    totalBytes = job.TotalBytes,
                    crc32 = job.Crc32,
                    allowPersist = job.AllowPersist,
                    data = slice,
                    contentHash = job.ContentHash,
                });
                int recipients = job.SendAction(writer);
                if (recipients <= 0)
                {
                    _outboundQueue.Dequeue();
                    continue;
                }

                _availableBytes -= (float)len * recipients;
                if (job.NextChunkIndex >= job.TotalChunks)
                {
                    _outboundQueue.Dequeue();
                }
            }
        }

        /// <summary>客户端向服务端上传皮肤包字节流，超过单包上限时自动分片发送。</summary>
        public static bool SendSkinPackData(byte[] payload, bool allowPersist)
        {
            return DispatchSkinPack(
                TargetKeyServer,
                payload,
                allowPersist,
                (writer, subId) =>
                {
                    DebugMessageTrace.Sent(writer, subId, "服务端");
                    ChannelConsumer.SendToServer(writer);
                    return 1;
                });
        }

        /// <summary>服务端向指定客户端单播皮肤包字节流，超过单包上限时自动分片发送。</summary>
        public static bool ServerSendSkinPackDataToClient(uint targetClientId, byte[] payload, bool allowPersist)
        {
            string targetLabel = "客户端 " + targetClientId;
            return DispatchSkinPack(
                TargetKeyClient(targetClientId),
                payload,
                allowPersist,
                (writer, subId) =>
                {
                    DebugMessageTrace.Sent(writer, subId, targetLabel);
                    ChannelConsumer.SendToClient((ushort)targetClientId, writer);
                    return 1;
                });
        }

        /// <summary>服务端将已校验的皮肤包转发给除来源客户端外的其他远端客户端。</summary>
        public static void ServerForwardSkinPackExcept(uint excludeClientId, byte[] payload, bool allowPersist)
        {
            DispatchSkinPack(
                TargetKeyForwardExcept(excludeClientId),
                payload,
                allowPersist,
                (writer, subId) => ChannelConsumer.ForwardExcept((ushort)excludeClientId, writer, subId));
        }

        private static bool DispatchSkinPack(
            ulong targetKey,
            byte[] payload,
            bool allowPersist,
            System.Func<IInteropWriter, ushort, int> sendAction)
        {
            if (!SessionConsumer.IsRunning || payload == null || payload.Length == 0) return false;
            if (!IsPayloadWithinLimit(payload)) return false;

            if (payload.Length <= SkinPackCodec.SINGLE_PACKET_LIMIT)
            {
                IInteropWriter writer = ChannelConsumer.CreateWriter(SkinNetworkIDs.SkinPackDataMessageId);
                SkinMessageCodec.WriteSkinPackData(writer, new SkinPackDataMessage { payload = payload, allowPersist = allowPersist });
                sendAction(writer, SkinNetworkIDs.SkinPackDataMessageId);
                return true;
            }

            SendChunked(targetKey, payload, allowPersist, writer => sendAction(writer, SkinNetworkIDs.SkinPackChunkMessageId));
            return true;
        }

        private static void SendChunked(
            ulong targetKey,
            byte[] payload,
            bool allowPersist,
            System.Func<IInteropWriter, int> send)
        {
            uint crc = SkinPackChunkAssembler.ComputeCrc32(payload);
            foreach (var pending in _outboundQueue)
            {
                if (pending.TargetKey == targetKey && pending.Crc32 == crc)
                {
                    return;
                }
            }

            uint transferId = _nextTransferId++;
            if (_nextTransferId == 0) _nextTransferId = 1;

            int totalBytes = payload.Length;
            int chunkSize = SkinPackCodec.CHUNK_PAYLOAD_SIZE;
            ushort totalChunks = (ushort)((totalBytes + chunkSize - 1) / chunkSize);

            _outboundQueue.Enqueue(new OutboundChunkJob
            {
                TargetKey = targetKey,
                TransferId = transferId,
                TotalChunks = totalChunks,
                TotalBytes = totalBytes,
                Crc32 = crc,
                AllowPersist = allowPersist,
                Payload = payload,
                ContentHash = SkinPackCodec.ComputeContentHash(payload),
                NextChunkIndex = 0,
                SendAction = send,
            });

            TickOutboundChunks();
        }

        private static bool IsPayloadWithinLimit(byte[] payload)
        {
            if (payload.Length <= SkinPackCodec.MAX_PACK_SIZE) return true;
            SkinSyncMod.ModLog.Warning($"皮肤包 {payload.Length} bytes 超过总上限 {SkinPackCodec.MAX_PACK_SIZE}，已跳过发送。");
            return false;
        }
    }
}
