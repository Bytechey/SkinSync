// SkinSync Interop
// Copyright (c) 2026 Bytechey
// SPDX-License-Identifier: MIT

namespace SkinSyncMod.Interop.Consumers
{
    /// <summary>对各类皮肤同步网络消息执行字段级序列化与反序列化。</summary>
    internal static class SkinMessageCodec
    {
        internal static void WriteSkinChange(IInteropWriter writer, SkinChangeMessage message)
        {
            writer.PutString(message.skinID ?? string.Empty);
            writer.PutUInt(message.netId);
            writer.PutInt(message.charType);
        }

        internal static SkinChangeMessage ReadSkinChange(IInteropReader reader)
        {
            SkinChangeMessage message = new SkinChangeMessage();
            message.skinID = reader.GetString();
            message.netId = reader.GetUInt();
            // 兼容未携带 charType 字段的旧版客户端消息。
            message.charType = reader.RemainingBytes >= 4 ? reader.GetInt() : 0;
            return message;
        }

        internal static void WriteAccessory(IInteropWriter writer, AccessorySyncMessage message)
        {
            writer.PutUInt(message.netId);
            writer.PutString(message.skinID ?? string.Empty);
            writer.PutString(message.accId ?? string.Empty);
            writer.PutBool(message.enabled);
            writer.PutInt(message.offX);
            writer.PutInt(message.offY);
            writer.PutFloat(message.rotation);
            writer.PutInt(message.zOrder);
        }

        internal static AccessorySyncMessage ReadAccessory(IInteropReader reader)
        {
            AccessorySyncMessage message = new AccessorySyncMessage();
            message.netId = reader.GetUInt();
            message.skinID = reader.GetString();
            message.accId = reader.GetString();
            message.enabled = reader.GetBool();
            message.offX = reader.GetInt();
            message.offY = reader.GetInt();
            message.rotation = reader.GetFloat();
            message.zOrder = reader.GetInt();
            return message;
        }

        internal static void WriteTail(IInteropWriter writer, TailSyncMessage message)
        {
            writer.PutUInt(message.netId);
            writer.PutString(message.skinID ?? string.Empty);
            writer.PutBool(message.enabled);
            writer.PutBool(message.frontGuard);
            writer.PutInt(message.segments);
            writer.PutInt(message.constraintIters);
            writer.PutFloat(message.damping);
            writer.PutFloat(message.speedDamping);
            writer.PutFloat(message.stiffness);
            writer.PutFloat(message.maxBendDeg);
            writer.PutFloat(message.anchorFollow);
            writer.PutFloat(message.smoothness);
            writer.PutFloat(message.maxStep);
            writer.PutFloat(message.maxFixedDt);
            writer.PutFloat(message.frontGuardMargin);
            writer.PutFloat(message.gravityX);
            writer.PutFloat(message.gravityY);
            writer.PutFloat(message.windFreq);
            writer.PutFloat(message.windAmp);
            writer.PutFloat(message.speedDisturb);
        }

        internal static TailSyncMessage ReadTail(IInteropReader reader)
        {
            TailSyncMessage message = new TailSyncMessage();
            message.netId = reader.GetUInt();
            message.skinID = reader.GetString();
            message.enabled = reader.GetBool();
            message.frontGuard = reader.GetBool();
            message.segments = reader.GetInt();
            message.constraintIters = reader.GetInt();
            message.damping = reader.GetFloat();
            message.speedDamping = reader.GetFloat();
            message.stiffness = reader.GetFloat();
            message.maxBendDeg = reader.GetFloat();
            message.anchorFollow = reader.GetFloat();
            message.smoothness = reader.GetFloat();
            message.maxStep = reader.GetFloat();
            message.maxFixedDt = reader.GetFloat();
            message.frontGuardMargin = reader.GetFloat();
            message.gravityX = reader.GetFloat();
            message.gravityY = reader.GetFloat();
            message.windFreq = reader.GetFloat();
            message.windAmp = reader.GetFloat();
            message.speedDisturb = reader.GetFloat();
            return message;
        }

        internal static void WriteSkinPackRequest(IInteropWriter writer, SkinPackRequestMessage message)
        {
            writer.PutString(message.skinID ?? string.Empty);
        }

        internal static SkinPackRequestMessage ReadSkinPackRequest(IInteropReader reader)
        {
            SkinPackRequestMessage message = new SkinPackRequestMessage();
            message.skinID = reader.GetString();
            return message;
        }

        internal static void WriteSkinPackData(IInteropWriter writer, SkinPackDataMessage message)
        {
            writer.PutBytes(message.payload ?? System.Array.Empty<byte>());
            writer.PutBool(message.allowPersist);
        }

        internal static SkinPackDataMessage ReadSkinPackData(IInteropReader reader)
        {
            SkinPackDataMessage message = new SkinPackDataMessage();
            message.payload = reader.GetBytes();
            message.allowPersist = reader.GetBool();
            return message;
        }

        internal static void WriteSkinPackChunk(IInteropWriter writer, SkinPackChunkMessage message)
        {
            writer.PutUInt(message.transferId);
            writer.PutUShort(message.chunkIndex);
            writer.PutUShort(message.totalChunks);
            writer.PutInt(message.totalBytes);
            writer.PutUInt(message.crc32);
            writer.PutBool(message.allowPersist);
            writer.PutBytes(message.data ?? System.Array.Empty<byte>());
        }

        internal static SkinPackChunkMessage ReadSkinPackChunk(IInteropReader reader)
        {
            SkinPackChunkMessage message = new SkinPackChunkMessage();
            message.transferId = reader.GetUInt();
            message.chunkIndex = reader.GetUShort();
            message.totalChunks = reader.GetUShort();
            message.totalBytes = reader.GetInt();
            message.crc32 = reader.GetUInt();
            message.allowPersist = reader.GetBool();
            message.data = reader.GetBytes();
            return message;
        }
    }
}
