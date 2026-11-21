using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>解析并缓存皮肤目录下的 baseSizes.json 原始尺寸配置。</summary>
    internal static class BaseSizesLoader
    {
        private static readonly char[] PathSeparators = { '/', '\\' };

        private static readonly Dictionary<string, Dictionary<string, Vector2Int>> _cache
            = new Dictionary<string, Dictionary<string, Vector2Int>>(StringComparer.Ordinal);

        /// <summary>读取指定皮肤的贴图基准像素尺寸表，目录不存在、缺失或失败时均缓存并返回空字典。</summary>
        internal static Dictionary<string, Vector2Int> Load(string characterName)
        {
            if (string.IsNullOrEmpty(characterName)) return new Dictionary<string, Vector2Int>();
            if (_cache.TryGetValue(characterName, out var cached)) return cached;

            var dict = new Dictionary<string, Vector2Int>();
            string skinDir = SkinPathResolver.GetSkinDir(characterName);
            if (!string.IsNullOrEmpty(skinDir) && Directory.Exists(skinDir))
            {
                string path = Path.Combine(skinDir, "baseSizes.json");
                if (File.Exists(path))
                {
                    try
                    {
                        dict = ParseContent(File.ReadAllText(path));
                    }
                    catch (Exception ex)
                    {
                        ModLog.Warning("baseSizes.json read failed: " + ex.Message);
                    }
                }
            }
            _cache[characterName] = dict;
            return dict;
        }

        /// <summary>从 JSON 文本安全解析 entries 下贴图名到 [w, h] 基准像素尺寸的映射。</summary>
        internal static Dictionary<string, Vector2Int> ParseContent(string json)
        {
            var dict = new Dictionary<string, Vector2Int>();
            if (string.IsNullOrWhiteSpace(json)) return dict;
            try
            {
                JObject root = JObject.Parse(json);
                if (!(root["entries"] is JObject entriesObj)) return dict;
                foreach (JProperty prop in entriesObj.Properties())
                {
                    if (!(prop.Value is JArray arr) || arr.Count < 2) continue;
                    if (!TryReadPositiveInt(arr[0], out int w) || !TryReadPositiveInt(arr[1], out int h)) continue;

                    string key = prop.Name ?? string.Empty;
                    int slash = key.LastIndexOfAny(PathSeparators);
                    string fileName = slash >= 0 ? key.Substring(slash + 1) : key;
                    if (fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    {
                        fileName = fileName.Substring(0, fileName.Length - 4);
                    }
                    if (!string.IsNullOrEmpty(fileName) && !dict.ContainsKey(fileName))
                    {
                        dict[fileName] = new Vector2Int(w, h);
                    }
                }
            }
            catch (Exception ex)
            {
                ModLog.Warning("baseSizes.json parse failed: " + ex.Message);
            }
            return dict;
        }

        private static bool TryReadPositiveInt(JToken token, out int value)
        {
            value = 0;
            if (token == null) return false;
            if (token.Type == JTokenType.Integer)
            {
                long v = token.Value<long>();
                if (v <= 0 || v > int.MaxValue) return false;
                value = (int)v;
                return true;
            }
            if (token.Type == JTokenType.Float)
            {
                double dv = token.Value<double>();
                if (double.IsNaN(dv) || double.IsInfinity(dv) || dv <= 0d || dv > int.MaxValue || dv != Math.Floor(dv))
                {
                    return false;
                }
                value = (int)dv;
                return true;
            }
            return false;
        }

        /// <summary>失效单个或全部皮肤的基准尺寸缓存。</summary>
        internal static void Invalidate(string characterName = null)
        {
            if (string.IsNullOrEmpty(characterName)) _cache.Clear();
            else _cache.Remove(characterName);
        }
    }
}
