namespace SkinSyncMod
{
    /// <summary>皮肤切换网络消息体。</summary>
    public struct SkinChangeMessage
    {
        public string skinID;
        public uint netId;
        public int charType;

        /// <summary>
        /// 皮肤内容哈希（32 位十六进制，见 SkinPackCodec.HASH_LENGTH）；空或 null 表示发送方是旧版本。
        /// 用于把「皮肤名」与「皮肤内容」解耦：接收端据此判断本地同名皮肤是否就是同一份内容。
        /// 追加在消息体末尾，旧版本读取时忽略尾部多余字节，因此不影响互通。
        /// </summary>
        public string contentHash;
    }

    /// <summary>定义 SkinSync 网络通道的子消息编号常量。</summary>
    public static class SkinNetworkIDs
    {
        public const ushort SkinChangeMessageId = 50000;
        public const ushort AccessorySyncMessageId = 50001;
        public const ushort TailSyncMessageId = 50002;
        public const ushort SkinPackRequestMessageId = 50003;
        public const ushort SkinPackDataMessageId = 50004;
        public const ushort SkinPackChunkMessageId = 50005;
        public const ushort ClientWorldReadyMessageId = 50006;
    }
}
