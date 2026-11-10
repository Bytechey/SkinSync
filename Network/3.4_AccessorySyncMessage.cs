namespace SkinSyncMod
{
    /// <summary>
    /// 子端改动配件覆盖时发到服务端，服务端校验后广播全员。
    /// 字段对齐 SkinSyncSettings.AccessoryOverride（5 个字段任选其一改动 + enabled 必填），编解码见 SkinMessageCodec。
    /// </summary>
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
