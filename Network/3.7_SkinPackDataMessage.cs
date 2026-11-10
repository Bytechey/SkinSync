namespace SkinSyncMod
{
    /// <summary>承载 SkinPackCodec 打包后的皮肤目录字节流；接收端调用 SkinPackCodec.UnpackAndCache 写缓存。编解码见 SkinMessageCodec。</summary>
    public struct SkinPackDataMessage
    {
        public byte[] payload;
        public bool allowPersist;
    }
}
