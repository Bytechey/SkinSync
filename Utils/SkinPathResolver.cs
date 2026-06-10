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

        /// <summary>同步缓存根：远端皮肤包 persist=false 时落到此处，下次启动可清空。</summary>
        public static string SyncCacheRoot
            => Path.Combine(BepInEx.Paths.PluginPath, "SkinSync_SyncCache");

        /// <summary>取皮肤实际根目录；缺失时返回直系路径（外部仍可 Directory.Exists 检查）。</summary>
        public static string GetSkinDir(string characterName)
        {
            if (string.IsNullOrEmpty(characterName)) return "";
            if (_resolved.TryGetValue(characterName, out var cached)) return cached;
            string direct = Path.Combine(BepInEx.Paths.PluginPath, "CustomSprites", characterName);
            if (Directory.Exists(direct))
            {
                string actual = ResolveActual(direct);
                _resolved[characterName] = actual;
                return actual;
            }
            string syncDir = Path.Combine(SyncCacheRoot, characterName);
            if (Directory.Exists(syncDir))
            {
                string actual = ResolveActual(syncDir);
                _resolved[characterName] = actual;
                return actual;
            }
            _resolved[characterName] = direct;
            return direct;
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
            // 仅当外层目录"看起来是 zip 多套一层"——没有任何 PNG 且只有 1 个子目录——才下钻；
            // 否则保留 dir 本身，避免把同目录里的另一个皮肤包子目录误判为本皮肤的根。
            string[] pngs = Directory.GetFiles(dir, "*.png", SearchOption.TopDirectoryOnly);
            if (pngs.Length > 0) return dir;
            string[] subs = Directory.GetDirectories(dir);
            if (subs.Length == 1 && HasMarker(subs[0])) return subs[0];
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
