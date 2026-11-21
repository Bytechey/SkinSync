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
    }
}
