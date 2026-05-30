using System.IO;
using BepInEx;
using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>
    /// 按skin/blood.json 给 Body 上每个 Limb 改：
    /// - bleedPart / waterBleedPart 的 startColor / endColor / size / lifetime / fadeCurve / 多色随机池 / 动效 / 光影
    /// - limb material 的 _BloodDark / _BloodLight uniform（受伤血迹颜色）
    /// 反射拿 Limb 的 private bleedPart / waterBleedPart 字段——游戏不暴露 public 访问器。
    /// </summary>
    public static class BloodAttacher
    {
        private static readonly System.Reflection.FieldInfo _bleedField =
            typeof(Limb).GetField("bleedPart", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        private static readonly System.Reflection.FieldInfo _waterBleedField =
            typeof(Limb).GetField("waterBleedPart", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        private static readonly System.Collections.Generic.Dictionary<int, (Color dark, Color light)> _origLimbBlood
            = new System.Collections.Generic.Dictionary<int, (Color dark, Color light)>();
        private static readonly System.Collections.Generic.Dictionary<int, ParticleState> _origParticles
            = new System.Collections.Generic.Dictionary<int, ParticleState>();

        private struct ParticleState
        {
            public ParticleSystem.MinMaxGradient StartColor;
            public float StartSize;
            public float StartLifetime;
            public bool ColorOverLifetimeEnabled;
            public bool TextureSheetEnabled;
            public Material RendererMaterial;
        }

        /// <summary>Apply：切皮肤时染全部 limb；blood.json 缺失则把受伤血色与流血粒子恢复游戏默认。</summary>
        public static void Apply(GameObject playerObj, string characterName)
        {
            if (playerObj == null || string.IsNullOrEmpty(characterName)) return;
            if (!BloodRenderConfig.Enabled) return;
            string skinDir = SkinPathResolver.GetSkinDir(characterName);
            var cfg = BloodConfigLoader.Load(skinDir);

            Body body = playerObj.GetComponentInChildren<Body>(true);
            if (body == null) return;

            Texture2D customTex = cfg != null ? LoadTexture(Path.Combine(skinDir, "Blood/BloodParticle.png")) : null;
            foreach (var limb in body.GetComponentsInChildren<Limb>(true))
            {
                ApplyToLimb(limb, cfg, customTex);
            }

            var watcher = playerObj.GetComponent<BloodVomitWatcher>();
            if (watcher == null) watcher = playerObj.AddComponent<BloodVomitWatcher>();
            watcher.Configure(body, characterName);

            if (cfg == null) return;
            foreach (var bp in body.GetComponentsInChildren<BleedParticle>(true))
            {
                Patches.BleedParticleRecolor.RecolorByCharacter(bp, characterName);
            }
        }

        /// <summary>给运行时新生成的 ParticleSystem（vomitBlood 等）按 character 重染色——给 BloodVomitWatcher 用。</summary>
        public static void RecolorParticleByCharacter(ParticleSystem ps, string characterName)
        {
            if (ps == null || string.IsNullOrEmpty(characterName)) return;
            if (!BloodRenderConfig.Enabled) return;
            string skinDir = SkinPathResolver.GetSkinDir(characterName);
            var cfg = BloodConfigLoader.Load(skinDir);
            if (cfg == null) return;
            string fullSpritePath = Path.Combine(skinDir, "Blood/BloodParticle.png");
            Texture2D customTex = LoadTexture(fullSpritePath);
            ApplyToParticle(ps, cfg, customTex);
        }

        /// <summary>读指定 character 的 blood.json 与自定义粒子贴图（缺失返回 null）；BloodGroundRecolorer 复用。</summary>
        internal static bool TryLoadCharacterBlood(string characterName, out BloodConfigLoader.Config cfg, out Texture2D customTex)
        {
            cfg = null;
            customTex = null;
            if (!BloodRenderConfig.Enabled) return false;
            if (string.IsNullOrEmpty(characterName)) return false;
            string skinDir = SkinPathResolver.GetSkinDir(characterName);
            cfg = BloodConfigLoader.Load(skinDir);
            if (cfg == null) return false;
            customTex = LoadTexture(Path.Combine(skinDir, "Blood/BloodParticle.png"));
            return true;
        }

        /// <summary>给落地 / 爆血粒子按 character 重染色，复用 ApplyToParticle。</summary>
        internal static void ApplyToParticleByCharacter(ParticleSystem ps, string characterName)
        {
            if (ps == null) return;
            if (!TryLoadCharacterBlood(characterName, out var cfg, out var tex)) return;
            ApplyToParticle(ps, cfg, tex);
        }

        private static void ApplyToLimb(Limb limb, BloodConfigLoader.Config cfg, Texture2D customTex)
        {
            if (limb == null) return;
            ApplyToParticle(_bleedField?.GetValue(limb) as ParticleSystem, cfg, customTex, persistent: true);
            ApplyToParticle(_waterBleedField?.GetValue(limb) as ParticleSystem, cfg, customTex, persistent: true);

            var sr = limb.GetComponent<SpriteRenderer>();
            var mat = sr != null ? sr.sharedMaterial : null;
            if (mat == null) return;

            int matId = mat.GetInstanceID();
            if (!_origLimbBlood.ContainsKey(matId))
            {
                _origLimbBlood[matId] = (mat.GetColor("_BloodDark"), mat.GetColor("_BloodLight"));
            }

            if (cfg == null)
            {
                var orig = _origLimbBlood[matId];
                mat.SetColor("_BloodDark", orig.dark);
                mat.SetColor("_BloodLight", orig.light);
                return;
            }

            Color32? darkSrc = cfg.BloodDark ?? cfg.ParticleEndColor ?? cfg.ParticleStartColor;
            Color32? lightSrc = cfg.BloodLight ?? cfg.ParticleStartColor ?? cfg.ParticleEndColor;
            if (darkSrc.HasValue)
            {
                Color c = darkSrc.Value; c.a = 1f;
                mat.SetColor("_BloodDark", c);
            }
            if (lightSrc.HasValue)
            {
                Color c = lightSrc.Value; c.a = 1f;
                mat.SetColor("_BloodLight", c);
            }
            SkinSyncMod.ModLog.Info($"limb {limb.gameObject.name} 受伤血色：dark={(darkSrc.HasValue ? darkSrc.Value.ToString() : "n/a")} light={(lightSrc.HasValue ? lightSrc.Value.ToString() : "n/a")} mat={mat.name}");
        }

        /// <summary>给单个 limb 按 character 设受伤血色 + 染 BleedParticle prefab；Limb.Awake patch 用。</summary>
        internal static void ApplyToLimbByCharacter(Limb limb, string characterName)
        {
            if (limb == null || string.IsNullOrEmpty(characterName)) return;
            if (!TryLoadCharacterBlood(characterName, out var cfg, out var tex)) return;
            ApplyToLimb(limb, cfg, tex);
        }

        private static void ApplyToParticle(ParticleSystem ps, BloodConfigLoader.Config cfg, Texture2D customTex, bool persistent = false)
        {
            if (ps == null) return;
            var main = ps.main;

            if (persistent)
            {
                int psId = ps.GetInstanceID();
                if (!_origParticles.ContainsKey(psId))
                {
                    var rd0 = ps.GetComponent<ParticleSystemRenderer>();
                    _origParticles[psId] = new ParticleState
                    {
                        StartColor = main.startColor,
                        StartSize = main.startSize.constant,
                        StartLifetime = main.startLifetime.constant,
                        ColorOverLifetimeEnabled = ps.colorOverLifetime.enabled,
                        TextureSheetEnabled = ps.textureSheetAnimation.enabled,
                        RendererMaterial = rd0 != null ? rd0.sharedMaterial : null,
                    };
                }
                if (cfg == null)
                {
                    var s = _origParticles[psId];
                    main.startColor = s.StartColor;
                    main.startSize = s.StartSize;
                    main.startLifetime = s.StartLifetime;
                    var col0 = ps.colorOverLifetime; col0.enabled = s.ColorOverLifetimeEnabled;
                    var tsa0 = ps.textureSheetAnimation; tsa0.enabled = s.TextureSheetEnabled;
                    var rd1 = ps.GetComponent<ParticleSystemRenderer>();
                    if (rd1 != null && s.RendererMaterial != null) rd1.sharedMaterial = s.RendererMaterial;
                    return;
                }
            }

            // 起始颜色：随机池非空时走 RandomColor 模式；否则按单色 / 渐变。
            if (cfg.RandomColors != null && cfg.RandomColors.Length > 0)
            {
                main.startColor = new ParticleSystem.MinMaxGradient(BuildRandomGradient(cfg.RandomColors))
                {
                    mode = ParticleSystemGradientMode.RandomColor,
                };
            }
            else if (cfg.ParticleStartColor.HasValue)
            {
                if (cfg.ParticleEndColor.HasValue)
                {
                    main.startColor = new ParticleSystem.MinMaxGradient(
                        (Color)cfg.ParticleStartColor.Value, (Color)cfg.ParticleEndColor.Value)
                    {
                        mode = ParticleSystemGradientMode.TwoColors,
                    };
                }
                else
                {
                    main.startColor = (Color)cfg.ParticleStartColor.Value;
                }
            }

            if (cfg.ParticleSize.HasValue) main.startSize = cfg.ParticleSize.Value;
            if (cfg.ParticleLifetime.HasValue) main.startLifetime = cfg.ParticleLifetime.Value;

            // alpha 渐隐曲线：覆盖到 colorOverLifetime。
            if (!string.IsNullOrEmpty(cfg.FadeCurve))
            {
                var col = ps.colorOverLifetime;
                col.enabled = true;
                var grad = new Gradient();
                grad.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    BuildAlphaKeys(cfg.FadeCurve));
                col.color = new ParticleSystem.MinMaxGradient(grad);
            }

            // 自定义贴图 + 光影 + 动效。
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null && customTex != null)
            {
                // material 直接修改影响所有 limb（共享）——切皮肤时先 new 一份避免污染默认。
                if (renderer.sharedMaterial != null)
                {
                    var matInstance = new Material(renderer.sharedMaterial);
                    matInstance.mainTexture = customTex;
                    if (cfg.Glow ?? false)
                    {
                        // Additive blend：让粒子叠加发光效果（默认材质是 Sprites/Default 不带 additive）。
                        matInstance.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                        matInstance.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
                    }
                    renderer.material = matInstance;
                }
            }

            // Glow 强度：对 startColor 各分量乘 GlowIntensity（hdr 风格）。
            if ((cfg.Glow ?? false) && (cfg.GlowIntensity ?? 1f) > 1f)
            {
                Color baseColor = main.startColor.color;
                float gi = cfg.GlowIntensity.Value;
                main.startColor = new Color(
                    Mathf.Min(1f, baseColor.r * gi),
                    Mathf.Min(1f, baseColor.g * gi),
                    Mathf.Min(1f, baseColor.b * gi),
                    baseColor.a);
            }

            // 动效：按 textureSheetAnimation 设置横向多帧。
            if ((cfg.Animated ?? false) && (cfg.AnimFrames ?? 1) > 1)
            {
                var tsa = ps.textureSheetAnimation;
                tsa.enabled = true;
                tsa.numTilesX = Mathf.Max(1, cfg.AnimFrames.Value);
                tsa.numTilesY = 1;
                tsa.cycleCount = 1;
                float fps = Mathf.Max(1f, cfg.AnimFps ?? 8f);
                float lifetime = main.startLifetime.constant;
                if (lifetime <= 0f) lifetime = 1f;
                // tsa 的 frameOverTime 0→1 对应整段 lifetime；让粒子寿命内播 fps×lifetime 个循环。
                int frames = cfg.AnimFrames.Value;
                tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f, frames - 1);
            }
        }

        private static Texture2D LoadTexture(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                byte[] data = File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                if (!ImageConversion.LoadImage(tex, data))
                {
                    UnityEngine.Object.Destroy(tex);
                    return null;
                }
                return tex;
            }
            catch (System.Exception ex)
            {
                SkinSyncMod.ModLog.Warning("Load blood particle texture failed: " + ex.Message);
                return null;
            }
        }

        private static Gradient BuildRandomGradient(Color32[] colors)
        {
            // RandomColor 模式下 gradient 的颜色键决定可选色集；我们均匀分布到 0..1。
            var g = new Gradient();
            int n = colors.Length;
            var ck = new GradientColorKey[n];
            var ak = new GradientAlphaKey[n];
            for (int i = 0; i < n; i++)
            {
                float t = n == 1 ? 0f : (float)i / (n - 1);
                ck[i] = new GradientColorKey(new Color(colors[i].r / 255f, colors[i].g / 255f, colors[i].b / 255f), t);
                ak[i] = new GradientAlphaKey(colors[i].a / 255f, t);
            }
            g.SetKeys(ck, ak);
            return g;
        }

        private static GradientAlphaKey[] BuildAlphaKeys(string curve)
        {
            switch (curve)
            {
                case "constant":
                    return new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) };
                case "easeOut":
                    return new[]
                    {
                        new GradientAlphaKey(1f, 0f),
                        new GradientAlphaKey(0.7f, 0.6f),
                        new GradientAlphaKey(0f, 1f),
                    };
                case "blink":
                    return new[]
                    {
                        new GradientAlphaKey(1f, 0f),
                        new GradientAlphaKey(0f, 0.3f),
                        new GradientAlphaKey(1f, 0.6f),
                        new GradientAlphaKey(0f, 1f),
                    };
                default:
                    return new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) };
            }
        }
    }
}
