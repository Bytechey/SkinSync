namespace SkinSyncMod
{
    /// <summary>皮肤切换消息体：字段布局由 Interop\Consumers\SkinMessageCodec 逐字段编解码。</summary>
    public struct SkinChangeMessage
    {
        public string skinID;   // 皮肤标识，比如 "st1"
        public uint netId;      // 玩家的 netId
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
