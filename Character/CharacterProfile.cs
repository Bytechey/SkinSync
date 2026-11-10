using System;
using System.Collections.Generic;
using UnityEngine;

namespace SkinSyncMod.Character
{
    /// <summary>某角色在某个部位上使用的贴图及其原生像素尺寸。</summary>
    internal sealed class PartBinding
    {
        /// <summary>角色无关的部位键：肢体用 GameObject 名（UpTorso/HandF…），表情用字段语义键（defaultHead/eyeList[0].front…）。</summary>
        internal string PartKey;

        /// <summary>该角色这个部位用的贴图名，例如 milkytorso。</summary>
        internal string SpriteName;

        /// <summary>该贴图在游戏里的原生像素尺寸（pixelsPerUnit 恒为 8 时即世界尺寸 ×8）。</summary>
        internal Vector2Int PixelSize;

        /// <summary>SpriteRenderer.sortingOrder，仅用于诊断输出。</summary>
        internal int SortingOrder;
    }

    /// <summary>
    /// 一个角色的渲染档案：15 个肢体 + 表情各字段 + 尾巴，全部在运行时从 prefab 采集。
    /// 存在的意义是把"游戏事实"集中到一处，避免跨角色映射表散落在渲染逻辑里。
    /// </summary>
    internal sealed class CharacterProfile
    {
        /// <summary>Body.charType（0=Experiment、1=Milky、2=Dune，未来可能更多）。</summary>
        internal int CharType;

        /// <summary>Resources 路径，仅用于诊断。</summary>
        internal string PrefabPath;

        /// <summary>部位键（肢体 GameObject 名）→ 绑定。</summary>
        internal readonly Dictionary<string, PartBinding> Limbs =
            new Dictionary<string, PartBinding>(StringComparer.Ordinal);

        /// <summary>表情语义键 → 绑定。</summary>
        internal readonly Dictionary<string, PartBinding> Faces =
            new Dictionary<string, PartBinding>(StringComparer.Ordinal);

        /// <summary>尾巴；该角色没有 TailScript 时为 null。</summary>
        internal PartBinding Tail;

        /// <summary>查某个部位键的绑定（先肢体后表情）。</summary>
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
