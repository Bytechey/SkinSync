namespace SkinSyncMod
{
    /// <summary>大型皮肤包分片传输网络消息体。</summary>
    public struct SkinPackChunkMessage
    {
        public uint transferId;
        public ushort chunkIndex;
        public ushort totalChunks;
        public int totalBytes;
        public uint crc32;
        public bool allowPersist;
        public byte[] data;

        /// <summary>
        /// 整包内容哈希（32 位十六进制，见 SkinPackCodec.HASH_LENGTH）；空或 null 表示发送方是旧版本。
        /// 与 crc32 的分工：crc32 只保证"这一次传输的字节没坏"，内容哈希标识"这是哪一份内容"，
        /// 可跨对端比较，用于判断本地同名皮肤是否就是同一份内容。追加在末尾，旧版本读取时忽略。
        /// </summary>
        public string contentHash;
    }
}
