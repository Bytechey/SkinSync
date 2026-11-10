namespace SkinSyncMod
{
    /// <summary>子端向服务端请求拉取指定皮肤包；服务端再选一个持有该皮肤的子端单播请求。编解码见 SkinMessageCodec。</summary>
    public struct SkinPackRequestMessage
    {
        public string skinID;
    }
}
