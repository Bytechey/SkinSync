using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>解析、校验并缓存部件区块遮罩图（.zones.png）与特效配置（.zones.json）。</summary>
    public static class ZonesConfigLoader
    {
        private const int MaxZonesPerFile = 16;

        private static readonly Dictionary<string, (List<Entry> entries, List<ZoneGeometry> geom)> _loadCache
            = new Dictionary<string, (List<Entry> entries, List<ZoneGeometry> geom)>(StringComparer.OrdinalIgnoreCase);

        public class Entry
        {
            public byte Id;
            public string Name;
            public LightConfig Light;
            public ParticleConfig Particle;
            public AnimationConfig Animation;
        }

        public class LightConfig
        {
            public Color32 Color;
            public float Intensity;
            public float OuterRadius;
            public float InnerRadius;
            public Color32? Color2;
            public string Mode;
            public float Direction;
            public float Spread;
            public float FalloffPower;
            public bool Breathing;
            public float BreathSpeed;
            public float BreathAmplitude;
        }

        public class ParticleConfig
        {
            public float Rate;
            public float Lifetime;
            public float StartSize;
            public Color32 StartColor;
            public float Gravity;
            public string Shape;
            public float ShapeRadius;
            public Color32? EndColor;
            public string Mode;
            public float Speed;
            public float Direction;
            public float Spread;
            public float EndSizeScale;
            public string FadeCurve;
        }

        public class AnimationConfig
        {
            public List<string> Frames;
            public float Fps;
            public bool Loop;
        }

        public class ZoneGeometry
        {
            public byte Id;
            public float CenterX;
            public float CenterY;
            public float Radius;
        }

        /// <summary>失效单个皮肤（skinID 非空时移除路径含该皮肤目录的条目）或全部区块解析缓存。</summary>
        internal static void Invalidate(string skinID = null)
        {
            if (skinID == null)
            {
                _loadCache.Clear();
                return;
            }
            if (skinID.Length == 0 || _loadCache.Count == 0) return;

            string skinDir = SkinPathResolver.GetSkinDir(skinID);
            string normDirPrefix = string.IsNullOrEmpty(skinDir)
                ? null
                : skinDir.Replace('\\', '/').TrimEnd('/') + "/";

            List<string> toRemove = null;
            foreach (string key in _loadCache.Keys)
            {
                if (MatchesSkinCacheKey(key, skinID, normDirPrefix))
                {
                    (toRemove ?? (toRemove = new List<string>())).Add(key);
                }
            }
            if (toRemove != null)
            {
                for (int i = 0; i < toRemove.Count; i++)
                {
                    _loadCache.Remove(toRemove[i]);
                }
            }
        }

        private static bool MatchesSkinCacheKey(string fullKey, string skinID, string normDirPrefix)
        {
            if (string.IsNullOrEmpty(fullKey)) return false;
            int sep = fullKey.IndexOf('|');
            string pathPart = (sep >= 0 ? fullKey.Substring(0, sep) : fullKey).Replace('\\', '/');

            if (!string.IsNullOrEmpty(normDirPrefix)
                && pathPart.StartsWith(normDirPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string[] segs = pathPart.Split('/');
            for (int i = 0; i < segs.Length; i++)
            {
                if (string.Equals(segs[i], skinID, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>从磁盘读取部件区块配置与遮罩几何数据并缓存结果，任一缺失或解析失败返回空元组。</summary>
        public static (List<Entry> entries, List<ZoneGeometry> geom) Load(string maskPath, string configPath, int spriteW, int spriteH)
        {
            if (string.IsNullOrEmpty(maskPath) || string.IsNullOrEmpty(configPath)) return (null, null);
            string cacheKey = maskPath + "|" + configPath;
            string fullKey = BuildFullKey(cacheKey, spriteW, spriteH);
            if (_loadCache.TryGetValue(fullKey, out var cached)) return cached;

            if (!File.Exists(maskPath) || !File.Exists(configPath)) return (null, null);

            string text;
            byte[] maskBytes;
            try
            {
                text = File.ReadAllText(configPath);
                maskBytes = File.ReadAllBytes(maskPath);
            }
            catch (Exception ex)
            {
                ModLog.Warning("zones file read failed: " + ex.Message);
                _loadCache[fullKey] = (null, null);
                return (null, null);
            }

            return LoadFromBytes(cacheKey, maskBytes, text, spriteW, spriteH);
        }

        /// <summary>从内存字节解析并钳制部件区块配置与遮罩几何数据，按 cacheKey 与基准尺寸写入缓存。</summary>
        internal static (List<Entry> entries, List<ZoneGeometry> geom) LoadFromBytes(
            string cacheKey,
            byte[] maskBytes,
            string configJson,
            int spriteW,
            int spriteH)
        {
            string fullKey = !string.IsNullOrEmpty(cacheKey) ? BuildFullKey(cacheKey, spriteW, spriteH) : null;
            if (fullKey != null && _loadCache.TryGetValue(fullKey, out var cached)) return cached;

            if (maskBytes == null || maskBytes.Length == 0 || string.IsNullOrEmpty(configJson))
            {
                return (null, null);
            }

            List<Entry> entries;
            try
            {
                var raw = JsonConvert.DeserializeObject<RawConfig>(configJson);
                entries = ToEntries(raw);
            }
            catch (Exception ex)
            {
                ModLog.Warning("zones.json parse failed: " + ex.Message);
                if (fullKey != null) _loadCache[fullKey] = (null, null);
                return (null, null);
            }

            var geom = ComputeGeometry(maskBytes, cacheKey, spriteW, spriteH);
            var result = geom == null ? (null, null) : (entries, geom);
            if (fullKey != null) _loadCache[fullKey] = result;
            return result;
        }

        private static string BuildFullKey(string cacheKey, int spriteW, int spriteH)
            => $"{cacheKey}|{spriteW}x{spriteH}";

        private static List<ZoneGeometry> ComputeGeometry(byte[] data, string cacheKey, int expectedW, int expectedH)
        {
            if (!SkinPackCodec.IsPngDimensionsSafe(data, cacheKey)) return null;

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            try
            {
                if (!ImageConversion.LoadImage(tex, data)) return null;
                int w = tex.width;
                int h = tex.height;
                if (w <= 0 || h <= 0) return null;
                var pixels = tex.GetPixels32();

                var seen = new bool[256];
                var minX = new int[256];
                var maxX = new int[256];
                var minY = new int[256];
                var maxY = new int[256];
                for (int y = 0; y < h; y++)
                {
                    int row = y * w;
                    for (int x = 0; x < w; x++)
                    {
                        var p = pixels[row + x];
                        if (p.a == 0) continue;
                        byte id = p.r;
                        if (id == 0) continue;
                        if (!seen[id])
                        {
                            seen[id] = true;
                            minX[id] = x;
                            maxX[id] = x;
                            minY[id] = y;
                            maxY[id] = y;
                        }
                        else
                        {
                            if (x < minX[id]) minX[id] = x;
                            if (x > maxX[id]) maxX[id] = x;
                            if (y < minY[id]) minY[id] = y;
                            if (y > maxY[id]) maxY[id] = y;
                        }
                    }
                }

                float scaleX = expectedW > 0 && expectedW != w ? (float)expectedW / w : 1f;
                float scaleY = expectedH > 0 && expectedH != h ? (float)expectedH / h : 1f;
                var geom = new List<ZoneGeometry>();
                for (int id = 1; id < 256; id++)
                {
                    if (!seen[id]) continue;
                    float cx = (minX[id] + maxX[id]) * 0.5f * scaleX;
                    float cy = (minY[id] + maxY[id]) * 0.5f * scaleY;
                    float dx = (maxX[id] - minX[id]) * 0.5f * scaleX;
                    float dy = (maxY[id] - minY[id]) * 0.5f * scaleY;
                    float radius = Mathf.Sqrt(dx * dx + dy * dy);
                    geom.Add(new ZoneGeometry { Id = (byte)id, CenterX = cx, CenterY = cy, Radius = radius });
                }
                return geom;
            }
            finally
            {
                UnityEngine.Object.Destroy(tex);
            }
        }

        private static List<Entry> ToEntries(RawConfig raw)
        {
            var list = new List<Entry>();
            if (raw == null || raw.zones == null) return list;
            foreach (var z in raw.zones)
            {
                if (list.Count >= MaxZonesPerFile) break;
                if (z == null || z.id < 1 || z.id > 255) continue;
                var e = new Entry { Id = (byte)z.id, Name = z.name };
                if (z.light != null)
                {
                    float outerRadius = ClampFinite(z.light.outerRadius, 0.05f, 0.05f, 25f);
                    e.Light = new LightConfig
                    {
                        Color = ParseColor(z.light.color, new Color32(255, 255, 255, 255)),
                        Intensity = ClampFinite(z.light.intensity, 0f, 0f, 10f),
                        OuterRadius = outerRadius,
                        InnerRadius = ClampFinite(z.light.innerRadius, 0f, 0f, outerRadius),
                        Color2 = z.light.color2 != null ? ParseColor(z.light.color2, new Color32(255, 255, 255, 255)) : (Color32?)null,
                        Mode = string.IsNullOrEmpty(z.light.mode) ? "radial" : z.light.mode,
                        Direction = SanitizeAngle(z.light.direction),
                        Spread = ClampPositiveOrDefault(z.light.spread, 60f, 1f, 360f),
                        FalloffPower = ClampPositiveOrDefault(z.light.falloffPower, 2f, 0.1f, 10f),
                        Breathing = z.light.breathing,
                        BreathSpeed = ClampPositiveOrDefault(z.light.breathSpeed, 1f, 0.05f, 20f),
                        BreathAmplitude = ClampNonNegativeOrDefault(z.light.breathAmplitude, 0.5f, 0f, 2f),
                    };
                }
                if (z.particle != null)
                {
                    float rawSpread = z.particle.spread.HasValue && IsFinite(z.particle.spread.Value)
                        ? z.particle.spread.Value
                        : 360f;
                    e.Particle = new ParticleConfig
                    {
                        Rate = ClampFinite(z.particle.rate, 0f, 0f, 80f),
                        Lifetime = ClampPositiveOrDefault(z.particle.lifetime, 1f, 0.05f, 15f),
                        StartSize = ClampPositiveOrDefault(z.particle.startSize, 0.05f, 0.01f, 3f),
                        StartColor = ParseColor(z.particle.startColor, new Color32(255, 255, 255, 255)),
                        Gravity = ClampFinite(z.particle.gravity, 0f, -10f, 10f),
                        Shape = string.IsNullOrEmpty(z.particle.shape) ? "circle" : z.particle.shape,
                        ShapeRadius = ClampFinite(z.particle.shapeRadius, 0.01f, 0.01f, 15f),
                        EndColor = z.particle.endColor != null ? ParseColor(z.particle.endColor, new Color32(255, 255, 255, 0)) : (Color32?)null,
                        Mode = string.IsNullOrEmpty(z.particle.mode) ? "gravity" : z.particle.mode,
                        Speed = ClampFinite(z.particle.speed, 0f, 0f, 30f),
                        Direction = SanitizeAngle(z.particle.direction),
                        Spread = Mathf.Clamp(rawSpread, 0f, 360f),
                        EndSizeScale = ClampPositiveOrDefault(z.particle.endSizeScale, 1f, 0.05f, 5f),
                        FadeCurve = string.IsNullOrEmpty(z.particle.fadeCurve) ? "linear" : z.particle.fadeCurve,
                    };
                }
                if (z.animation != null && z.animation.frames != null && z.animation.frames.Count > 0)
                {
                    e.Animation = new AnimationConfig
                    {
                        Frames = z.animation.frames,
                        Fps = ClampPositiveOrDefault(z.animation.fps, 6f, 0.5f, 60f),
                        Loop = z.animation.loop,
                    };
                }
                list.Add(e);
            }
            return list;
        }

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        private static float ClampFinite(float v, float fallback, float min, float max)
            => Mathf.Clamp(IsFinite(v) ? v : fallback, min, max);

        private static float ClampPositiveOrDefault(float v, float defaultVal, float min, float max)
            => Mathf.Clamp(IsFinite(v) && v > 0f ? v : defaultVal, min, max);

        private static float ClampNonNegativeOrDefault(float v, float defaultVal, float min, float max)
            => Mathf.Clamp(IsFinite(v) && v >= 0f ? v : defaultVal, min, max);

        private static float SanitizeAngle(float deg)
        {
            if (!IsFinite(deg)) return 0f;
            return deg > 360f || deg < -360f ? deg % 360f : deg;
        }

        private static Color32 ParseColor(int[] arr, Color32 fb)
        {
            if (arr == null || arr.Length < 3) return fb;
            byte r = (byte)Mathf.Clamp(arr[0], 0, 255);
            byte g = (byte)Mathf.Clamp(arr[1], 0, 255);
            byte b = (byte)Mathf.Clamp(arr[2], 0, 255);
            byte a = arr.Length >= 4 ? (byte)Mathf.Clamp(arr[3], 0, 255) : (byte)255;
            return new Color32(r, g, b, a);
        }

        private class RawConfig
        {
#pragma warning disable CS0649
            public List<RawZone> zones;
#pragma warning restore CS0649
        }

        private class RawZone
        {
#pragma warning disable CS0649
            public int id;
            public string name;
            public RawLight light;
            public RawParticle particle;
            public RawAnimation animation;
#pragma warning restore CS0649
        }

        private class RawLight
        {
#pragma warning disable CS0649
            public int[] color;
            public float intensity;
            public float outerRadius;
            public float innerRadius;
            public int[] color2;
            public string mode;
            public float direction;
            public float spread;
            public float falloffPower;
            public bool breathing;
            public float breathSpeed;
            public float breathAmplitude;
#pragma warning restore CS0649
        }

        private class RawParticle
        {
#pragma warning disable CS0649
            public float rate;
            public float lifetime;
            public float startSize;
            public int[] startColor;
            public float gravity;
            public string shape;
            public float shapeRadius;
            public int[] endColor;
            public string mode;
            public float speed;
            public float direction;
            public float? spread;
            public float endSizeScale;
            public string fadeCurve;
#pragma warning restore CS0649
        }

        private class RawAnimation
        {
#pragma warning disable CS0649
            public List<string> frames;
            public float fps;
            public bool loop;
#pragma warning restore CS0649
        }
    }
}
