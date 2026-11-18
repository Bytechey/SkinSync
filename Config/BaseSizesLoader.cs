using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>解析并缓存皮肤目录下的 baseSizes.json 原始尺寸配置。</summary>
    internal static class BaseSizesLoader
    {
        private static readonly Dictionary<string, Dictionary<string, Vector2Int>> _cache
            = new Dictionary<string, Dictionary<string, Vector2Int>>();

        /// <summary>读取指定皮肤的贴图基准像素尺寸表，缺失或失败返回空字典。</summary>
        internal static Dictionary<string, Vector2Int> Load(string characterName)
        {
            if (string.IsNullOrEmpty(characterName)) return new Dictionary<string, Vector2Int>();
            if (_cache.TryGetValue(characterName, out var cached)) return cached;
            string skinDir = SkinPathResolver.GetSkinDir(characterName);
            if (string.IsNullOrEmpty(skinDir)) return new Dictionary<string, Vector2Int>();
            string path = Path.Combine(skinDir, "baseSizes.json");
            var dict = File.Exists(path) ? ParseContent(File.ReadAllText(path)) : new Dictionary<string, Vector2Int>();
            _cache[characterName] = dict;
            return dict;
        }

        /// <summary>从 JSON 文本解析贴图名到基准像素尺寸的映射。</summary>
        internal static Dictionary<string, Vector2Int> ParseContent(string json)
        {
            var dict = new Dictionary<string, Vector2Int>();
            if (string.IsNullOrEmpty(json)) return dict;
            try
            {
                int entriesIdx = json.IndexOf("\"entries\"", System.StringComparison.Ordinal);
                if (entriesIdx < 0) return dict;
                int p = entriesIdx;
                while (true)
                {
                    int keyStart = json.IndexOf('"', p + 1);
                    if (keyStart < 0) break;
                    int keyEnd = json.IndexOf('"', keyStart + 1);
                    if (keyEnd < 0) break;
                    string key = json.Substring(keyStart + 1, keyEnd - keyStart - 1);
                    if (key == "entries") { p = keyEnd + 1; continue; }
                    int arrStart = json.IndexOf('[', keyEnd + 1);
                    int arrEnd = json.IndexOf(']', arrStart + 1);
                    if (arrStart < 0 || arrEnd < 0) break;
                    string nums = json.Substring(arrStart + 1, arrEnd - arrStart - 1);
                    var parts = nums.Split(',');
                    if (parts.Length >= 2 &&
                        int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int w) &&
                        int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int h) &&
                        w > 0 && h > 0)
                    {
                        int slash = key.LastIndexOf('/');
                        string fileName = slash >= 0 ? key.Substring(slash + 1) : key;
                        if (!dict.ContainsKey(fileName))
                            dict[fileName] = new Vector2Int(w, h);
                    }
                    p = arrEnd + 1;
                    if (p >= json.Length) break;
                }
            }
            catch { }
            return dict;
        }

        /// <summary>失效单个或全部皮肤的基准尺寸缓存。</summary>
        internal static void Invalidate(string characterName = null)
        {
            if (string.IsNullOrEmpty(characterName)) _cache.Clear();
            else _cache.Remove(characterName);
        }
    }
}
