using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>解析皮肤目录下的 blood.json 自定义血液与粒子配置。</summary>
    public static class BloodConfigLoader
    {
        public class Config
        {
            public Color32? ParticleStartColor;
            public Color32? ParticleEndColor;
            public Color32[] RandomColors;
            public float? ParticleSize;
            public float? ParticleLifetime;
            public string FadeCurve;

            public Color32? BloodDark;
            public Color32? BloodLight;

            public bool? Glow;
            public float? GlowIntensity;

            public bool? Animated;
            public int? AnimFrames;
            public float? AnimFps;
        }

        /// <summary>从皮肤目录读取 blood.json，缺失或失败返回 null。</summary>
        public static Config Load(string skinDir)
        {
            if (string.IsNullOrEmpty(skinDir)) return null;
            string path = Path.Combine(skinDir, "blood.json");
            if (!File.Exists(path)) return null;
            try { return Parse(File.ReadAllText(path)); }
            catch (System.Exception ex)
            {
                SkinSyncMod.ModLog.Warning("blood.json load failed: " + ex.Message);
                return null;
            }
        }

        /// <summary>从 JSON 文本解析血液配置，失败返回 null。</summary>
        public static Config Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            try
            {
                var raw = JsonConvert.DeserializeObject<RawConfig>(text);
                if (raw == null) return null;
                return new Config
                {
                    ParticleStartColor = ParseColor(raw.particleStartColor),
                    ParticleEndColor = ParseColor(raw.particleEndColor),
                    RandomColors = ParseColorList(raw.randomColors),
                    ParticleSize = raw.particleSize,
                    ParticleLifetime = raw.particleLifetime,
                    FadeCurve = raw.fadeCurve,
                    BloodDark = ParseColor(raw.bloodDark),
                    BloodLight = ParseColor(raw.bloodLight),
                    Glow = raw.glow,
                    GlowIntensity = raw.glowIntensity,
                    Animated = raw.animated,
                    AnimFrames = raw.animFrames,
                    AnimFps = raw.animFps,
                };
            }
            catch (System.Exception ex)
            {
                SkinSyncMod.ModLog.Warning("blood.json parse failed: " + ex.Message);
                return null;
            }
        }

        private static Color32? ParseColor(int[] arr)
        {
            if (arr == null || arr.Length < 3) return null;
            byte r = (byte)Mathf.Clamp(arr[0], 0, 255);
            byte g = (byte)Mathf.Clamp(arr[1], 0, 255);
            byte b = (byte)Mathf.Clamp(arr[2], 0, 255);
            byte a = arr.Length >= 4 ? (byte)Mathf.Clamp(arr[3], 0, 255) : (byte)255;
            return new Color32(r, g, b, a);
        }

        private static Color32[] ParseColorList(int[][] arrs)
        {
            if (arrs == null || arrs.Length == 0) return null;
            var list = new System.Collections.Generic.List<Color32>();
            foreach (var arr in arrs)
            {
                var c = ParseColor(arr);
                if (c.HasValue) list.Add(c.Value);
            }
            return list.Count > 0 ? list.ToArray() : null;
        }

        private class RawConfig
        {
#pragma warning disable CS0649
            public int[] particleStartColor;
            public int[] particleEndColor;
            public int[][] randomColors;
            public float? particleSize;
            public float? particleLifetime;
            public string fadeCurve;
            public int[] bloodDark;
            public int[] bloodLight;
            public bool? glow;
            public float? glowIntensity;
            public bool? animated;
            public int? animFrames;
            public float? animFps;
#pragma warning restore CS0649
        }
    }
}
