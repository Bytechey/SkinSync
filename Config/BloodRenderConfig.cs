namespace SkinSyncMod
{
    /// <summary>自定义血液渲染总开关；关闭时所有血液染色入口与扫描短路，避免每帧开销。</summary>
    public static class BloodRenderConfig
    {
        public static bool Enabled = true;
    }
}
