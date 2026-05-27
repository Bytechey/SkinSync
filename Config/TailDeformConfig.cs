using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>
    /// 尾巴形变运行时全局配置。
    /// </summary>
    public static class TailDeformConfig
    {
        public static int Segments = 10;
        public static int ConstraintIters = 6;

        public static float Damping = 0.45f;
        public static float SpeedDamping = 0.05f;
        public static float Stiffness = 1.0f;
        public static float GravityX = 0f;
        public static float GravityY = -1.2f;

        public static float WindFreq = 1.2f;
        public static float WindAmp = 0.012f;
        public static float SpeedDisturb = 0.005f;

        public static float MaxBendDeg = 18f;
        public static float AnchorFollow = 1.0f;

        public static float Smoothness = 0.55f;
        public static bool FrontGuard = true;
        public static float FrontGuardMargin = 0.05f;
        public static float MaxStep = 0.15f;
        public static float MaxFixedDt = 0.02f;

        public static bool Enabled = true;
    }
}
