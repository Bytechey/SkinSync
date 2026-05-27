using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>
    /// 解析 blood.json（位于皮肤目录下）：per-skin 自定义血液颜色 + 粒子贴图 + 粒子动效 / 光影。
    /// 缺字段回落游戏默认；blood.json 缺失时返回 null（外部按"无自定义"处理）。
    /// </summary>
    public static class BloodConfigLoader
    {
        public class Config
        {
            /// <summary>粒子起始色（含 alpha）。null = 不覆盖。</summary>
            public Color32? ParticleStartColor;
            /// <summary>粒子末尾色，与 startColor 形成渐变；null = 不渐变。</summary>
            public Color32? ParticleEndColor;
            /// <summary>多色随机池：每次发射粒子时从池中随机抽一色覆盖 startColor。null/空 = 不随机（默认）。</summary>
            public Color32[] RandomColors;
            /// <summary>粒子单颗大小（Unity unit），null = 沿用 prefab 配置。</summary>
            public float? ParticleSize;
            /// <summary>粒子寿命（秒），null = 沿用 prefab 配置。</summary>
            public float? ParticleLifetime;
            /// <summary>粒子 alpha 渐隐曲线："linear" / "easeOut" / "blink" / "constant"；null = linear。</summary>
            public string FadeCurve;

            /// <summary>limb shader uniform _BloodDark（深血色）。null = 不覆盖。</summary>
            public Color32? BloodDark;
            /// <summary>limb shader uniform _BloodLight（浅血色）。null = 不覆盖。</summary>
            public Color32? BloodLight;

            /// <summary>true = ParticleSystemRenderer 用 Additive blend 制造发光效果。</summary>
            public bool? Glow;
            /// <summary>发光强度（startColor 各分量 ×glowIntensity，越亮越扩光）。null = 1。</summary>
            public float? GlowIntensity;

            /// <summary>true = 粒子贴图按 textureSheetAnimation 多帧播放（贴图须横向切片）。</summary>
            public bool? Animated;
            /// <summary>动效贴图横向帧数；null = 1（单帧）。</summary>
            public int? AnimFrames;
            /// <summary>动效循环每秒帧数；null = 8。</summary>
            public float? AnimFps;
        }

        /// <summary>读取皮肤目录下的 blood.json；缺失或解析失败返回 null。</summary>
        public static Config Load(string skinDir)
        {
            if (string.IsNullOrEmpty(skinDir)) return null;
            string path = Path.Combine(skinDir, "blood.json");
            if (!File.Exists(path)) return null;
            try
            {
                string text = File.ReadAllText(path);
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
                Debug.LogWarning("[SkinSync] blood.json parse failed: " + ex.Message);
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
