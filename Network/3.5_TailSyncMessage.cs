namespace SkinSyncMod
{
    /// <summary>尾巴形变参数同步消息体。</summary>
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
