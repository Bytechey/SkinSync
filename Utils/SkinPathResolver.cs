using BepInEx;
using System.Collections.Generic;
using System.IO;

namespace SkinSyncMod
{
    /// <summary>解析皮肤目录真实根：顶层缺 Body/Head/Wings/Accessories/Blood 时自动下钻一层（兼容 zip 多包一层）。</summary>
    public static class SkinPathResolver
    {
        private static readonly Dictionary<string, string> _resolved = new Dictionary<string, string>();
        private static readonly string[] _markerDirs = { "Body", "Head", "Wings", "Accessories", "Blood" };

        /// <summary>取皮肤实际根目录；缺失时返回直系路径（外部仍可 Directory.Exists 检查）。</summary>
        public static string GetSkinDir(string characterName)
        {
            if (string.IsNullOrEmpty(characterName)) return "";
            if (_resolved.TryGetValue(characterName, out var cached)) return cached;
            string direct = Path.Combine(Paths.PluginPath, "CustomSprites", characterName);
            string actual = ResolveActual(direct);
            _resolved[characterName] = actual;
            return actual;
        }

        /// <summary>切皮肤 / rescan 后调，下次 GetSkinDir 重新检测嵌套层。</summary>
        public static void Invalidate(string characterName = null)
        {
            if (string.IsNullOrEmpty(characterName)) _resolved.Clear();
            else _resolved.Remove(characterName);
        }

        private static string ResolveActual(string dir)
        {
            if (!Directory.Exists(dir)) return dir;
            if (HasMarker(dir)) return dir;
            string[] subs = Directory.GetDirectories(dir);
            foreach (var sub in subs)
            {
                if (HasMarker(sub)) return sub;
            }
            return dir;
        }

        private static bool HasMarker(string dir)
        {
            foreach (var m in _markerDirs)
            {
                if (Directory.Exists(Path.Combine(dir, m))) return true;
            }
            return false;
        }
    }
}
