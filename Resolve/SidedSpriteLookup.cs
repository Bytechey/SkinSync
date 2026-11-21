using System.Collections.Generic;
using UnityEngine;

namespace SkinSyncMod.Resolve
{
    /// <summary>按角色左右朝向（R_ 前缀）与前后侧（F/B 后缀）优先级查找贴图。</summary>
    internal static class SidedSpriteLookup
    {
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
