using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;

namespace SkinSyncMod
{
    /// <summary>解析皮肤目录的实际根路径并校验皮肤名称安全性。</summary>
    public static class SkinPathResolver
    {
        private static readonly Dictionary<string, string> _resolved = new Dictionary<string, string>();
        private static readonly string[] _markerDirs = { "Body", "Head", "Wings", "Accessories", "Blood" };
        private static readonly HashSet<string> _reservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

        public static string SyncCacheRoot => Path.Combine(Paths.PluginPath, "SkinSync_SyncCache");

        /// <summary>校验皮肤标识是否为不含路径穿越与系统保留设备名的合法单级目录名。</summary>
        public static bool IsSafeSkinName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 64) return false;
            if (char.IsWhiteSpace(name[0]) || char.IsWhiteSpace(name[name.Length - 1])) return false;
            if (name[name.Length - 1] == '.') return false;
            if (name == "." || name.IndexOf("..", StringComparison.Ordinal) >= 0) return false;

            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (char.IsControl(c)) return false;
                if (c == '/' || c == '\\' || c == ':' || c == '*' || c == '?' || c == '"' || c == '<' || c == '>' || c == '|')
                    return false;
            }

            int dot = name.IndexOf('.');
            string stem = dot >= 0 ? name.Substring(0, dot) : name;
            return !_reservedNames.Contains(stem);
        }

        internal static bool IsReservedFileStem(string segment)
        {
            if (string.IsNullOrEmpty(segment)) return true;
            int dot = segment.IndexOf('.');
            string stem = dot >= 0 ? segment.Substring(0, dot) : segment;
            return _reservedNames.Contains(stem);
        }

        /// <summary>解析指定皮肤的实际根目录，兼容单层压缩包嵌套与临时会话缓存目录。</summary>
        public static string GetSkinDir(string characterName)
        {
            if (!IsSafeSkinName(characterName)) return "";
            if (_resolved.TryGetValue(characterName, out var cached)) return cached;

            string direct = Path.Combine(Paths.PluginPath, "CustomSprites", characterName);
            string syncDir = Path.Combine(SyncCacheRoot, characterName);

            if (Directory.Exists(direct) && HasAnyContent(direct))
            {
                string actual = ResolveActual(direct);
                _resolved[characterName] = actual;
                return actual;
            }
            if (Directory.Exists(syncDir) && HasAnyContent(syncDir))
            {
                string actual = ResolveActual(syncDir);
                _resolved[characterName] = actual;
                return actual;
            }
            if (Directory.Exists(direct))
            {
                _resolved[characterName] = direct;
                return direct;
            }

            return direct;
        }

        /// <summary>清除单个或全部皮肤目录的解析缓存。</summary>
        public static void Invalidate(string characterName = null)
        {
            if (string.IsNullOrEmpty(characterName)) _resolved.Clear();
            else _resolved.Remove(characterName);
        }

        private static bool HasAnyContent(string dir)
        {
            try
            {
                foreach (var _ in Directory.EnumerateFileSystemEntries(dir))
                    return true;
            }
            catch { }
            return false;
        }

        private static string ResolveActual(string dir)
        {
            if (!Directory.Exists(dir)) return dir;
            if (HasMarker(dir)) return dir;
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
