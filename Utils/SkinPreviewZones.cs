using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>
    /// 预览端 zones 渲染：把 part 的 zones.png + zones.json 解析成几何 + 配置，
    /// 在合成画布上叠加光 / 粒子 / 动画第一帧。算法移植自 src-tauri/src/apiLayer/mod.rs 的
    /// renderZoneLightsForPart / renderZoneParticles / renderZoneAnimation。
    /// 预览静态用 timeSec=0，避免 IMGUI 每帧重渲染。
    /// </summary>
    internal static partial class SkinPreviewZones
    {
        /// <summary>把 part 上所有区块的 light + particle + animation 叠到 canvas。</summary>
        internal static void Render(byte[] canvas, int canvasW, int canvasH,
            string skinDir, string categoryDir, string partName,
            int partW, int partH, int centerX, int centerY, float rotationDeg, bool flipX,
            float timeSec)
        {
            string maskPath = Path.Combine(skinDir, categoryDir, partName + ".zones.png");
            string configPath = Path.Combine(skinDir, categoryDir, partName + ".zones.json");
            if (!File.Exists(maskPath) || !File.Exists(configPath)) return;

            var (entries, geom) = ZonesConfigLoader.Load(maskPath, configPath, partW, partH);
            if (entries == null || geom == null || geom.Count == 0) return;

            var geomById = new Dictionary<byte, ZonesConfigLoader.ZoneGeometry>();
            foreach (var g in geom) geomById[g.Id] = g;

            float rad = -rotationDeg * Mathf.Deg2Rad;
            float cosT = Mathf.Cos(rad);
            float sinT = Mathf.Sin(rad);
            float halfW = partW * 0.5f;
            float halfH = partH * 0.5f;

            // 区块世界坐标计算（mask 像素 → part 局部 → 旋转 → 加 part 世界中心）
            // 注意：ZonesConfigLoader.ComputeGeometry 用 Texture2D.GetPixels32（左下原点）扫像素，
            // 而本预览画布用左上原点；mask 几何 y 需翻转一次匹配画布坐标。
            foreach (var e in entries)
            {
                if (!geomById.TryGetValue(e.Id, out var g)) continue;
                float cx = g.CenterX;
                float cy = partH - g.CenterY;
                float lx = cx - halfW;
                float ly = cy - halfH;
                float fx = flipX ? -lx : lx;
                float wx = centerX + fx * cosT - ly * sinT;
                float wy = centerY + fx * sinT + ly * cosT;

                if (e.Light != null)
                {
                    BlendLight(canvas, canvasW, canvasH, wx, wy, e.Light, rotationDeg, flipX, timeSec);
                }
                if (e.Particle != null)
                {
                    // seed 含 partName + zoneId，让不同区块粒子分布独立。
                    uint seed = e.Id;
                    foreach (char ch in partName) seed = seed * 31 + ch;
                    BlendParticles(canvas, canvasW, canvasH, wx, wy, e.Particle, timeSec, seed);
                }
                if (e.Animation != null)
                {
                    BlendAnimation(canvas, canvasW, canvasH, wx, wy, e.Animation, skinDir, timeSec);
                }
            }
        }

        // —— Animation：按 timeSec + fps 选当前帧（预览 timeSec=0 即 frame 0），从 4 类目录任找 PNG —— //
        private static void BlendAnimation(byte[] canvas, int canvasW, int canvasH,
            float centerX, float centerY, ZonesConfigLoader.AnimationConfig cfg,
            string skinDir, float timeSec)
        {
            if (cfg.Frames == null || cfg.Frames.Count == 0 || cfg.Fps <= 0f) return;
            float total = cfg.Frames.Count;
            float phase = cfg.Loop
                ? Mod(timeSec * cfg.Fps, total)
                : Mathf.Clamp(timeSec * cfg.Fps, 0f, total - 1f);
            int idx = Mathf.FloorToInt(phase);
            if (idx < 0 || idx >= cfg.Frames.Count) return;
            string frameName = cfg.Frames[idx];
            if (string.IsNullOrEmpty(frameName)) return;

            // 在 4 类目录下找 frameName.png。
            string[] cats = { "Body", "Head", "Wings", "Accessories" };
            SkinPreviewRenderer.Pixels pix = default;
            foreach (var cat in cats)
            {
                pix = SkinPreviewRenderer.LoadPng(Path.Combine(skinDir, cat, frameName + ".png"));
                if (pix.W != 0) break;
            }
            if (pix.W == 0) return;
            SkinPreviewRenderer.BlitWithRotation(canvas, canvasW, canvasH,
                pix.Rgba, pix.W, pix.H, Mathf.RoundToInt(centerX), Mathf.RoundToInt(centerY), 0f, false);
        }

        // —— Light：径向加光 + directional/scatter cone + 呼吸 + 双色渐变 —— //
        private static void BlendLight(byte[] canvas, int canvasW, int canvasH,
            float centerX, float centerY, ZonesConfigLoader.LightConfig cfg,
            float rotationDeg, bool flipX, float timeSec)
        {
            const float PixelsPerUnit = 8f;
            float outer = cfg.OuterRadius * PixelsPerUnit;
            float inner = Mathf.Clamp(cfg.InnerRadius * PixelsPerUnit, 0f, outer);
            if (outer <= 0f || cfg.Intensity <= 0f) return;

            // 呼吸：强度按时间正弦在 [intensity*(1-amp), intensity*(1+amp)] 摆动。
            float intensity = cfg.Breathing
                ? Mathf.Max(0f, cfg.Intensity * (1f + Mathf.Sin(timeSec * cfg.BreathSpeed * Mathf.PI * 2f) * cfg.BreathAmplitude))
                : cfg.Intensity;

            // 方向角：light.direction 度，按 part 旋转 + flip 同步。
            float dirRad = -cfg.Direction * Mathf.Deg2Rad;
            float baseDx = Mathf.Cos(dirRad);
            float baseDy = -Mathf.Sin(dirRad);
            if (flipX) baseDx = -baseDx;
            float partRad = -rotationDeg * Mathf.Deg2Rad;
            float pCos = Mathf.Cos(partRad);
            float pSin = Mathf.Sin(partRad);
            float dirX = baseDx * pCos - baseDy * pSin;
            float dirY = baseDx * pSin + baseDy * pCos;

            float halfCos = Mathf.Cos(Mathf.Clamp(cfg.Spread, 0f, 360f) * 0.5f * Mathf.Deg2Rad);
            bool isDir = cfg.Mode == "directional" || cfg.Mode == "scatter";
            float pow = Mathf.Max(0.1f, cfg.FalloffPower);

            int x0 = Mathf.Max(0, Mathf.FloorToInt(centerX - outer));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(centerY - outer));
            int x1 = Mathf.Min(canvasW - 1, Mathf.CeilToInt(centerX + outer));
            int y1 = Mathf.Min(canvasH - 1, Mathf.CeilToInt(centerY + outer));

            float r1 = cfg.Color.r, g1 = cfg.Color.g, b1 = cfg.Color.b;
            float r2 = r1, g2 = g1, b2 = b1;
            if (cfg.Color2.HasValue) { r2 = cfg.Color2.Value.r; g2 = cfg.Color2.Value.g; b2 = cfg.Color2.Value.b; }
            float alphaScale = cfg.Color.a / 255f;
            float outerSq = outer * outer;

            for (int y = y0; y <= y1; y++)
            {
                float dy = y - centerY;
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x - centerX;
                    float d2 = dx * dx + dy * dy;
                    if (d2 > outerSq) continue;
                    float d = Mathf.Sqrt(d2);
                    float baseT = d <= inner ? 1f : 1f - (d - inner) / Mathf.Max(0.001f, outer - inner);
                    float t = Mathf.Pow(baseT, pow);
                    float mix = d <= inner ? 0f : Mathf.Clamp01((d - inner) / Mathf.Max(0.001f, outer - inner));
                    if (isDir && d > 0.001f)
                    {
                        float nx = dx / d, ny = dy / d;
                        float dot = nx * dirX + ny * dirY;
                        if (dot < halfCos) continue;
                        if (cfg.Mode == "directional")
                        {
                            float cone = Mathf.Clamp01((dot - halfCos) / Mathf.Max(0.001f, 1f - halfCos));
                            t *= cone;
                        }
                        else
                        {
                            t *= 0.7f + 0.3f * Mathf.Clamp01((dot - halfCos) / Mathf.Max(0.001f, 1f - halfCos));
                        }
                    }
                    float a = Mathf.Clamp01(intensity * t * alphaScale);
                    if (a <= 0f) continue;
                    float cr = r1 * (1f - mix) + r2 * mix;
                    float cg = g1 * (1f - mix) + g2 * mix;
                    float cb = b1 * (1f - mix) + b2 * mix;
                    int idx = (y * canvasW + x) * 4;
                    canvas[idx]     = (byte)Mathf.Min(255f, canvas[idx]     + cr * a);
                    canvas[idx + 1] = (byte)Mathf.Min(255f, canvas[idx + 1] + cg * a);
                    canvas[idx + 2] = (byte)Mathf.Min(255f, canvas[idx + 2] + cb * a);
                    canvas[idx + 3] = (byte)Mathf.Min(255f, canvas[idx + 3] + 255f * a);
                }
            }
        }
    }
}

