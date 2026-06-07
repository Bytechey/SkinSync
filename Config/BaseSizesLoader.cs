using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>读皮肤目录下的 baseSizes.json，把每张标准化 sprite 的原始像素尺寸暴露给 SkinApplier 计算自适应 PPU。</summary>
    internal static class BaseSizesLoader
    {
        private static readonly Dictionary<string, Dictionary<string, Vector2Int>> _cache
            = new Dictionary<string, Dictionary<string, Vector2Int>>();

        /// <summary>返回 fileName(无扩展) → (baseW, baseH) 字典；缺文件 / 解析失败返回空字典。</summary>
        internal static Dictionary<string, Vector2Int> Load(string characterName)
        {
            if (string.IsNullOrEmpty(characterName)) return new Dictionary<string, Vector2Int>();
            if (_cache.TryGetValue(characterName, out var cached)) return cached;
            string path = Path.Combine(SkinPathResolver.GetSkinDir(characterName), "baseSizes.json");
            var dict = File.Exists(path) ? ParseContent(File.ReadAllText(path)) : new Dictionary<string, Vector2Int>();
            _cache[characterName] = dict;
            return dict;
        }

        /// <summary>从 baseSizes.json 文本解析 fileName → (baseW, baseH)；空 / 解析失败返回空字典。</summary>
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
                        int.TryParse(parts[0].Trim(), out int w) &&
                        int.TryParse(parts[1].Trim(), out int h) &&
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

        /// <summary>切皮肤 / rescan 时调用，让 baseSizes 重新读盘。</summary>
        internal static void Invalidate(string characterName)
        {
            if (string.IsNullOrEmpty(characterName)) _cache.Clear();
            else _cache.Remove(characterName);
        }
    }
}
