using System;
using System.Collections.Generic;
using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>按发送端与传输编号重组大型皮肤包分片，执行首片包头预检、超时回收、并发限额与 CRC32 校验。</summary>
    internal static class SkinPackChunkAssembler
    {
        // 客户端接收服务端转发的分片时统一归入 senderKey = ushort.MaxValue，放宽至 16 以覆盖满房并发大皮肤包同步。
        private const int MaxTransfersPerSender = 16;
        private const float TransferTimeoutSec = 30f;

        /// <summary>当未完成分片因超时、配额淘汰、头元数据冲突或 CRC32 不一致被丢弃且已识别出 skinId 时触发。</summary>
        internal static Action<string> OnTransferDropped = SkinApplier.ClearRequestedPack;

        private sealed class InboundState
        {
            public uint SenderKey;
            public int TotalBytes;
            public ushort TotalChunks;
            public uint ExpectedCrc32;
            public bool AllowPersist;
            public string SkinId;
            public byte[] Buffer;
            public bool[] ReceivedMask;
            public int ReceivedChunks;
            public float UpdatedAt;
        }

        private static readonly Dictionary<ulong, InboundState> _pending = new Dictionary<ulong, InboundState>();
        private static readonly List<ulong> _scratchKeys = new List<ulong>();
        private static readonly uint[] _crcTable = BuildCrc32Table();

        internal static void Clear()
        {
            _pending.Clear();
            _scratchKeys.Clear();
        }

        /// <summary>喂入一片分片消息；全部收齐且 CRC32 校验通过时返回 true 并输出完整字节流。</summary>
        internal static bool TryPushChunk(
            uint senderKey,
            SkinPackChunkMessage msg,
            out byte[] completePayload,
            out bool allowPersist)
        {
            completePayload = null;
            allowPersist = false;
            PruneExpired();

            if (msg.transferId == 0
                || msg.totalBytes <= 0
                || msg.totalBytes > SkinPackCodec.MAX_PACK_SIZE
                || msg.data == null
                || msg.data.Length == 0
                || msg.data.Length > SkinPackCodec.CHUNK_PAYLOAD_SIZE)
            {
                return false;
            }

            int expectedChunks = (msg.totalBytes + SkinPackCodec.CHUNK_PAYLOAD_SIZE - 1) / SkinPackCodec.CHUNK_PAYLOAD_SIZE;
            if (expectedChunks <= 0 || expectedChunks > ushort.MaxValue || msg.totalChunks != (ushort)expectedChunks)
            {
                return false;
            }
            if (msg.chunkIndex >= msg.totalChunks)
            {
                return false;
            }

            int destOffset = msg.chunkIndex * SkinPackCodec.CHUNK_PAYLOAD_SIZE;
            int expectedSliceLen = Math.Min(SkinPackCodec.CHUNK_PAYLOAD_SIZE, msg.totalBytes - destOffset);
            if (msg.data.Length != expectedSliceLen)
            {
                return false;
            }

            ulong key = ((ulong)senderKey << 32) | msg.transferId;
            float now = Time.realtimeSinceStartup;

            if (!_pending.TryGetValue(key, out InboundState state))
            {
                // 可靠有序通道上第 0 片必先送达，未通过首片包头与安全名称预检前绝不分配整包缓冲区。
                if (msg.chunkIndex != 0
                    || !SkinPackCodec.TryPeekSkinId(msg.data, msg.data.Length, out string peekedSkinId))
                {
                    return false;
                }
                if (senderKey == ushort.MaxValue && SkinPathResolver.HasLocalCustomSkin(peekedSkinId))
                {
                    return false;
                }

                EnforceSenderQuota(senderKey);
                state = new InboundState
                {
                    SenderKey = senderKey,
                    TotalBytes = msg.totalBytes,
                    TotalChunks = msg.totalChunks,
                    ExpectedCrc32 = msg.crc32,
                    AllowPersist = msg.allowPersist,
                    SkinId = peekedSkinId,
                    Buffer = new byte[msg.totalBytes],
                    ReceivedMask = new bool[msg.totalChunks],
                    UpdatedAt = now,
                };
                _pending[key] = state;
            }
            else if (state.TotalBytes != msg.totalBytes
                || state.TotalChunks != msg.totalChunks
                || state.ExpectedCrc32 != msg.crc32
                || state.AllowPersist != msg.allowPersist)
            {
                _pending.Remove(key);
                NotifyDropped(state);
                return false;
            }

            Buffer.BlockCopy(msg.data, 0, state.Buffer, destOffset, msg.data.Length);

            state.UpdatedAt = now;
            if (!state.ReceivedMask[msg.chunkIndex])
            {
                state.ReceivedMask[msg.chunkIndex] = true;
                state.ReceivedChunks++;
            }

            if (state.ReceivedChunks < state.TotalChunks)
            {
                return false;
            }

            _pending.Remove(key);
            uint actualCrc = ComputeCrc32(state.Buffer);
            if (actualCrc != state.ExpectedCrc32)
            {
                ModLog.Warning($"SkinPackChunkAssembler: 分片重组 CRC32 不一致（expected=0x{state.ExpectedCrc32:X8}, actual=0x{actualCrc:X8}），已丢弃。");
                NotifyDropped(state);
                return false;
            }

            completePayload = state.Buffer;
            allowPersist = state.AllowPersist;
            return true;
        }

        internal static uint ComputeCrc32(byte[] data)
        {
            if (data == null || data.Length == 0) return 0u;
            uint crc = 0xFFFFFFFFu;
            for (int i = 0; i < data.Length; i++)
            {
                crc = (crc >> 8) ^ _crcTable[(crc ^ data[i]) & 0xFF];
            }
            return ~crc;
        }

        private static void NotifyDropped(InboundState state)
        {
            if (state == null || string.IsNullOrEmpty(state.SkinId)) return;
            try { OnTransferDropped?.Invoke(state.SkinId); }
            catch { }
        }

        private static void EnforceSenderQuota(uint senderKey)
        {
            int count = 0;
            ulong oldestKey = 0;
            float oldestTime = float.MaxValue;

            foreach (var kv in _pending)
            {
                if (kv.Value.SenderKey != senderKey) continue;
                count++;
                if (kv.Value.UpdatedAt < oldestTime)
                {
                    oldestTime = kv.Value.UpdatedAt;
                    oldestKey = kv.Key;
                }
            }

            if (count >= MaxTransfersPerSender && _pending.TryGetValue(oldestKey, out InboundState evicted))
            {
                _pending.Remove(oldestKey);
                ModLog.Warning($"SkinPackChunkAssembler: 发送端 {senderKey} 并发分片达上限 {MaxTransfersPerSender}，已淘汰未完成传输（skin={evicted.SkinId ?? "unknown"}）。");
                NotifyDropped(evicted);
            }
        }

        private static void PruneExpired()
        {
            if (_pending.Count == 0) return;
            float now = Time.realtimeSinceStartup;
            _scratchKeys.Clear();
            foreach (var kv in _pending)
            {
                if (now - kv.Value.UpdatedAt > TransferTimeoutSec)
                {
                    _scratchKeys.Add(kv.Key);
                }
            }
            for (int i = 0; i < _scratchKeys.Count; i++)
            {
                ulong k = _scratchKeys[i];
                if (_pending.TryGetValue(k, out InboundState expired))
                {
                    _pending.Remove(k);
                    NotifyDropped(expired);
                }
            }
            _scratchKeys.Clear();
        }

        private static uint[] BuildCrc32Table()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1u) != 0 ? (0xEDB88320u ^ (c >> 1)) : (c >> 1);
                }
                table[i] = c;
            }
            return table;
        }
    }
}
