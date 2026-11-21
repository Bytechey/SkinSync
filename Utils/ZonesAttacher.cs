using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace SkinSyncMod
{
    /// <summary>按部件 zones 配置在角色肢体上挂载 Light2D、ParticleSystem 与序列帧动画。</summary>
    public static class ZonesAttacher
    {
        private const string ContainerPrefix = "HwZones_";
        private const float DEFAULT_PPU = 8f;
        private const int MaxZonesPerCharacter = 32;
        private static Material _defaultParticleMat;

        /// <summary>清理目标角色的旧区块容器并按当前肢体贴图重新挂载区块特效（单角色最多挂载 32 个区块）。</summary>
        public static void Apply(GameObject playerObj, string characterName)
        {
            if (playerObj == null) return;
            Body body = playerObj.GetComponentInChildren<Body>(true);
            if (body == null) return;

            ClearExisting(body);
            if (string.IsNullOrEmpty(characterName)) return;

            string skinDir = SkinPathResolver.GetSkinDir(characterName);
            IReadOnlyList<string> diskFiles = !string.IsNullOrEmpty(skinDir) && Directory.Exists(skinDir)
                ? SkinPathResolver.GetSkinFiles(skinDir)
                : null;
            bool useDisk = diskFiles != null && diskFiles.Count > 0;
            Dictionary<string, byte[]> memPack = !useDisk ? SkinPackCodec.GetInMemory(characterName) : null;
            if (!HasAnyZonesConfig(diskFiles, memPack)) return;

            int attachedCount = 0;
            foreach (var limb in body.GetComponentsInChildren<Limb>(true))
            {
                if (attachedCount >= MaxZonesPerCharacter) break;
                var sr = limb.GetComponent<SpriteRenderer>();
                if (sr == null || sr.sprite == null) continue;
                AttachForRenderer(limb.transform, sr, diskFiles, memPack, characterName, ref attachedCount);
            }
        }

        private static bool HasAnyZonesConfig(IReadOnlyList<string> diskFiles, Dictionary<string, byte[]> memPack)
        {
            if (diskFiles != null)
            {
                for (int i = 0; i < diskFiles.Count; i++)
                {
                    if (diskFiles[i].EndsWith(".zones.json", StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            if (memPack != null)
            {
                foreach (var kv in memPack)
                {
                    if (kv.Key.EndsWith(".zones.json", StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            return false;
        }

        private static void AttachForRenderer(
            Transform host,
            SpriteRenderer sr,
            IReadOnlyList<string> diskFiles,
            Dictionary<string, byte[]> memPack,
            string characterName,
            ref int attachedCount)
        {
            string spriteName = sr.sprite.name;
            int spriteW = (int)sr.sprite.rect.width;
            int spriteH = (int)sr.sprite.rect.height;

            List<ZonesConfigLoader.Entry> entries = null;
            List<ZonesConfigLoader.ZoneGeometry> geom = null;

            if (diskFiles != null && diskFiles.Count > 0)
            {
                string maskPath = FindZonesFileInList(diskFiles, spriteName, ".zones.png");
                string configPath = FindZonesFileInList(diskFiles, spriteName, ".zones.json");
                if (maskPath == null || configPath == null) return;
                (entries, geom) = ZonesConfigLoader.Load(maskPath, configPath, spriteW, spriteH);
            }
            else if (memPack != null && TryFindZonesInMemory(memPack, spriteName, out string maskRel, out byte[] maskBytes, out string cfgJson))
            {
                (entries, geom) = ZonesConfigLoader.LoadFromBytes(characterName + "/" + maskRel, maskBytes, cfgJson, spriteW, spriteH);
            }

            if (entries == null || geom == null || geom.Count == 0) return;

            var geomById = new Dictionary<byte, ZonesConfigLoader.ZoneGeometry>(geom.Count);
            foreach (var g in geom) geomById[g.Id] = g;

            foreach (var entry in entries)
            {
                if (attachedCount >= MaxZonesPerCharacter) break;
                if (!geomById.TryGetValue(entry.Id, out var g)) continue;
                AttachZone(host, sr, entry, g, spriteW, spriteH, characterName);
                attachedCount++;
            }
        }

        private static string FindZonesFileInList(IReadOnlyList<string> files, string spriteName, string suffix)
        {
            string targetName = spriteName + suffix;
            for (int i = 0; i < files.Count; i++)
            {
                string f = files[i];
                if (!string.IsNullOrEmpty(f) && string.Equals(Path.GetFileName(f), targetName, StringComparison.OrdinalIgnoreCase))
                {
                    return f;
                }
            }
            return null;
        }

        private static bool TryFindZonesInMemory(
            Dictionary<string, byte[]> memPack,
            string spriteName,
            out string maskRelPath,
            out byte[] maskBytes,
            out string configJson)
        {
            maskRelPath = null;
            maskBytes = null;
            configJson = null;
            string maskTarget = spriteName + ".zones.png";
            string cfgTarget = spriteName + ".zones.json";
            byte[] cfgBytes = null;
            foreach (var kv in memPack)
            {
                string fileName = Path.GetFileName(kv.Key);
                if (maskBytes == null && string.Equals(fileName, maskTarget, StringComparison.OrdinalIgnoreCase))
                {
                    maskRelPath = kv.Key;
                    maskBytes = kv.Value;
                }
                else if (cfgBytes == null && string.Equals(fileName, cfgTarget, StringComparison.OrdinalIgnoreCase))
                {
                    cfgBytes = kv.Value;
                }
                if (maskBytes != null && cfgBytes != null) break;
            }
            if (maskBytes == null || cfgBytes == null) return false;
            configJson = Encoding.UTF8.GetString(cfgBytes);
            return true;
        }

        private static void AttachZone(Transform host, SpriteRenderer hostSr, ZonesConfigLoader.Entry entry,
            ZonesConfigLoader.ZoneGeometry geom, int spriteW, int spriteH, string characterName)
        {
            float ppu = hostSr != null && hostSr.sprite != null && hostSr.sprite.pixelsPerUnit > 0f
                ? hostSr.sprite.pixelsPerUnit
                : DEFAULT_PPU;

            var go = new GameObject(ContainerPrefix + entry.Id);
            go.transform.SetParent(host, worldPositionStays: false);
            float lx = (geom.CenterX - spriteW * 0.5f) / ppu;
            float ly = (geom.CenterY - spriteH * 0.5f) / ppu;
            go.transform.localPosition = new Vector3(lx, ly, 0f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            float radiusUnits = geom.Radius / ppu;

            if (entry.Light != null) AttachLight(go, entry.Light);
            if (entry.Particle != null) AttachParticle(go, entry.Particle, hostSr, radiusUnits);
            if (entry.Animation != null) AttachAnimator(go, entry.Animation, hostSr, characterName);
        }

        private static void AttachLight(GameObject host, ZonesConfigLoader.LightConfig cfg)
        {
            var light = host.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Point;
            light.color = cfg.Color;
            light.intensity = cfg.Intensity;
            light.pointLightOuterRadius = cfg.OuterRadius;
            light.pointLightInnerRadius = cfg.InnerRadius;
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

            float dirDeg = cfg.Direction;
            if (cfg.Direction == 0f)
            {
                if (cfg.Mode == "fire") dirDeg = 90f;
                else if (cfg.Mode == "water") dirDeg = -90f;
            }

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = Mathf.Clamp(cfg.Spread * 0.5f, 0f, 90f);
            float rad = dirDeg * Mathf.Deg2Rad;
            // Cone 默认沿局部 +Z 轴发射，将其转向 2D XY 平面目标角。
            shape.rotation = Quaternion.FromToRotation(Vector3.forward, new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f)).eulerAngles;
            shape.radius = Mathf.Max(0.01f, cfg.ShapeRadius > 0f ? cfg.ShapeRadius : fallbackRadius * 0.5f);

            if (cfg.EndSizeScale > 0f && Mathf.Abs(cfg.EndSizeScale - 1f) > 0.01f)
            {
                var sol = ps.sizeOverLifetime;
                sol.enabled = true;
                var curve = new AnimationCurve();
                curve.AddKey(0f, 1f);
                curve.AddKey(1f, cfg.EndSizeScale);
                sol.size = new ParticleSystem.MinMaxCurve(1f, curve);
            }

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            Color32 startC = cfg.StartColor;
            Color32 endC = cfg.EndColor ?? new Color32(startC.r, startC.g, startC.b, 0);
            grad.SetKeys(
                new[] { new GradientColorKey((Color)startC, 0f), new GradientColorKey((Color)endC, 1f) },
                BuildAlphaKeys(cfg.FadeCurve, startC.a, endC.a));
            col.color = new ParticleSystem.MinMaxGradient(grad);

            switch (cfg.Mode)
            {
                case "fire":
                    main.gravityModifier = -0.4f;
                    break;
                case "water":
                    main.gravityModifier = cfg.Gravity > 0f ? cfg.Gravity : 1.2f;
                    break;
                case "orbit":
                    var vol = ps.velocityOverLifetime;
                    vol.enabled = true;
                    vol.orbitalZ = new ParticleSystem.MinMaxCurve(baseSpeed * Mathf.Deg2Rad);
                    main.startSpeed = 0f;
                    break;
                case "star":
                    main.startSpeed = 0f;
                    break;
            }

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = EnsureDefaultParticleMaterial();
                if (hostSr != null)
                {
                    renderer.sortingLayerID = hostSr.sortingLayerID;
                    renderer.sortingOrder = hostSr.sortingOrder + 1;
                }
            }
        }

        private static Material EnsureDefaultParticleMaterial()
        {
            if (_defaultParticleMat != null) return _defaultParticleMat;
            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Particles/Standard Unlit");
            if (shader != null)
            {
                _defaultParticleMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            return _defaultParticleMat;
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

        private static void AttachAnimator(GameObject host, ZonesConfigLoader.AnimationConfig cfg, SpriteRenderer hostSr, string characterName)
        {
            var sr = host.AddComponent<SpriteRenderer>();
            if (hostSr != null)
            {
                sr.sortingLayerID = hostSr.sortingLayerID;
                sr.sortingOrder = hostSr.sortingOrder + 2;
                sr.sharedMaterial = SkinApplier.GetSharedCleanLitMaterial(hostSr);
            }
            var anim = host.AddComponent<ZoneFrameAnimator>();
            anim.CharacterName = characterName;
            anim.FrameNames = cfg.Frames.ToArray();
            anim.Fps = cfg.Fps;
            anim.Loop = cfg.Loop;
        }

        private static void ClearExisting(Body body)
        {
            foreach (var tf in body.GetComponentsInChildren<Transform>(true))
            {
                if (tf == null || tf.gameObject == null) continue;
                if (tf.name.StartsWith(ContainerPrefix, StringComparison.Ordinal))
                    UnityEngine.Object.Destroy(tf.gameObject);
            }
        }
    }

    /// <summary>按固定帧率轮播区块覆盖层的序列帧贴图。</summary>
    public class ZoneFrameAnimator : MonoBehaviour
    {
        public string CharacterName;
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
            var dict = SkinApplier.GetSpriteDict(CharacterName);
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
                if (Loop)
                {
                    if (_index >= _frames.Length) _index = 0;
                }
                else if (_index >= _frames.Length)
                {
                    _index = _frames.Length - 1;
                    _sr.sprite = _frames[_index];
                    enabled = false;
                    break;
                }
                _sr.sprite = _frames[_index];
            }
        }
    }

    /// <summary>驱动区块点光源的强度呼吸与双色渐变动画。</summary>
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
