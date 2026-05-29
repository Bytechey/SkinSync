using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace SkinSyncMod
{
    /// <summary>
    /// 按部件 zones.png + zones.json 在每个 limb 的 sprite 上挂区块效果（Light2D / ParticleSystem / 帧动画）。
    /// 区块几何用 mask 包围盒中心 + 包围盒对角线一半作为半径——简化方案，避免 mesh 转换。
    /// </summary>
    public static class ZonesAttacher
    {
        private const string ContainerPrefix = "HwZones_";
        private const float PIXELS_PER_UNIT = 8f;

        /// <summary>清掉旧挂载，按 sprite 名扫 zones 文件并挂载。</summary>
        public static void Apply(GameObject playerObj, string characterName)
        {
            if (playerObj == null) return;
            Body body = playerObj.GetComponentInChildren<Body>(true);
            if (body == null) return;

            ClearExisting(body);

            string skinDir = SkinPathResolver.GetSkinDir(characterName);
            if (!Directory.Exists(skinDir)) return;

            foreach (var limb in body.GetComponentsInChildren<Limb>(true))
            {
                var sr = limb.GetComponent<SpriteRenderer>();
                if (sr == null || sr.sprite == null) continue;
                AttachForRenderer(limb.transform, sr, skinDir);
            }
        }

        private static void AttachForRenderer(Transform host, SpriteRenderer sr, string skinDir)
        {
            string spriteName = sr.sprite.name;
            string maskPath = FindZonesFile(skinDir, spriteName, ".zones.png");
            string configPath = FindZonesFile(skinDir, spriteName, ".zones.json");
            if (maskPath == null || configPath == null) return;

            int spriteW = (int)sr.sprite.rect.width;
            int spriteH = (int)sr.sprite.rect.height;
            var (entries, geom) = ZonesConfigLoader.Load(maskPath, configPath, spriteW, spriteH);
            if (entries == null || geom == null) return;

            var geomById = new Dictionary<byte, ZonesConfigLoader.ZoneGeometry>();
            foreach (var g in geom) geomById[g.Id] = g;

            foreach (var entry in entries)
            {
                if (!geomById.TryGetValue(entry.Id, out var g)) continue;
                AttachZone(host, sr, entry, g, spriteW, spriteH);
            }
        }

        /// <summary>在皮肤目录递归找 `<sprite><suffix>` 文件；用于 Body / Head / Wings / Accessories 任意子目录。</summary>
        private static string FindZonesFile(string skinDir, string spriteName, string suffix)
        {
            string fileName = spriteName + suffix;
            try
            {
                foreach (var f in Directory.GetFiles(skinDir, fileName, SearchOption.AllDirectories))
                {
                    return f;
                }
            }
            catch { }
            return null;
        }

        private static void AttachZone(Transform host, SpriteRenderer hostSr, ZonesConfigLoader.Entry entry,
            ZonesConfigLoader.ZoneGeometry geom, int spriteW, int spriteH)
        {
            var go = new GameObject(ContainerPrefix + entry.Id);
            go.transform.SetParent(host, worldPositionStays: false);
            // mask 像素坐标系：原点在左下；sprite pivot 中心。把 (cx, cy) 转 limb 局部空间。
            float lx = (geom.CenterX - spriteW * 0.5f) / PIXELS_PER_UNIT;
            float ly = (geom.CenterY - spriteH * 0.5f) / PIXELS_PER_UNIT;
            go.transform.localPosition = new Vector3(lx, ly, 0f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            float radiusUnits = geom.Radius / PIXELS_PER_UNIT;

            if (entry.Light != null) AttachLight(go, entry.Light);
            if (entry.Particle != null) AttachParticle(go, entry.Particle, hostSr, radiusUnits);
            if (entry.Animation != null) AttachAnimator(go, entry.Animation, hostSr);
        }

        private static void AttachLight(GameObject host, ZonesConfigLoader.LightConfig cfg)
        {
            var light = host.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Point;
            light.color = cfg.Color;
            light.intensity = cfg.Intensity;
            light.pointLightOuterRadius = cfg.OuterRadius;
            light.pointLightInnerRadius = cfg.InnerRadius;
            // directional / scatter 模式：把 inner 拉小近似聚光（Light2D Point 没有 cone 概念，只能近似）。
            if (cfg.Mode == "directional" || cfg.Mode == "scatter")
            {
                light.pointLightInnerRadius = cfg.InnerRadius * 0.3f;
            }
            if (cfg.Breathing || cfg.Color2.HasValue)
            {
                var anim = host.AddComponent<ZoneLightAnimator>();
                anim.Light = light;
                anim.BaseColor = cfg.Color;
                anim.SecondColor = cfg.Color2 ?? cfg.Color;
                anim.UseColorGradient = cfg.Color2.HasValue;
                anim.Breathing = cfg.Breathing;
                anim.BaseIntensity = cfg.Intensity;
                anim.BreathSpeed = cfg.BreathSpeed;
                anim.BreathAmplitude = cfg.BreathAmplitude;
            }
        }

        private static void AttachParticle(GameObject host, ZonesConfigLoader.ParticleConfig cfg,
            SpriteRenderer hostSr, float fallbackRadius)
        {
            var ps = host.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.duration = 5f;
            main.loop = true;
            main.startLifetime = cfg.Lifetime > 0f ? cfg.Lifetime : 1f;
            main.startSize = cfg.StartSize > 0f ? cfg.StartSize : 0.05f;
            main.startColor = new ParticleSystem.MinMaxGradient((Color)cfg.StartColor);
            main.gravityModifier = cfg.Gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            float baseSpeed = cfg.Speed > 0f ? cfg.Speed : ModeDefaultSpeed(cfg.Mode);
            main.startSpeed = baseSpeed;

            var emission = ps.emission;
            emission.rateOverTime = cfg.Rate > 0f ? cfg.Rate : 5f;

            var shape = ps.shape;
            shape.shapeType = string.Equals(cfg.Shape, "sphere", System.StringComparison.OrdinalIgnoreCase)
                ? ParticleSystemShapeType.Sphere
                : ParticleSystemShapeType.Circle;
            shape.radius = cfg.ShapeRadius > 0f ? cfg.ShapeRadius : Mathf.Max(0.05f, fallbackRadius);
            // 发射方向：Direction 度（0=右，90=上）+ Spread 散射角。
            float dirRad = cfg.Direction * Mathf.Deg2Rad;
            // shape.rotation = Vector3.zero（默认）；用 startSpeed + velocity over lifetime 不够，
            // 直接用 cone 形把发射限制到 direction±halfSpread。
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = Mathf.Clamp(cfg.Spread * 0.5f, 0f, 90f);
            shape.rotation = new Vector3(0f, 0f, cfg.Direction - 90f);
            shape.radius = Mathf.Max(0.01f, cfg.ShapeRadius > 0f ? cfg.ShapeRadius : fallbackRadius * 0.5f);

            // 大小渐变：startSize → startSize * EndSizeScale。
            if (cfg.EndSizeScale > 0f && Mathf.Abs(cfg.EndSizeScale - 1f) > 0.01f)
            {
                var sol = ps.sizeOverLifetime;
                sol.enabled = true;
                var curve = new AnimationCurve();
                curve.AddKey(0f, 1f);
                curve.AddKey(1f, cfg.EndSizeScale);
                sol.size = new ParticleSystem.MinMaxCurve(1f, curve);
            }

            // 颜色 / alpha 渐变。
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            Color32 startC = cfg.StartColor;
            Color32 endC = cfg.EndColor ?? new Color32(startC.r, startC.g, startC.b, 0);
            grad.SetKeys(
                new[] { new GradientColorKey((Color)startC, 0f), new GradientColorKey((Color)endC, 1f) },
                BuildAlphaKeys(cfg.FadeCurve, startC.a, endC.a));
            col.color = new ParticleSystem.MinMaxGradient(grad);

            // 模式专项：fire 向上加速（负重力）+ 起始速度向上；water 向下；orbit 用 velocityOverLifetime 旋转。
            switch (cfg.Mode)
            {
                case "fire":
                    main.gravityModifier = -0.4f;
                    if (cfg.Direction == 0f) shape.rotation = new Vector3(0f, 0f, 0f);
                    break;
                case "water":
                    main.gravityModifier = cfg.Gravity > 0f ? cfg.Gravity : 1.2f;
                    if (cfg.Direction == 0f) shape.rotation = new Vector3(0f, 0f, 180f);
                    break;
                case "orbit":
                    var vol = ps.velocityOverLifetime;
                    vol.enabled = true;
                    vol.orbitalZ = new ParticleSystem.MinMaxCurve(baseSpeed);
                    main.startSpeed = 0f;
                    break;
                case "star":
                    main.startSpeed = 0f;
                    break;
            }

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null && hostSr != null)
            {
                renderer.sortingLayerID = hostSr.sortingLayerID;
                renderer.sortingOrder = hostSr.sortingOrder + 1;
            }
        }

        private static float ModeDefaultSpeed(string mode)
        {
            switch (mode)
            {
                case "fire": return 3.5f;
                case "water": return 4.5f;
                case "orbit": return 90f;
                case "star": return 0f;
                default: return 1.5f;
            }
        }

        private static GradientAlphaKey[] BuildAlphaKeys(string curve, byte startA, byte endA)
        {
            float a0 = startA / 255f;
            float a1 = endA / 255f;
            switch (curve)
            {
                case "constant":
                    return new[] { new GradientAlphaKey(a0, 0f), new GradientAlphaKey(a0, 1f) };
                case "easeOut":
                    return new[]
                    {
                        new GradientAlphaKey(a0, 0f),
                        new GradientAlphaKey(a0 * 0.7f, 0.6f),
                        new GradientAlphaKey(a1, 1f),
                    };
                case "blink":
                    return new[]
                    {
                        new GradientAlphaKey(a0, 0f),
                        new GradientAlphaKey(0f, 0.3f),
                        new GradientAlphaKey(a0, 0.6f),
                        new GradientAlphaKey(a1, 1f),
                    };
                default:
                    return new[] { new GradientAlphaKey(a0, 0f), new GradientAlphaKey(a1, 1f) };
            }
        }

        private static void AttachAnimator(GameObject host, ZonesConfigLoader.AnimationConfig cfg, SpriteRenderer hostSr)
        {
            // 帧动画在区块上需要一个 SpriteRenderer 作目标——临时叠加一层覆盖区块位置。
            // 当前实现复用区块容器自身：加 SpriteRenderer，按 fps 切 frames（与原 AccessoryAnimator 同语义）。
            var sr = host.AddComponent<SpriteRenderer>();
            if (hostSr != null)
            {
                sr.sortingLayerID = hostSr.sortingLayerID;
                sr.sortingOrder = hostSr.sortingOrder + 2;
                sr.sharedMaterial = hostSr.sharedMaterial;
            }
            var anim = host.AddComponent<ZoneFrameAnimator>();
            anim.FrameNames = cfg.Frames.ToArray();
            anim.Fps = cfg.Fps;
            anim.Loop = cfg.Loop;
        }

        private static void ClearExisting(Body body)
        {
            foreach (var tf in body.GetComponentsInChildren<Transform>(true))
            {
                if (tf == null || tf.gameObject == null) continue;
                if (tf.name.StartsWith(ContainerPrefix))
                    Object.Destroy(tf.gameObject);
            }
        }
    }

    /// <summary>逐帧切换 SpriteRenderer.sprite；frame 名按 SkinApplier spriteDict 解析，缺失帧跳过。</summary>
    public class ZoneFrameAnimator : MonoBehaviour
    {
        public string[] FrameNames;
        public float Fps;
        public bool Loop;

        private SpriteRenderer _sr;
        private Sprite[] _frames;
        private float _accum;
        private int _index;

        private void Start()
        {
            _sr = GetComponent<SpriteRenderer>();
            ResolveFrames();
            if (_frames != null && _frames.Length > 0 && _sr != null)
                _sr.sprite = _frames[0];
        }

        private void ResolveFrames()
        {
            if (FrameNames == null) return;
            var dict = SkinApplier.GetSpriteDict();
            if (dict == null) return;
            var list = new List<Sprite>();
            foreach (var n in FrameNames)
            {
                if (dict.TryGetValue(n, out var sp) && sp != null) list.Add(sp);
            }
            _frames = list.ToArray();
        }

        private void Update()
        {
            if (_sr == null || _frames == null || _frames.Length == 0 || Fps <= 0f) return;
            _accum += Time.deltaTime;
            float frameLen = 1f / Fps;
            while (_accum >= frameLen)
            {
                _accum -= frameLen;
                _index++;
                if (_index >= _frames.Length)
                {
                    if (Loop) _index = 0;
                    else { _index = _frames.Length - 1; enabled = false; break; }
                }
                _sr.sprite = _frames[_index];
            }
        }
    }

    /// <summary>呼吸光强度 + 渐变发光（base ↔ second 颜色按时间脉冲）。无渐变 + 无呼吸时不挂。</summary>
    public class ZoneLightAnimator : MonoBehaviour
    {
        public Light2D Light;
        public Color BaseColor;
        public Color SecondColor;
        public bool UseColorGradient;
        public bool Breathing;
        public float BaseIntensity;
        public float BreathSpeed;
        public float BreathAmplitude;

        private float _t;

        private void Update()
        {
            if (Light == null) return;
            _t += Time.deltaTime;
            if (Breathing)
            {
                float phase = Mathf.Sin(_t * BreathSpeed * Mathf.PI * 2f);
                Light.intensity = Mathf.Max(0f, BaseIntensity * (1f + phase * BreathAmplitude));
            }
            if (UseColorGradient)
            {
                float k = (Mathf.Sin(_t * BreathSpeed * Mathf.PI) + 1f) * 0.5f;
                Light.color = Color.Lerp(BaseColor, SecondColor, k);
            }
        }
    }
}
