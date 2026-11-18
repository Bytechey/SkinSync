using UnityEngine;

namespace SkinSyncMod.Resolve
{
    /// <summary>封装单次部位贴图解析的结果与命中来源。</summary>
    internal sealed class ResolveOutcome
    {
        internal Sprite Sprite;
        internal string SpriteName;
        internal ResolveOrigin Origin;
        internal int OwnerCharType = -1;

        internal bool Hit => Sprite != null;

        internal static ResolveOutcome Miss()
        {
            return new ResolveOutcome { Origin = ResolveOrigin.None, OwnerCharType = -1 };
        }
    }
}
