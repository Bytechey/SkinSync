using System.Collections.Generic;
using UnityEngine;

namespace SkinSyncMod.Resolve
{
    /// <summary>
    /// 朝向变体的贴图查找：同一张部位图可能有 R_ 前缀（朝右专用）与 F/B 后缀（前后侧）。
    /// </summary>
    /// <remarks>
    /// 候选键序（朝右时）：R_&lt;名&gt;侧 → R_&lt;名&gt; → &lt;名&gt;侧 → &lt;名&gt; → R_&lt;名&gt;反向侧 → &lt;名&gt;反向侧。
    /// 反向侧兜底是为了让只画了一侧的作品不至于在转身时消失。
    /// </remarks>
    internal static class SidedSpriteLookup
    {
        /// <summary>按侧向规则在皮肤字典里查；未命中返回 null。</summary>
        internal static Sprite Find(Dictionary<string, Sprite> dict, string baseName, char side, bool isRight)
        {
            if (dict == null || string.IsNullOrEmpty(baseName)) return null;

            bool hasSide = side == 'F' || side == 'B';

            if (isRight)
            {
                if (hasSide && dict.TryGetValue("R_" + baseName + side, out var a)) return a;
                if (dict.TryGetValue("R_" + baseName, out var b)) return b;
            }
            if (hasSide && dict.TryGetValue(baseName + side, out var c)) return c;
            if (dict.TryGetValue(baseName, out var d)) return d;

            if (hasSide)
            {
                char opposite = side == 'F' ? 'B' : 'F';
                if (isRight && dict.TryGetValue("R_" + baseName + opposite, out var e)) return e;
                if (dict.TryGetValue(baseName + opposite, out var f)) return f;
            }
            return null;
        }
    }
}
