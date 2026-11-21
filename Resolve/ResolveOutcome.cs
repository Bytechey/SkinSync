using System.ComponentModel;
using UnityEngine;

namespace SkinSyncMod.Resolve
{
    /// <summary>封装单次部位贴图解析的结果与命中来源。</summary>
    public readonly struct ResolveOutcome
    {
        public Sprite Sprite { get; init; }
        public string SpriteName { get; init; }
        internal ResolveOrigin Origin { get; init; }
        public int OwnerCharType { get; init; }

        public bool Hit => Sprite != null;

        internal ResolveOutcome(Sprite sprite, string spriteName, ResolveOrigin origin, int ownerCharType = -1)
        {
            Sprite = sprite;
            SpriteName = spriteName;
            Origin = origin;
            OwnerCharType = ownerCharType;
        }

        public static ResolveOutcome Miss()
        {
            return new ResolveOutcome(null, null, ResolveOrigin.None, -1);
        }
    }
}

namespace System.Runtime.CompilerServices
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static class IsExternalInit { }
}
