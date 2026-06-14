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

        // limb shader 采样的受伤血迹贴图 sampler 属性名（对应 limbBlood.png）。
        private const string LimbBloodProp = "_SampleTexture2D_722a3646334c45d58299ef3dd9fd21be_Texture_1_Texture2D";
        private static readonly System.Collections.Generic.Dictionary<int, Texture2D> _origBloodTex
            = new System.Collections.Generic.Dictionary<int, Texture2D>();
        private static readonly System.Collections.Generic.Dictionary<string, Texture2D> _recoloredBlood
            = new System.Collections.Generic.Dictionary<string, Texture2D>();
        private static readonly System.Collections.Generic.Dictionary<int, Sprite> _origNoseSprite
            = new System.Collections.Generic.Dictionary<int, Sprite>();
        private static readonly System.Collections.Generic.Dictionary<string, Sprite> _recoloredNose
            = new System.Collections.Generic.Dictionary<string, Sprite>();

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
            var cfg = BloodConfigLoader.Load(skinDir) ?? LoadCfgFromMemoryPack(characterName);

            Body body = playerObj.GetComponentInChildren<Body>(true);
            if (body == null) return;

            Texture2D customTex = cfg != null ? LoadBloodTexture(skinDir, characterName, "Blood/BloodParticle.png") : null;
            Texture2D limbBloodTex = cfg != null ? LoadBloodTexture(skinDir, characterName, "Blood/limbBlood.png") : null;
            foreach (var limb in body.GetComponentsInChildren<Limb>(true))
            {
                ApplyToLimb(limb, cfg, customTex, limbBloodTex);
            }

            Texture2D noseTex = cfg != null ? LoadBloodTexture(skinDir, characterName, "Blood/nosebleed.png") : null;
            foreach (var face in playerObj.GetComponentsInChildren<FacialExpression>(true))
            {
                ApplyNoseBleed(face, cfg, noseTex);
            }

            var watcher = playerObj.GetComponent<BloodVomitWatcher>();
            if (watcher == null) watcher = playerObj.AddComponent<BloodVomitWatcher>();
            watcher.Configure(body, characterName);
        }

        /// <summary>给运行时新生成的 ParticleSystem（vomitBlood 等）按 character 重染色——给 BloodVomitWatcher 用。</summary>
        public static void RecolorParticleByCharacter(ParticleSystem ps, string characterName)
        {
            if (ps == null || string.IsNullOrEmpty(characterName)) return;
            if (!BloodRenderConfig.Enabled) return;
            string skinDir = SkinPathResolver.GetSkinDir(characterName);
            var cfg = BloodConfigLoader.Load(skinDir) ?? LoadCfgFromMemoryPack(characterName);
            if (cfg == null) return;
            Texture2D customTex = LoadBloodTexture(skinDir, characterName, "Blood/BloodParticle.png");
            ApplyToParticle(ps, cfg, customTex);
        }

        /// <summary>读指定 character 的 blood.json 与自定义粒子贴图（缺失返回 null）。</summary>
        internal static bool TryLoadCharacterBlood(string characterName, out BloodConfigLoader.Config cfg, out Texture2D customTex)
        {
            cfg = null;
            customTex = null;
            if (!BloodRenderConfig.Enabled) return false;
            if (string.IsNullOrEmpty(characterName)) return false;
            string skinDir = SkinPathResolver.GetSkinDir(characterName);
            cfg = BloodConfigLoader.Load(skinDir) ?? LoadCfgFromMemoryPack(characterName);
            if (cfg == null) return false;
            customTex = LoadBloodTexture(skinDir, characterName, "Blood/BloodParticle.png");
            return true;
        }

        /// <summary>blood.json 内存包 fallback：磁盘缺失时读 SkinPackCodec 缓存。</summary>
        private static BloodConfigLoader.Config LoadCfgFromMemoryPack(string characterName)
        {
            var pack = SkinPackCodec.GetInMemory(characterName);
            if (pack == null) return null;
            foreach (var kv in pack)
            {
                if (kv.Key.Equals("blood.json", System.StringComparison.OrdinalIgnoreCase))
                    return BloodConfigLoader.Parse(System.Text.Encoding.UTF8.GetString(kv.Value));
            }
            return null;
        }

        /// <summary>血液贴图加载：先磁盘，后内存包字节回退构造 Texture2D。</summary>
        private static Texture2D LoadBloodTexture(string skinDir, string characterName, string relPath)
        {
            var tex = LoadTexture(Path.Combine(skinDir, relPath));
            if (tex != null) return tex;
            var pack = SkinPackCodec.GetInMemory(characterName);
            if (pack == null) return null;
            string norm = relPath.Replace('\\', '/');
            foreach (var kv in pack)
            {
                if (kv.Key.Equals(norm, System.StringComparison.OrdinalIgnoreCase))
                    return LoadTextureFromBytes(kv.Value);
            }
            return null;
        }

        private static Texture2D LoadTextureFromBytes(byte[] data)
        {
            if (data == null || data.Length == 0) return null;
            try
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                if (!ImageConversion.LoadImage(tex, data))
                {
                    UnityEngine.Object.Destroy(tex);
                    return null;
                }
                return tex;
            }
            catch { return null; }
        }

        private static void ApplyToLimb(Limb limb, BloodConfigLoader.Config cfg, Texture2D customTex, Texture2D limbBloodTex = null)
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
            if (!_origBloodTex.ContainsKey(matId))
            {
                var t = mat.GetTexture(LimbBloodProp) as Texture2D;
                if (t != null) _origBloodTex[matId] = t;
            }

            if (cfg == null)
            {
                var orig = _origLimbBlood[matId];
                mat.SetColor("_BloodDark", orig.dark);
                mat.SetColor("_BloodLight", orig.light);
                if (_origBloodTex.TryGetValue(matId, out var ot) && ot != null)
                    mat.SetTexture(LimbBloodProp, ot);
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
            if (limbBloodTex != null)
                mat.SetTexture(LimbBloodProp, limbBloodTex);
            else
                ApplyBloodTexture(mat, matId, darkSrc, lightSrc);
        }

        /// <summary>把 limb material 的受伤血迹贴图替换为按自定义血色重着色的版本；shader 直接采样该贴图而非 _BloodLight 上色。</summary>
        private static void ApplyBloodTexture(Material mat, int matId, Color32? darkSrc, Color32? lightSrc)
        {
            if (!darkSrc.HasValue && !lightSrc.HasValue) return;
            Color dark = darkSrc.HasValue ? (Color)darkSrc.Value : (Color)lightSrc.Value;
            Color light = lightSrc.HasValue ? (Color)lightSrc.Value : dark;
            dark.a = 1f; light.a = 1f;

            if (!_origBloodTex.TryGetValue(matId, out var src) || src == null)
            {
                src = mat.GetTexture(LimbBloodProp) as Texture2D;
                if (src != null) _origBloodTex[matId] = src;
            }
            if (src == null)
            {
                // 拿不到游戏默认贴图基底——用暗色单色填充贴图兜底（shader 任何 uv 采到都是该色）。
                string solidKey = $"solid|{dark}";
                if (!_recoloredBlood.TryGetValue(solidKey, out var solid) || solid == null)
                {
                    solid = BuildSolidTexture(dark);
                    _recoloredBlood[solidKey] = solid;
                }
                if (solid != null) mat.SetTexture(LimbBloodProp, solid);
                return;
            }

            string key = $"{dark}|{light}";
            if (!_recoloredBlood.TryGetValue(key, out var tex) || tex == null)
            {
                tex = RecolorLimbBlood(src, dark, light);
                _recoloredBlood[key] = tex;
            }
            if (tex != null) mat.SetTexture(LimbBloodProp, tex);
        }

        private static Texture2D BuildSolidTexture(Color color)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var px = new Color[4] { color, color, color, color };
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        private static Texture2D RecolorLimbBlood(Texture2D src, Color dark, Color light)
        {
            if (src == null) return null;
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false) { filterMode = src.filterMode };
            tex.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = tex.GetPixels();
            for (int i = 0; i < px.Length; i++)
            {
                float lum = px[i].r * 0.4f + px[i].g * 0.5f + px[i].b * 0.1f;
                Color c = Color.Lerp(dark, light, lum);
                c.a = px[i].a;
                px[i] = c;
            }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        /// <summary>按 blood.json 设鼻血：有自定义 nosebleed 贴图用贴图，否则按受伤血色重着色游戏默认鼻血贴图；blood.json 缺失恢复默认。</summary>
        private static void ApplyNoseBleed(FacialExpression face, BloodConfigLoader.Config cfg, Texture2D noseTex)
        {
            if (face == null) return;
            var sr = face.nosebleedSprite;
            if (sr == null) return;
            int id = sr.GetInstanceID();
            if (!_origNoseSprite.ContainsKey(id)) _origNoseSprite[id] = sr.sprite;
            var orig = _origNoseSprite[id];
            if (orig == null) return;

            if (cfg == null) { sr.sprite = orig; return; }

            if (noseTex != null)
            {
                sr.sprite = BuildSpriteLike(orig, noseTex);
                return;
            }

            Color32? darkSrc = cfg.BloodDark ?? cfg.ParticleEndColor ?? cfg.ParticleStartColor;
            Color32? lightSrc = cfg.BloodLight ?? cfg.ParticleStartColor ?? cfg.ParticleEndColor;
            if (!darkSrc.HasValue && !lightSrc.HasValue) { sr.sprite = orig; return; }
            Color dark = darkSrc.HasValue ? (Color)darkSrc.Value : (Color)lightSrc.Value;
            Color light = lightSrc.HasValue ? (Color)lightSrc.Value : dark;
            dark.a = 1f; light.a = 1f;
            string key = $"{id}|{dark}|{light}";
            if (!_recoloredNose.TryGetValue(key, out var spr) || spr == null)
            {
                var rtex = orig.texture != null ? RecolorLimbBlood(orig.texture, dark, light) : null;
                spr = rtex != null ? BuildSpriteLike(orig, rtex) : orig;
                _recoloredNose[key] = spr;
            }
            sr.sprite = spr;
        }

        // 用新贴图重建 sprite，保留参考 sprite 的归一化 pivot 与 pixelsPerUnit。
        private static Sprite BuildSpriteLike(Sprite reference, Texture2D tex)
        {
            if (reference == null || tex == null) return reference;
            Vector2 pivot = new Vector2(
                reference.rect.width > 0 ? reference.pivot.x / reference.rect.width : 0.5f,
                reference.rect.height > 0 ? reference.pivot.y / reference.rect.height : 0.5f);
            var spr = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), pivot, reference.pixelsPerUnit);
            spr.name = reference.name;
            return spr;
        }

        /// <summary>给单个 limb 按 character 设受伤血色 + 染 BleedParticle prefab；Limb.Awake patch 用。</summary>
        internal static void ApplyToLimbByCharacter(Limb limb, string characterName)
        {
            if (limb == null || string.IsNullOrEmpty(characterName)) return;
            if (!TryLoadCharacterBlood(characterName, out var cfg, out var tex)) return;
            string skinDir = SkinPathResolver.GetSkinDir(characterName);
            var limbBloodTex = LoadBloodTexture(skinDir, characterName, "Blood/limbBlood.png");
            ApplyToLimb(limb, cfg, tex, limbBloodTex);
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

            // 起始颜色：有自定义粒子贴图时用贴图本色（startColor 中性白），否则按设置的随机池 / 单色 / 渐变。
            if (customTex != null)
            {
                main.startColor = Color.white;
            }
            else if (cfg.RandomColors != null && cfg.RandomColors.Length > 0)
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
