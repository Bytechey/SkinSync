namespace SkinSyncMod
{
    /// <summary>配件参数覆盖同步消息体。</summary>
    public struct AccessorySyncMessage
    {
        public uint netId;
        public string skinID;
        public string accId;
        public bool enabled;
        public int offX;
        public int offY;
        public float rotation;
        public int zOrder;
    }
}
