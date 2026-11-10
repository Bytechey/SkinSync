// SkinSync Interop
// Copyright (c) 2026 Bytechey
// SPDX-License-Identifier: MIT

namespace SkinSyncMod.Interop.Consumers
{
    /// <summary>皮肤同步消息在契约原语上的字段级编解码。</summary>
    /// <remarks>
    /// 字段顺序与类型逐一对应 Network\3.3 至 3.7 各消息类原有的序列化代码，
    /// 但改走 IInteropWriter / IInteropReader，避免把传输库的写入器/读取器类型的实例方法签名带进消费方。
    /// 字符串走 PutString / GetString（与目标写入器的字符串编码一致）；
    /// 字节数组走 PutBytes / GetBytes（2 字节长度前缀，上限 65535）；
    /// 整数与浮点走 PutUInt / PutInt / PutBool / PutFloat 及其读取对应项。
    /// 子 ID 由 ChannelConsumer 写入，本类型只处理消息体。
    /// </remarks>
    internal static class SkinMessageCodec
    {
        /// <summary>写入皮肤切换消息体。</summary>
        internal static void WriteSkinChange(IInteropWriter writer, SkinChangeMessage message)
        {
            writer.PutString(message.skinID ?? string.Empty);
            writer.PutUInt(message.netId);
        }

        /// <summary>读取皮肤切换消息体。</summary>
        internal static SkinChangeMessage ReadSkinChange(IInteropReader reader)
        {
            SkinChangeMessage message = new SkinChangeMessage();
            message.skinID = reader.GetString();
            message.netId = reader.GetUInt();
            return message;
        }

        /// <summary>写入配件覆盖同步消息体。</summary>
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

        /// <summary>读取配件覆盖同步消息体。</summary>
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

        /// <summary>写入尾巴形变同步消息体（20 字段）。</summary>
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

        /// <summary>读取尾巴形变同步消息体（20 字段）。</summary>
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

        /// <summary>写入皮肤包请求消息体。</summary>
        internal static void WriteSkinPackRequest(IInteropWriter writer, SkinPackRequestMessage message)
        {
            writer.PutString(message.skinID ?? string.Empty);
        }

        /// <summary>读取皮肤包请求消息体。</summary>
        internal static SkinPackRequestMessage ReadSkinPackRequest(IInteropReader reader)
        {
            SkinPackRequestMessage message = new SkinPackRequestMessage();
            message.skinID = reader.GetString();
            return message;
        }

        /// <summary>写入皮肤包数据消息体；负载超过 65535 字节时由写入器拒绝，与目标读取器的长度前缀一致。</summary>
        internal static void WriteSkinPackData(IInteropWriter writer, SkinPackDataMessage message)
        {
            writer.PutBytes(message.payload ?? System.Array.Empty<byte>());
            writer.PutBool(message.allowPersist);
        }

        /// <summary>读取皮肤包数据消息体。</summary>
        internal static SkinPackDataMessage ReadSkinPackData(IInteropReader reader)
        {
            SkinPackDataMessage message = new SkinPackDataMessage();
            message.payload = reader.GetBytes();
            message.allowPersist = reader.GetBool();
            return message;
        }
    }
}
