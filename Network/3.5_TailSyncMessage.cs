namespace SkinSyncMod
{
    /// <summary>
    /// 子端改动尾巴形变参数时发到服务端，服务端校验后广播全员。
    /// 18 字段对齐 TailDeformConfig；接收端覆盖到全局 static + 写 settings.SetTailOverride（按 skin）。编解码见 SkinMessageCodec。
    /// </summary>
    public struct TailSyncMessage
    {
        public uint netId;
        public string skinID;
        public bool enabled;
        public bool frontGuard;
        public int segments;
        public int constraintIters;
        public float damping;
        public float speedDamping;
        public float stiffness;
        public float maxBendDeg;
        public float anchorFollow;
        public float smoothness;
        public float maxStep;
        public float maxFixedDt;
        public float frontGuardMargin;
        public float gravityX;
        public float gravityY;
        public float windFreq;
        public float windAmp;
        public float speedDisturb;
    }
}
