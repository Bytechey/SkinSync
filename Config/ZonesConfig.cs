using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>
    /// 解析部件区块（mask + 配置）：与编辑器 zones.rs / web 端 ZoneEntry schema 1:1 对齐。
    /// mask PNG：RGBA8，R 通道存区块 ID（1~255），A=0 视为无区块。
    /// </summary>
    public static class ZonesConfigLoader
    {
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

        /// <summary>区块的几何数据：mask 包围盒中心（PNG 像素坐标）+ 半径（像素）。</summary>
        public class ZoneGeometry
        {
            public byte Id;
            public float CenterX;
            public float CenterY;
            public float Radius;
        }

        /// <summary>读取 zones.json + zones.png；任一缺失返回 null。</summary>
        public static (List<Entry> entries, List<ZoneGeometry> geom) Load(string maskPath, string configPath, int spriteW, int spriteH)
        {
            if (!File.Exists(maskPath) || !File.Exists(configPath)) return (null, null);
            List<Entry> entries;
            try
            {
                string text = File.ReadAllText(configPath);
                var raw = JsonConvert.DeserializeObject<RawConfig>(text);
                entries = ToEntries(raw);
            }
            catch (System.Exception ex)
            {
                SkinSyncMod.ModLog.Warning("zones.json parse failed: " + ex.Message);
                return (null, null);
            }
            var geom = ComputeGeometry(maskPath, spriteW, spriteH);
            if (geom == null) return (null, null);
            return (entries, geom);
        }

        /// <summary>解码 mask PNG，扫每个 ID 的像素得到包围盒中心 + 包围盒对角线一半作为半径。</summary>
        private static List<ZoneGeometry> ComputeGeometry(string maskPath, int expectedW, int expectedH)
        {
            byte[] data;
            try { data = File.ReadAllBytes(maskPath); }
            catch { return null; }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(tex, data)) { Object.Destroy(tex); return null; }
            int w = tex.width;
            int h = tex.height;
            var pixels = tex.GetPixels32();
            Object.Destroy(tex);

            var minX = new Dictionary<byte, int>();
            var maxX = new Dictionary<byte, int>();
            var minY = new Dictionary<byte, int>();
            var maxY = new Dictionary<byte, int>();
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var p = pixels[y * w + x];
                    if (p.a == 0) continue;
                    byte id = p.r;
                    if (id == 0) continue;
                    if (!minX.ContainsKey(id)) { minX[id] = x; maxX[id] = x; minY[id] = y; maxY[id] = y; }
                    else
                    {
                        if (x < minX[id]) minX[id] = x;
                        if (x > maxX[id]) maxX[id] = x;
                        if (y < minY[id]) minY[id] = y;
                        if (y > maxY[id]) maxY[id] = y;
                    }
                }
            }

            var geom = new List<ZoneGeometry>();
            foreach (var kv in minX)
            {
                byte id = kv.Key;
                float cx = (kv.Value + maxX[id]) * 0.5f;
                float cy = (minY[id] + maxY[id]) * 0.5f;
                float dx = (maxX[id] - kv.Value) * 0.5f;
                float dy = (maxY[id] - minY[id]) * 0.5f;
                float radius = Mathf.Sqrt(dx * dx + dy * dy);
                geom.Add(new ZoneGeometry { Id = id, CenterX = cx, CenterY = cy, Radius = radius });
            }
            return geom;
        }

        private static List<Entry> ToEntries(RawConfig raw)
        {
            var list = new List<Entry>();
            if (raw == null || raw.zones == null) return list;
            foreach (var z in raw.zones)
            {
                if (z == null || z.id == 0) continue;
                var e = new Entry { Id = (byte)z.id, Name = z.name };
                if (z.light != null)
                {
                    e.Light = new LightConfig
                    {
                        Color = ParseColor(z.light.color, new Color32(255, 255, 255, 255)),
                        Intensity = z.light.intensity,
                        OuterRadius = z.light.outerRadius,
                        InnerRadius = z.light.innerRadius,
                        Color2 = z.light.color2 != null ? ParseColor(z.light.color2, new Color32(255, 255, 255, 255)) : (Color32?)null,
                        Mode = string.IsNullOrEmpty(z.light.mode) ? "radial" : z.light.mode,
                        Direction = z.light.direction,
                        Spread = z.light.spread > 0f ? z.light.spread : 60f,
                        FalloffPower = z.light.falloffPower > 0f ? z.light.falloffPower : 2f,
                        Breathing = z.light.breathing,
                        BreathSpeed = z.light.breathSpeed > 0f ? z.light.breathSpeed : 1f,
                        BreathAmplitude = z.light.breathAmplitude >= 0f ? z.light.breathAmplitude : 0.5f,
                    };
                }
                if (z.particle != null)
                {
                    e.Particle = new ParticleConfig
                    {
                        Rate = z.particle.rate,
                        Lifetime = z.particle.lifetime,
                        StartSize = z.particle.startSize,
                        StartColor = ParseColor(z.particle.startColor, new Color32(255, 255, 255, 255)),
                        Gravity = z.particle.gravity,
                        Shape = string.IsNullOrEmpty(z.particle.shape) ? "circle" : z.particle.shape,
                        ShapeRadius = z.particle.shapeRadius,
                        EndColor = z.particle.endColor != null ? ParseColor(z.particle.endColor, new Color32(255, 255, 255, 0)) : (Color32?)null,
                        Mode = string.IsNullOrEmpty(z.particle.mode) ? "gravity" : z.particle.mode,
                        Speed = z.particle.speed,
                        Direction = z.particle.direction,
                        Spread = z.particle.spread > 0f ? z.particle.spread : 360f,
                        EndSizeScale = z.particle.endSizeScale > 0f ? z.particle.endSizeScale : 1f,
                        FadeCurve = string.IsNullOrEmpty(z.particle.fadeCurve) ? "linear" : z.particle.fadeCurve,
                    };
                }
                if (z.animation != null && z.animation.frames != null && z.animation.frames.Count > 0)
                {
                    e.Animation = new AnimationConfig
                    {
                        Frames = z.animation.frames,
                        Fps = z.animation.fps > 0f ? z.animation.fps : 6f,
                        Loop = z.animation.loop,
                    };
                }
                list.Add(e);
            }
            return list;
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
            public float spread;
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
