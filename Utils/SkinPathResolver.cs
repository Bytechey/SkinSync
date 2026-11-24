using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;

namespace SkinSyncMod
{
    /// <summary>解析皮肤目录的实际根路径、索引目录文件列表并校验皮肤名称安全性。</summary>
    public static class SkinPathResolver
    {
        private static readonly Dictionary<string, string> _resolved = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, bool> _localCustomCache = new Dictionary<string, bool>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string[]> _dirFilesCache = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        private static readonly string[] _markerDirs = { "Body", "Head", "Wings", "Accessories", "Blood" };
        private static readonly HashSet<string> _reservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

        public static string SyncCacheRoot => Path.Combine(Paths.CachePath, "SkinSync_SyncCache");

        internal static string LegacySyncCacheRoot => Path.Combine(Paths.PluginPath, "SkinSync_SyncCache");

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

            string customSpritesRoot = Path.Combine(Paths.PluginPath, "CustomSprites");
            string direct = Path.Combine(customSpritesRoot, characterName);
            string syncDir = Path.Combine(SyncCacheRoot, characterName);

            // 内容寻址目录：hash 形态的 id 只可能指向 _received/<hash>，代表"某一份具体内容"，
            // 与皮肤名解耦，因此不会与用户自建的同名皮肤混淆。
            if (SkinPackCodec.IsHashLike(characterName))
            {
                string received = Path.Combine(customSpritesRoot, "_received", characterName);
                if (Directory.Exists(received) && HasAnyContent(received))
                {
                    string receivedActual = ResolveActual(received);
                    _resolved[characterName] = receivedActual;
                    return receivedActual;
                }
            }

            if (IsEffectiveLocalCustomDir(customSpritesRoot, characterName, direct))
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

            // 原版内置槽位容器（Exp/Milky/Dune）或空目录不能被当作已就绪皮肤目录返回或缓存，否则会短路内存包回退与远端皮肤请求。
            if (!Directory.Exists(direct)) return direct;
            if (!Directory.Exists(syncDir)) return syncDir;
            return "";
        }

        /// <summary>判断本机 CustomSprites 目录下是否已存在包含有效贴图或配置的同名自有皮肤。</summary>
        public static bool HasLocalCustomSkin(string characterName)
        {
            if (!IsSafeSkinName(characterName)) return false;
            if (_localCustomCache.TryGetValue(characterName, out bool cached)) return cached;

            string customSpritesRoot = Path.Combine(Paths.PluginPath, "CustomSprites");
            string direct = Path.Combine(customSpritesRoot, characterName);
            bool exists = IsEffectiveLocalCustomDir(customSpritesRoot, characterName, direct);
            _localCustomCache[characterName] = exists;
            return exists;
        }

        private static bool IsEffectiveLocalCustomDir(string customSpritesRoot, string characterName, string directDir)
        {
            return Directory.Exists(directDir)
                && !Patches.LegacyTogetherSkinSuppressor.IsLegacyContainerDir(customSpritesRoot, characterName)
                && HasAnyContent(directDir);
        }

        /// <summary>返回并缓存指定皮肤目录下的全部文件路径列表；目录不存在或访问异常时缓存空数组。</summary>
        internal static IReadOnlyList<string> GetSkinFiles(string skinDir)
        {
            if (string.IsNullOrEmpty(skinDir)) return Array.Empty<string>();

            string normalized;
            try
            {
                normalized = Path.GetFullPath(skinDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return Array.Empty<string>();
            }

            if (_dirFilesCache.TryGetValue(normalized, out string[] cached))
            {
                return cached;
            }

            string[] files;
            if (!Directory.Exists(normalized))
            {
                files = Array.Empty<string>();
            }
            else
            {
                try
                {
                    files = Directory.GetFiles(normalized, "*", SearchOption.AllDirectories);
                }
                catch
                {
                    files = Array.Empty<string>();
                }
            }

            _dirFilesCache[normalized] = files;
            return files;
        }

        /// <summary>清除单个或全部皮肤目录的解析、本地归属与文件列表缓存。</summary>
        public static void Invalidate(string characterName = null)
        {
            if (string.IsNullOrEmpty(characterName))
            {
                _resolved.Clear();
                _localCustomCache.Clear();
            }
            else
            {
                _resolved.Remove(characterName);
                _localCustomCache.Remove(characterName);
            }

            _dirFilesCache.Clear();
            SkinPackCodec.InvalidatePackCache(characterName);
        }

        private static bool HasAnyContent(string dir)
        {
            try
            {
                foreach (string fp in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    if (fp.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                        || fp.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
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
