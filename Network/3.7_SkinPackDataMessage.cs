namespace SkinSyncMod
{
    /// <summary>承载打包后皮肤目录字节流与落盘许可标记的消息体。</summary>
    public struct SkinPackDataMessage
    {
        public byte[] payload;
        public bool allowPersist;
    }
}
