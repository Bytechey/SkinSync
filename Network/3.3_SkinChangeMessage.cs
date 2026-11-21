namespace SkinSyncMod
{
    /// <summary>皮肤切换网络消息体。</summary>
    public struct SkinChangeMessage
    {
        public string skinID;
        public uint netId;
        public int charType;
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
