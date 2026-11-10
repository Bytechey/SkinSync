namespace SkinSyncMod.Resolve
{
    /// <summary>命中来源，用于诊断输出与覆盖率统计。</summary>
    internal enum ResolveOrigin
    {
        /// <summary>没找到任何候选，保持游戏原图。</summary>
        None = 0,

        /// <summary>命中当前角色的贴图名（为这个角色定制的皮肤）。</summary>
        Native = 1,

        /// <summary>命中部位键名，例如 UpTorso.png（角色无关的通用命名）。</summary>
        Generic = 2,

        /// <summary>命中其他角色的同名部位（跨角色借用）。</summary>
        CrossCharacter = 3,
    }
}
