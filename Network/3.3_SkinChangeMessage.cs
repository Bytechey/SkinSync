namespace SkinSyncMod
{
    /// <summary>皮肤切换消息体：字段布局由 Interop\Consumers\SkinMessageCodec 逐字段编解码。</summary>
    public struct SkinChangeMessage
    {
        public string skinID;   // 皮肤标识，比如 "st1"
        public uint netId;      // 玩家的 netId

        /// <summary>
        /// 发送者所在身体的 charType（0 = 未知）。
        /// Together 的 SyncBody.characterSelected 从未被读写，远端 Body.charType 只是「本机 prefab 的角色类型」，
        /// 同一玩家在不同观战者眼里可能不同，所以真实角色只能靠消息带。
        /// 仅用于日志诊断与翅膀缩放；贴图名解析仍必须用 body 自身的 sprite 名。
        /// </summary>
        public int charType;
    }

    public static class SkinNetworkIDs
    {
        public const ushort SkinChangeMessageId = 50000; // 选择一个未被占用的ID
        public const ushort AccessorySyncMessageId = 50001;
        public const ushort TailSyncMessageId = 50002;
        public const ushort SkinPackRequestMessageId = 50003;
        public const ushort SkinPackDataMessageId = 50004;
    }
}
