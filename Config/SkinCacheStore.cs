using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>按 SteamID 读写并持久化玩家上次使用的皮肤标识。</summary>
    public static class SkinCacheStore
    {
        private static Dictionary<ulong, string> _cache;
        private static bool _loaded;

        private static string FilePath => Path.Combine(Application.persistentDataPath, "SkinSync", "skinCache.txt");

        /// <summary>查询指定 SteamID 记录的皮肤标识，无记录返回空串。</summary>
        public static string Get(ulong steamId)
        {
            if (steamId == 0UL) return string.Empty;
            EnsureLoaded();
            return _cache.TryGetValue(steamId, out string id) ? id : string.Empty;
        }

        /// <summary>保存并落盘 SteamID 与皮肤标识的映射。</summary>
        public static void Set(ulong steamId, string skinId)
        {
            if (steamId == 0UL || string.IsNullOrEmpty(skinId)) return;
            if (skinId.IndexOfAny(new[] { '=', '\r', '\n' }) >= 0) return;
            EnsureLoaded();
            if (_cache.TryGetValue(steamId, out string existing) && existing == skinId) return;
            _cache[steamId] = skinId;
            Save();
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _cache = new Dictionary<ulong, string>();
            _loaded = true;
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (string raw in File.ReadAllLines(FilePath))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0 || eq >= line.Length - 1) continue;
                    if (!ulong.TryParse(line.Substring(0, eq), out ulong sid) || sid == 0UL) continue;
                    string skin = line.Substring(eq + 1).Trim();
                    if (skin.Length == 0) continue;
                    _cache[sid] = skin;
                }
                SkinSyncMod.ModLog.Info($"SkinCacheStore loaded {_cache.Count} entries from {FilePath}");
            }
            catch (System.Exception ex)
            {
                SkinSyncMod.ModLog.Warning("SkinCacheStore load failed: " + ex.Message);
            }
        }

        private static void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var sb = new StringBuilder();
                foreach (var kv in _cache) sb.Append(kv.Key).Append('=').Append(kv.Value).Append('\n');
                File.WriteAllText(FilePath, sb.ToString());
            }
            catch (System.Exception ex)
            {
                SkinSyncMod.ModLog.Warning("SkinCacheStore save failed: " + ex.Message);
            }
        }
    }
}
