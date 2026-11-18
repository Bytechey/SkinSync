using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace SkinSyncMod
{
    /// <summary>按皮肤 blood.json 配置替换角色肢体血迹颜色、流血粒子、鼻血与呕血表现。</summary>
    public static class BloodAttacher
    {
        private const string LimbBloodProp = "_SampleTexture2D_722a3646334c45d58299ef3dd9fd21be_Texture_1_Texture2D";

        private static readonly FieldInfo _bleedField =
            typeof(Limb).GetField("bleedPart", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo _waterBleedField =
            typeof(Limb).GetField("waterBleedPart", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo _limbMatField =
            typeof(Limb).GetField("mat", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly Dictionary<string, BloodConfigLoader.Config> _cfgCache
            = new Dictionary<string, BloodConfigLoader.Config>(StringComparer.Ordinal);
        private static readonly HashSet<string> _noBloodSkins
            = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Texture2D> _texCache
            = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<int, (Color dark, Color light)> _origLimbBlood
            = new Dictionary<int, (Color dark, Color light)>();
        private static readonly Dictionary<int, Texture2D> _origBloodTex
            = new Dictionary<int, Texture2D>();
        private static readonly Dictionary<int, ParticleState> _origParticles
            = new Dictionary<int, ParticleState>();
        private static readonly Dictionary<int, Sprite> _origNoseSprite
            = new Dictionary<int, Sprite>();

        private static readonly Dictionary<string, Texture2D> _recoloredBlood
            = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Sprite> _recoloredNose
            = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Material> _particleMats
            = new Dictionary<string, Material>(StringComparer.Ordinal);

        private struct ParticleState
        {
            public ParticleSystem.MinMaxGradient StartColor;
            public ParticleSystem.MinMaxCurve StartSize;
            public ParticleSystem.MinMaxCurve StartLifetime;
            public bool ColorOverLifetimeEnabled;
            public ParticleSystem.MinMaxGradient ColorOverLifetimeColor;
            public bool TextureSheetEnabled;
            public Material RendererMaterial;
        }

        /// <summary>对指定角色应用自定义血液配置；无配置时恢复原版血液表现。</summary>
        public static void Apply(GameObject playerObj, string characterName)
        {
            if (playerObj == null || string.IsNullOrEmpty(characterName)) return;
            if (!BloodRenderConfig.Enabled)
            {
                Restore(playerObj);
                return;
            }

            Body body = playerObj.GetComponentInChildren<Body>(true);
            if (body == null) return;

            TryGetBloodConfig(characterName, out var cfg);
            string skinDir = cfg != null ? SkinPathResolver.GetSkinDir(characterName) : null;
            Texture2D customTex = cfg != null ? GetCachedBloodTexture(skinDir, characterName, "Blood/BloodParticle.png") : null;
            Texture2D limbBloodTex = cfg != null ? GetCachedBloodTexture(skinDir, characterName, "Blood/limbBlood.png") : null;
            Texture2D noseTex = cfg != null ? GetCachedBloodTexture(skinDir, characterName, "Blood/nosebleed.png") : null;

            foreach (var limb in body.GetComponentsInChildren<Limb>(true))
            {
                ApplyToLimb(limb, cfg, customTex, limbBloodTex);
            }

            foreach (var face in playerObj.GetComponentsInChildren<FacialExpression>(true))
            {
                ApplyNoseBleed(face, cfg, noseTex);
            }

            var watcher = playerObj.GetComponent<BloodVomitWatcher>();
            if (cfg != null)
            {
                if (watcher == null) watcher = playerObj.AddComponent<BloodVomitWatcher>();
                watcher.Configure(body, characterName);
            }
            else if (watcher != null)
            {
                UnityEngine.Object.Destroy(watcher);
            }
        }

        /// <summary>将指定角色的肢体血色、流血粒子与鼻血贴图恢复为游戏原版。</summary>
        public static void Restore(GameObject playerObj)
        {
            if (playerObj == null) return;

            Body body = playerObj.GetComponentInChildren<Body>(true);
            if (body != null)
            {
                foreach (var limb in body.GetComponentsInChildren<Limb>(true))
                {
                    ApplyToLimb(limb, null, null, null);
                }
            }

            foreach (var face in playerObj.GetComponentsInChildren<FacialExpression>(true))
            {
                ApplyNoseBleed(face, null, null);
            }

            var watcher = playerObj.GetComponent<BloodVomitWatcher>();
            if (watcher != null)
            {
                UnityEngine.Object.Destroy(watcher);
            }
        }

        /// <summary>清空血液配置缓存并释放未被活跃粒子引用的动态贴图与材质资源。</summary>
        public static void ClearCaches()
        {
            _cfgCache.Clear();
            _noBloodSkins.Clear();

            foreach (var kv in _recoloredBlood)
            {
                if (kv.Value != null) UnityEngine.Object.Destroy(kv.Value);
            }
            _recoloredBlood.Clear();

            var origNoseSprites = new HashSet<Sprite>();
            var origNoseTextures = new HashSet<Texture2D>();
            foreach (var origSpr in _origNoseSprite.Values)
            {
                if (origSpr == null) continue;
                origNoseSprites.Add(origSpr);
                if (origSpr.texture != null) origNoseTextures.Add(origSpr.texture);
            }

            foreach (var kv in _recoloredNose)
            {
                if (kv.Value == null) continue;
                if (!kv.Key.StartsWith("custom|", StringComparison.Ordinal)
                    && kv.Value.texture != null
                    && !origNoseTextures.Contains(kv.Value.texture))
                {
                    UnityEngine.Object.Destroy(kv.Value.texture);
                }
                if (!origNoseSprites.Contains(kv.Value))
                {
                    UnityEngine.Object.Destroy(kv.Value);
                }
            }
            _recoloredNose.Clear();

            // 保护场上尚未消散的临时呕血/飞溅粒子正在引用的材质及其主贴图，避免热刷瞬间变洋红或白块。
            var activeMats = new HashSet<Material>();
            foreach (var psr in UnityEngine.Object.FindObjectsOfType<ParticleSystemRenderer>())
            {
                if (psr != null && psr.sharedMaterial != null)
                {
                    activeMats.Add(psr.sharedMaterial);
                }
            }

            var activeTexSet = new HashSet<Texture2D>();
            foreach (var kv in _particleMats)
            {
                if (kv.Value == null) continue;
                if (activeMats.Contains(kv.Value))
                {
                    kv.Value.hideFlags = HideFlags.None;
                    if (kv.Value.mainTexture is Texture2D activeTex && activeTex != null)
                    {
                        activeTexSet.Add(activeTex);
                    }
                    continue;
                }
                UnityEngine.Object.Destroy(kv.Value);
            }
            _particleMats.Clear();

            foreach (var kv in _texCache)
            {
                if (kv.Value == null) continue;
                if (activeTexSet.Contains(kv.Value))
                {
                    kv.Value.hideFlags = HideFlags.None;
                    continue;
                }
                UnityEngine.Object.Destroy(kv.Value);
            }
            _texCache.Clear();
        }

        /// <summary>使单个皮肤的血液配置与贴图缓存失效。</summary>
        internal static void Invalidate(string characterName)
        {
            if (string.IsNullOrEmpty(characterName)) return;
            _cfgCache.Remove(characterName);
            _noBloodSkins.Remove(characterName);

            string prefix = characterName + "|";
            var removeKeys = new List<string>();
            var deadTexIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var kv in _texCache)
            {
                if (kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    if (kv.Value != null)
                    {
                        deadTexIds.Add(kv.Value.GetInstanceID().ToString());
                        UnityEngine.Object.Destroy(kv.Value);
                    }
                    removeKeys.Add(kv.Key);
                }
            }
            foreach (var k in removeKeys) _texCache.Remove(k);
            if (deadTexIds.Count == 0) return;

            var deadMatKeys = new List<string>();
            foreach (var kv in _particleMats)
            {
                string[] parts = kv.Key.Split('|');
                if (parts.Length >= 2 && deadTexIds.Contains(parts[1]))
                {
                    if (kv.Value != null) UnityEngine.Object.Destroy(kv.Value);
                    deadMatKeys.Add(kv.Key);
                }
            }
            foreach (var k in deadMatKeys) _particleMats.Remove(k);

            var deadNoseKeys = new List<string>();
            foreach (var kv in _recoloredNose)
            {
                if (!kv.Key.StartsWith("custom|", StringComparison.Ordinal)) continue;
                string[] parts = kv.Key.Split('|');
                if (parts.Length >= 3 && deadTexIds.Contains(parts[2]))
                {
                    if (kv.Value != null) UnityEngine.Object.Destroy(kv.Value);
                    deadNoseKeys.Add(kv.Key);
                }
            }
            foreach (var k in deadNoseKeys) _recoloredNose.Remove(k);
        }

        /// <summary>按皮肤血液配置为运行时生成的粒子系统重设血色与贴图。</summary>
        public static void RecolorParticleByCharacter(ParticleSystem ps, string characterName)
        {
            if (ps == null || string.IsNullOrEmpty(characterName)) return;
            if (!TryLoadCharacterBlood(characterName, out var cfg, out var customTex)) return;
            ApplyToParticle(ps, cfg, customTex, persistent: false);
        }

        /// <summary>按皮肤名称读取已缓存的 blood.json 配置。</summary>
        internal static bool TryGetBloodConfig(string characterName, out BloodConfigLoader.Config cfg)
        {
            cfg = null;
            if (!BloodRenderConfig.Enabled || string.IsNullOrEmpty(characterName)) return false;
            if (_cfgCache.TryGetValue(characterName, out cfg)) return cfg != null;
            if (_noBloodSkins.Contains(characterName)) return false;

            string skinDir = SkinPathResolver.GetSkinDir(characterName);
            cfg = BloodConfigLoader.Load(skinDir) ?? LoadCfgFromMemoryPack(characterName);
            if (cfg != null)
            {
                _cfgCache[characterName] = cfg;
                return true;
            }

            _noBloodSkins.Add(characterName);
            return false;
        }

        /// <summary>读取指定皮肤的 blood.json 配置与已缓存的粒子贴图。</summary>
        internal static bool TryLoadCharacterBlood(string characterName, out BloodConfigLoader.Config cfg, out Texture2D customTex)
        {
            customTex = null;
            if (!TryGetBloodConfig(characterName, out cfg)) return false;
            string skinDir = SkinPathResolver.GetSkinDir(characterName);
            customTex = GetCachedBloodTexture(skinDir, characterName, "Blood/BloodParticle.png");
            return true;
        }

        /// <summary>为新初始化的单个 Limb 应用角色血液配置。</summary>
        internal static void ApplyToLimbByCharacter(Limb limb, string characterName)
        {
            if (limb == null || string.IsNullOrEmpty(characterName)) return;
            if (!TryLoadCharacterBlood(characterName, out var cfg, out var tex)) return;
            string skinDir = SkinPathResolver.GetSkinDir(characterName);
            var limbBloodTex = GetCachedBloodTexture(skinDir, characterName, "Blood/limbBlood.png");
            ApplyToLimb(limb, cfg, tex, limbBloodTex);
        }

        /// <summary>为单个 FacialExpression 应用角色鼻血贴图或重着色。</summary>
        internal static void ApplyToFaceByCharacter(FacialExpression face, string characterName)
        {
            if (face == null || string.IsNullOrEmpty(characterName)) return;
            if (!TryGetBloodConfig(characterName, out var cfg))
            {
                ApplyNoseBleed(face, null, null);
                return;
            }
            string skinDir = SkinPathResolver.GetSkinDir(characterName);
            var noseTex = GetCachedBloodTexture(skinDir, characterName, "Blood/nosebleed.png");
            ApplyNoseBleed(face, cfg, noseTex);
        }

        private static BloodConfigLoader.Config LoadCfgFromMemoryPack(string characterName)
        {
            var pack = SkinPackCodec.GetInMemory(characterName);
            if (pack == null) return null;
            foreach (var kv in pack)
            {
                if (kv.Key.Equals("blood.json", StringComparison.OrdinalIgnoreCase))
                    return BloodConfigLoader.Parse(Encoding.UTF8.GetString(kv.Value));
            }
            return null;
        }

        private static Texture2D GetCachedBloodTexture(string skinDir, string characterName, string relPath)
        {
            string norm = relPath.Replace('\\', '/');
            string cacheKey = characterName + "|" + norm;
            if (_texCache.TryGetValue(cacheKey, out var cached))
            {
                if (!ReferenceEquals(cached, null) && cached == null)
                {
                    _texCache.Remove(cacheKey);
                }
                else
                {
                    return cached;
                }
            }

            Texture2D loaded = !string.IsNullOrEmpty(skinDir) ? LoadTexture(Path.Combine(skinDir, relPath)) : null;
            if (loaded == null)
            {
                var pack = SkinPackCodec.GetInMemory(characterName);
                if (pack != null)
                {
                    foreach (var kv in pack)
                    {
                        if (kv.Key.Equals(norm, StringComparison.OrdinalIgnoreCase))
                        {
                            loaded = LoadTextureFromBytes(kv.Value);
                            break;
                        }
                    }
                }
            }

            _texCache[cacheKey] = loaded;
            return loaded;
        }

        private static Texture2D LoadTexture(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                return LoadTextureFromBytes(File.ReadAllBytes(path));
            }
            catch (Exception ex)
            {
                ModLog.Warning("Load blood texture failed: " + ex.Message);
                return null;
            }
        }

        private static Texture2D LoadTextureFromBytes(byte[] data)
        {
            if (data == null || data.Length == 0) return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.HideAndDontSave,
            };
            try
            {
                if (!ImageConversion.LoadImage(tex, data))
                {
                    UnityEngine.Object.Destroy(tex);
                    return null;
                }
                return tex;
            }
            catch
            {
                UnityEngine.Object.Destroy(tex);
                return null;
            }
        }

        private static void ApplyToLimb(Limb limb, BloodConfigLoader.Config cfg, Texture2D customTex, Texture2D limbBloodTex)
        {
            if (limb == null) return;
            ApplyToParticle(_bleedField?.GetValue(limb) as ParticleSystem, cfg, customTex, persistent: true);
            ApplyToParticle(_waterBleedField?.GetValue(limb) as ParticleSystem, cfg, customTex, persistent: true);

            // 仅修改已完成 Awake 实例化独立材质的 Limb，避免污染未唤醒实例引用的共享默认材质。
            var mat = _limbMatField?.GetValue(limb) as Material;
            if (mat == null) return;

            int matId = mat.GetInstanceID();
            if (!_origLimbBlood.ContainsKey(matId))
            {
                _origLimbBlood[matId] = (mat.GetColor("_BloodDark"), mat.GetColor("_BloodLight"));
            }
            if (!_origBloodTex.ContainsKey(matId))
            {
                _origBloodTex[matId] = mat.GetTexture(LimbBloodProp) as Texture2D;
            }

            var orig = _origLimbBlood[matId];
            mat.SetColor("_BloodDark", orig.dark);
            mat.SetColor("_BloodLight", orig.light);
            if (_origBloodTex.TryGetValue(matId, out var origTex) && origTex != null)
            {
                mat.SetTexture(LimbBloodProp, origTex);
            }

            if (cfg == null) return;

            Color32? darkSrc = cfg.BloodDark ?? cfg.ParticleEndColor ?? cfg.ParticleStartColor;
            Color32? lightSrc = cfg.BloodLight ?? cfg.ParticleStartColor ?? cfg.ParticleEndColor;
            if (darkSrc.HasValue)
            {
                Color c = darkSrc.Value;
                c.a = 1f;
                mat.SetColor("_BloodDark", c);
            }
            if (lightSrc.HasValue)
            {
                Color c = lightSrc.Value;
                c.a = 1f;
                mat.SetColor("_BloodLight", c);
            }

            if (limbBloodTex != null)
                mat.SetTexture(LimbBloodProp, limbBloodTex);
            else
                ApplyBloodTexture(mat, matId, darkSrc, lightSrc);
        }

        private static void ApplyBloodTexture(Material mat, int matId, Color32? darkSrc, Color32? lightSrc)
        {
            if (!darkSrc.HasValue && !lightSrc.HasValue) return;
            Color dark = darkSrc.HasValue ? (Color)darkSrc.Value : (Color)lightSrc.Value;
            Color light = lightSrc.HasValue ? (Color)lightSrc.Value : dark;
            dark.a = 1f;
            light.a = 1f;

            string darkHex = ColorUtility.ToHtmlStringRGBA(dark);
            string lightHex = ColorUtility.ToHtmlStringRGBA(light);

            if (!_origBloodTex.TryGetValue(matId, out var src) || src == null)
            {
                src = mat.GetTexture(LimbBloodProp) as Texture2D;
                if (src != null) _origBloodTex[matId] = src;
            }
            if (src == null)
            {
                string solidKey = "solid|" + darkHex;
                if (!_recoloredBlood.TryGetValue(solidKey, out var solid) || solid == null)
                {
                    solid = BuildSolidTexture(dark);
                    _recoloredBlood[solidKey] = solid;
                }
                if (solid != null) mat.SetTexture(LimbBloodProp, solid);
                return;
            }

            string key = src.GetInstanceID() + "|" + darkHex + "|" + lightHex;
            if (!_recoloredBlood.TryGetValue(key, out var tex) || tex == null)
            {
                tex = RecolorLimbBlood(src, dark, light);
                _recoloredBlood[key] = tex;
            }
            if (tex != null) mat.SetTexture(LimbBloodProp, tex);
        }

        private static Texture2D BuildSolidTexture(Color color)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.HideAndDontSave,
            };
            tex.SetPixels(new[] { color, color, color, color });
            tex.Apply();
            return tex;
        }

        private static Texture2D RecolorLimbBlood(Texture2D src, Color dark, Color light)
        {
            if (src == null) return null;
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            Texture2D tex;
            try
            {
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false)
                {
                    filterMode = src.filterMode,
                    hideFlags = HideFlags.HideAndDontSave,
                };
                tex.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }

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

        internal static void ApplyNoseBleed(FacialExpression face, BloodConfigLoader.Config cfg, Texture2D noseTex)
        {
            if (face == null) return;
            var sr = face.nosebleedSprite;
            if (sr == null) return;
            int id = sr.GetInstanceID();
            if (!_origNoseSprite.ContainsKey(id)) _origNoseSprite[id] = sr.sprite;
            var orig = _origNoseSprite[id];
            if (orig == null) return;

            sr.sprite = orig;
            if (cfg == null) return;

            if (noseTex != null)
            {
                string customKey = "custom|" + orig.GetInstanceID() + "|" + noseTex.GetInstanceID();
                if (!_recoloredNose.TryGetValue(customKey, out var customSpr) || customSpr == null)
                {
                    customSpr = BuildSpriteLike(orig, noseTex);
                    _recoloredNose[customKey] = customSpr;
                }
                sr.sprite = customSpr;
                return;
            }

            Color32? darkSrc = cfg.BloodDark ?? cfg.ParticleEndColor ?? cfg.ParticleStartColor;
            Color32? lightSrc = cfg.BloodLight ?? cfg.ParticleStartColor ?? cfg.ParticleEndColor;
            if (!darkSrc.HasValue && !lightSrc.HasValue) return;
            Color dark = darkSrc.HasValue ? (Color)darkSrc.Value : (Color)lightSrc.Value;
            Color light = lightSrc.HasValue ? (Color)lightSrc.Value : dark;
            dark.a = 1f;
            light.a = 1f;
            string key = orig.GetInstanceID() + "|" + ColorUtility.ToHtmlStringRGBA(dark) + "|" + ColorUtility.ToHtmlStringRGBA(light);
            if (!_recoloredNose.TryGetValue(key, out var spr) || spr == null)
            {
                var rtex = orig.texture != null ? RecolorLimbBlood(orig.texture, dark, light) : null;
                spr = rtex != null ? BuildSpriteLike(orig, rtex) : orig;
                _recoloredNose[key] = spr;
            }
            sr.sprite = spr;
        }

        private static Sprite BuildSpriteLike(Sprite reference, Texture2D tex)
        {
            if (reference == null || tex == null) return reference;
            Vector2 pivot = new Vector2(
                reference.rect.width > 0 ? reference.pivot.x / reference.rect.width : 0.5f,
                reference.rect.height > 0 ? reference.pivot.y / reference.rect.height : 0.5f);
            var spr = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), pivot, reference.pixelsPerUnit);
            spr.name = reference.name;
            spr.hideFlags = HideFlags.HideAndDontSave;
            return spr;
        }

        private static void ApplyToParticle(ParticleSystem ps, BloodConfigLoader.Config cfg, Texture2D customTex, bool persistent)
        {
            if (ps == null) return;
            var main = ps.main;
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            Material baseMat = renderer != null ? renderer.sharedMaterial : null;

            if (persistent)
            {
                int psId = ps.GetInstanceID();
                if (!_origParticles.TryGetValue(psId, out var origState))
                {
                    origState = new ParticleState
                    {
                        StartColor = main.startColor,
                        StartSize = main.startSize,
                        StartLifetime = main.startLifetime,
                        ColorOverLifetimeEnabled = ps.colorOverLifetime.enabled,
                        ColorOverLifetimeColor = ps.colorOverLifetime.color,
                        TextureSheetEnabled = ps.textureSheetAnimation.enabled,
                        RendererMaterial = baseMat,
                    };
                    _origParticles[psId] = origState;
                }

                main.startColor = origState.StartColor;
                main.startSize = origState.StartSize;
                main.startLifetime = origState.StartLifetime;
                var colOrig = ps.colorOverLifetime;
                colOrig.enabled = origState.ColorOverLifetimeEnabled;
                colOrig.color = origState.ColorOverLifetimeColor;
                var tsaOrig = ps.textureSheetAnimation;
                tsaOrig.enabled = origState.TextureSheetEnabled;
                if (renderer != null && origState.RendererMaterial != null)
                {
                    renderer.sharedMaterial = origState.RendererMaterial;
                    baseMat = origState.RendererMaterial;
                }

                if (cfg == null) return;
            }
            else if (cfg == null)
            {
                return;
            }

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

            bool glow = cfg.Glow ?? false;
            if (renderer != null && baseMat != null && (customTex != null || glow))
            {
                renderer.sharedMaterial = GetOrCreateParticleMaterial(baseMat, customTex, glow);
            }

            if (glow && (cfg.GlowIntensity ?? 1f) > 1f && main.startColor.mode == ParticleSystemGradientMode.Color)
            {
                Color baseColor = main.startColor.color;
                float gi = cfg.GlowIntensity.Value;
                main.startColor = new Color(
                    Mathf.Min(1f, baseColor.r * gi),
                    Mathf.Min(1f, baseColor.g * gi),
                    Mathf.Min(1f, baseColor.b * gi),
                    baseColor.a);
            }

            if ((cfg.Animated ?? false) && (cfg.AnimFrames ?? 1) > 1)
            {
                var tsa = ps.textureSheetAnimation;
                tsa.enabled = true;
                tsa.numTilesX = Mathf.Max(1, cfg.AnimFrames.Value);
                tsa.numTilesY = 1;
                float fps = Mathf.Max(1f, cfg.AnimFps ?? 8f);
                float lifetime = main.startLifetime.constant;
                if (lifetime <= 0f) lifetime = 1f;
                tsa.cycleCount = Mathf.Max(1, Mathf.RoundToInt(fps * lifetime));
                tsa.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
            }
        }

        private static Material GetOrCreateParticleMaterial(Material baseMat, Texture2D customTex, bool glow)
        {
            int texId = customTex != null ? customTex.GetInstanceID() : 0;
            string key = baseMat.GetInstanceID() + "|" + texId + "|" + (glow ? "1" : "0");
            if (_particleMats.TryGetValue(key, out var mat) && mat != null)
            {
                if (customTex != null && mat.mainTexture != customTex)
                    mat.mainTexture = customTex;
                return mat;
            }

            mat = new Material(baseMat) { hideFlags = HideFlags.HideAndDontSave };
            if (customTex != null) mat.mainTexture = customTex;
            if (glow)
            {
                mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)BlendMode.One);
            }
            _particleMats[key] = mat;
            return mat;
        }

        private static Gradient BuildRandomGradient(Color32[] colors)
        {
            var g = new Gradient { mode = GradientMode.Fixed };
            int n = Mathf.Min(colors.Length, 8);
            var ck = new GradientColorKey[n];
            var ak = new GradientAlphaKey[n];
            for (int i = 0; i < n; i++)
            {
                float t = n == 1 ? 1f : (float)(i + 1) / n;
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
