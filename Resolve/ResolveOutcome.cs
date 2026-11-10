using UnityEngine;

namespace SkinSyncMod.Resolve
{
    /// <summary>一次部位解析的结果。</summary>
    internal sealed class ResolveOutcome
    {
        /// <summary>解析到的贴图；未命中时为 null。</summary>
        internal Sprite Sprite;

        /// <summary>解析到的贴图名，便于日志说明「借用了谁」。</summary>
        internal string SpriteName;

        /// <summary>来源。</summary>
        internal ResolveOrigin Origin;

        /// <summary>跨角色命中时，这张贴图原本属于哪个 charType；其他情况为 -1。</summary>
        internal int OwnerCharType = -1;

        internal bool Hit => Sprite != null;

        internal static ResolveOutcome Miss()
        {
            return new ResolveOutcome { Origin = ResolveOrigin.None, OwnerCharType = -1 };
        }
    }
}
