using System;
using System.Collections.Generic;
using UnityEngine;

namespace SkinSyncMod.Character
{
    /// <summary>单个角色部位的原生贴图引用、名称与像素尺寸记录。</summary>
    internal sealed class PartBinding
    {
        internal string PartKey;
        internal string SpriteName;
        internal Sprite StockSprite;
        internal Vector2Int PixelSize;
        internal int SortingOrder;
    }

    /// <summary>单个角色的肢体、表情与尾巴部位绑定档案。</summary>
    internal sealed class CharacterProfile
    {
        internal int CharType;
        internal string PrefabPath;

        internal readonly Dictionary<string, PartBinding> Limbs =
            new Dictionary<string, PartBinding>(StringComparer.Ordinal);

        internal readonly Dictionary<string, PartBinding> Faces =
            new Dictionary<string, PartBinding>(StringComparer.Ordinal);

        internal PartBinding Tail;

        /// <summary>按部位键查询肢体、表情或尾巴绑定。</summary>
        internal PartBinding Find(string partKey)
        {
            if (partKey == null) return null;
            if (Limbs.TryGetValue(partKey, out var limb)) return limb;
            if (Faces.TryGetValue(partKey, out var face)) return face;
            if (partKey == PartKeyNames.Tail) return Tail;
            return null;
        }
    }
}