namespace SkinSyncMod
{
    internal static partial class SkinPreviewZones
    {
        // —— Particles：gravity / star / fire / water / orbit 5 模式（移植 Rust renderZoneParticles） —— //

        internal static void BlendParticles(byte[] canvas, int canvasW, int canvasH,
            float centerX, float centerY, ZonesConfigLoader.ParticleConfig cfg, float timeSec, uint seed)
        {
            if (cfg.Rate <= 0f || cfg.Lifetime <= 0f) return;
            int active = Mathf.Clamp(Mathf.CeilToInt(cfg.Rate * cfg.Lifetime), 1, 512);
            float sizePx = Mathf.Max(0.5f, cfg.StartSize * 8f);
            float shapeRadiusPx = cfg.ShapeRadius > 0f ? cfg.ShapeRadius * 8f : 4f;
            string mode = cfg.Mode ?? "gravity";
            float speedPxPerSec = cfg.Speed > 0f ? cfg.Speed * 8f : ModeDefaultSpeedPx(mode);
            float gravityPxPerSec2 = cfg.Gravity * 80f;
            float dirCenterRad = -cfg.Direction * Mathf.Deg2Rad;
            float halfSpread = Mathf.Clamp(cfg.Spread, 0f, 360f) * 0.5f * Mathf.Deg2Rad;
            Color32 endC = cfg.EndColor ?? new Color32(cfg.StartColor.r, cfg.StartColor.g, cfg.StartColor.b, 0);
            float endScale = Mathf.Max(0.01f, cfg.EndSizeScale);

            for (int i = 0; i < active; i++)
            {
                uint h = HashU32((uint)(seed + (uint)i));
                float phase = (h & 0xFFFF) / 65536f;
                float birth = phase * cfg.Lifetime + i / Mathf.Max(0.0001f, cfg.Rate);
                float age = Mod(timeSec - birth, cfg.Lifetime);
                if (age < 0f || age > cfg.Lifetime) continue;
                float lifeT = Mathf.Clamp01(age / cfg.Lifetime);

                float baseAlpha;
                switch (cfg.FadeCurve)
                {
                    case "constant": baseAlpha = 1f; break;
                    case "easeOut": baseAlpha = Mathf.Pow(1f - lifeT, 0.5f); break;
                    case "blink": baseAlpha = (1f - lifeT) * ((Mathf.Sin(lifeT * 6f * Mathf.PI) + 1f) * 0.5f); break;
                    default: baseAlpha = 1f - lifeT; break;
                }
                float alpha = Mathf.Clamp01(baseAlpha) * (cfg.StartColor.a / 255f);
                if (alpha <= 0.005f) continue;

                float spreadFrac = ((h >> 16) & 0xFFFF) / 65536f * 2f - 1f;
                float angle = dirCenterRad + spreadFrac * halfSpread;
                float r0 = ((h ^ 0xA5A5A5A5u) & 0xFFFF) / 65536f * shapeRadiusPx;

                float px, py;
                switch (mode)
                {
                    case "star":
                        px = centerX + Mathf.Cos(angle) * r0;
                        py = centerY + Mathf.Sin(angle) * r0;
                        break;
                    case "fire":
                    {
                        float sway = Mathf.Sin(age * 4f + (h & 0xFF) * 0.3f) * 2f;
                        float dy = -speedPxPerSec * age - 0.5f * gravityPxPerSec2 * age * age;
                        px = centerX + Mathf.Cos(angle) * r0 + sway;
                        py = centerY + Mathf.Sin(angle) * r0 + dy;
                        break;
                    }
                    case "water":
                    {
                        float sway = Mathf.Sin(age * 3f + (h & 0xFF) * 0.2f) * 1.5f;
                        float dx = Mathf.Cos(angle) * speedPxPerSec * age;
                        float dy = Mathf.Sin(angle) * speedPxPerSec * age + 0.5f * gravityPxPerSec2 * age * age;
                        px = centerX + r0 + dx + sway;
                        py = centerY + dy;
                        break;
                    }
                    case "orbit":
                    {
                        float omega = cfg.Speed > 0f ? cfg.Speed : 2f;
                        float theta = angle + omega * age;
                        float r = r0 + speedPxPerSec * age * 0.05f;
                        px = centerX + Mathf.Cos(theta) * r;
                        py = centerY + Mathf.Sin(theta) * r;
                        break;
                    }
                    default:
                    {
                        float vx = Mathf.Cos(angle) * speedPxPerSec;
                        float vy = -Mathf.Sin(angle) * speedPxPerSec;
                        px = centerX + r0 * Mathf.Cos(angle) + vx * age;
                        py = centerY + r0 * (-Mathf.Sin(angle)) + vy * age + 0.5f * gravityPxPerSec2 * age * age;
                        break;
                    }
                }

                float pr = cfg.StartColor.r * (1f - lifeT) + endC.r * lifeT;
                float pg = cfg.StartColor.g * (1f - lifeT) + endC.g * lifeT;
                float pb = cfg.StartColor.b * (1f - lifeT) + endC.b * lifeT;
                float curSize = sizePx * (1f + (endScale - 1f) * lifeT);
                float halfSize = curSize * 0.5f;

                int x0 = Mathf.Max(0, Mathf.FloorToInt(px - halfSize));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(py - halfSize));
                int x1 = Mathf.Min(canvasW - 1, Mathf.CeilToInt(px + halfSize));
                int y1 = Mathf.Min(canvasH - 1, Mathf.CeilToInt(py + halfSize));
                if (x1 < x0 || y1 < y0) continue;
                float inv = 1f - alpha;
                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        int idx = (y * canvasW + x) * 4;
                        canvas[idx]     = (byte)(canvas[idx]     * inv + pr * alpha);
                        canvas[idx + 1] = (byte)(canvas[idx + 1] * inv + pg * alpha);
                        canvas[idx + 2] = (byte)(canvas[idx + 2] * inv + pb * alpha);
                        canvas[idx + 3] = (byte)Mathf.Max(canvas[idx + 3], alpha * 255f);
                    }
                }
            }
        }

        private static float ModeDefaultSpeedPx(string mode)
        {
            switch (mode)
            {
                case "fire": return 28f;
                case "water": return 36f;
                case "orbit": return 18f;
                case "star": return 0f;
                default: return 12f;
            }
        }

        private static uint HashU32(uint x)
        {
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            return x + 0x9e3779b9u;
        }

        private static float Mod(float a, float b)
        {
            float r = a - b * Mathf.Floor(a / b);
            return r;
        }
    }
}
