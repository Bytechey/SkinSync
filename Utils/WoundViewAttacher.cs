using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace SkinSyncMod
{
    /// <summary>按被观察角色当前生效皮肤替换 WoundView 伤势面板贴图，维护原版尺寸比例与彩色肌肉层显示。</summary>
    internal static class WoundViewAttacher
    {
        private const float PIXELS_PER_UNIT = 8f;

        private sealed class WoundSkinData
        {
            public readonly Dictionary<string, Sprite> Sprites
                = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, Sprite>[] SpeciesSprites =
            {
                new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase),
            };
            public readonly List<Sprite> OwnedSprites = new List<Sprite>();
            public readonly HashSet<Sprite> ColoredSprites = new HashSet<Sprite>();
            public Dictionary<string, Vector2Int> BaseSizes;
        }

        private struct ViewAppliedKey : IEquatable<ViewAppliedKey>
        {
            public int BodyId;
            public int CharType;
            public int SkinVersion;
            public string SkinName;

            public bool Equals(ViewAppliedKey other)
                => BodyId == other.BodyId
                && CharType == other.CharType
                && SkinVersion == other.SkinVersion
                && string.Equals(SkinName, other.SkinName, StringComparison.Ordinal);
        }

        private static readonly string[] HeadOuterNames = { "headoutline", "milkheadouter", "headOuterDune" };
        private static readonly string[] HeadInnerNames = { "headMiddle", "milkheadmiddle", "headMiddleDune" };

        private static readonly HashSet<string> KnownWoundSpriteNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "abdomenMiddle", "abdomenOutline",
            "armMiddle", "armOutline",
            "crusMiddle", "crusOutline",
            "disfiguredExpWound", "eyeGoneExpWound",
            "footMiddle", "footOutline",
            "forearmMiddle", "forearmOutline",
            "handMiddle", "handOutline",
            "headMiddle", "headMiddleDune", "headOuterDune", "headoutline",
            "milkheadmiddle", "milkheadouter",
            "thighMiddle", "thighOutline",
            "thoraxMiddle", "thoraxOutline",
        };

        private static readonly Dictionary<string, WoundSkinData> _skinCache
            = new Dictionary<string, WoundSkinData>(StringComparer.Ordinal);
        private static readonly HashSet<string> _emptySkins
            = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<Sprite> _customSprites
            = new HashSet<Sprite>();

        private const int ImagePruneThreshold = 256;

        private static readonly HashSet<WoundView> _activeViews = new HashSet<WoundView>();
        private static readonly List<WoundView> _viewSnapshot = new List<WoundView>(8);
        private static readonly List<WoundView> _deadViews = new List<WoundView>(4);
        private static readonly Dictionary<int, Image[]> _muscleImagesByView = new Dictionary<int, Image[]>();

        private static readonly Dictionary<int, Image> _trackedImages = new Dictionary<int, Image>();
        private static readonly List<int> _deadImageIds = new List<int>();
        private static readonly Dictionary<int, Sprite> _origImageSprites = new Dictionary<int, Sprite>();
        private static readonly Dictionary<int, Vector2> _stockImageSizes = new Dictionary<int, Vector2>();
        private static readonly Dictionary<int, Sprite> _origSrSprites = new Dictionary<int, Sprite>();
        private static readonly Dictionary<int, ViewAppliedKey> _lastAppliedByView = new Dictionary<int, ViewAppliedKey>();

        internal static void OnSceneChanged()
        {
            _activeViews.Clear();
            _viewSnapshot.Clear();
            _deadViews.Clear();
            _muscleImagesByView.Clear();
            _trackedImages.Clear();
            _deadImageIds.Clear();
            _origImageSprites.Clear();
            _stockImageSizes.Clear();
            _origSrSprites.Clear();
            _lastAppliedByView.Clear();
        }

        private static void RegisterActiveView(WoundView view)
        {
            if (view != null && view.gameObject.scene.IsValid())
            {
                _activeViews.Add(view);
            }
        }

        private static List<WoundView> CollectActiveViews()
        {
            _viewSnapshot.Clear();
            if (_activeViews.Count > 0)
            {
                _deadViews.Clear();
                foreach (WoundView v in _activeViews)
                {
                    if (v == null || !v.gameObject.scene.IsValid())
                    {
                        _deadViews.Add(v);
                        continue;
                    }
                    _viewSnapshot.Add(v);
                }
                for (int i = 0; i < _deadViews.Count; i++)
                {
                    WoundView dead = _deadViews[i];
                    _activeViews.Remove(dead);
                    if (!ReferenceEquals(dead, null))
                    {
                        int deadId = dead.GetInstanceID();
                        _muscleImagesByView.Remove(deadId);
                        _lastAppliedByView.Remove(deadId);
                    }
                }
                _deadViews.Clear();
            }

            if (_viewSnapshot.Count == 0)
            {
                foreach (WoundView v in Resources.FindObjectsOfTypeAll<WoundView>())
                {
                    if (v == null || !v.gameObject.scene.IsValid()) continue;
                    _activeViews.Add(v);
                    _viewSnapshot.Add(v);
                }
            }
            return _viewSnapshot;
        }

        private static Image[] GetMuscleImages(WoundView view)
        {
            if (view == null || view.limbImages == null) return null;
            int viewId = view.GetInstanceID();
            int count = view.limbImages.Length;
            if (_muscleImagesByView.TryGetValue(viewId, out Image[] cached) && cached != null && cached.Length == count)
            {
                return cached;
            }

            var arr = new Image[count];
            for (int i = 0; i < count; i++)
            {
                Image outer = view.limbImages[i];
                if (outer != null && outer.transform.childCount > 0)
                {
                    arr[i] = outer.transform.GetChild(0).GetComponent<Image>();
                }
            }
            _muscleImagesByView[viewId] = arr;
            return arr;
        }

        /// <summary>伤势面板切换目标或重置物种头图后强制重涂自定义贴图并维持彩色肌肉层显示。</summary>
        internal static void OnTargetApplied(WoundView view, Body body)
        {
            if (view == null) return;
            RegisterActiveView(view);
            _lastAppliedByView.Remove(view.GetInstanceID());
            Apply(view, body != null ? body : view.body);
            ApplyCustomMuscleColors(view);
        }

        /// <summary>刷新场景中所有打开或已实例化的伤势面板。</summary>
        internal static void RefreshAll()
        {
            _lastAppliedByView.Clear();
            List<WoundView> views = CollectActiveViews();
            for (int i = 0; i < views.Count; i++)
            {
                WoundView view = views[i];
                if (view != null) Apply(view, view.body);
            }
        }

        /// <summary>当指定躯体的皮肤或原版开关变化时同步刷新正在观察它的伤势面板。</summary>
        internal static void RefreshForBody(Body body)
        {
            if (body == null) return;
            List<WoundView> views = CollectActiveViews();
            for (int i = 0; i < views.Count; i++)
            {
                WoundView view = views[i];
                if (view == null || view.body != body) continue;
                _lastAppliedByView.Remove(view.GetInstanceID());
                Apply(view, body);
            }
        }

        /// <summary>清理单个皮肤的伤势面板贴图缓存并安全释放纹理。</summary>
        internal static void Invalidate(string skinID)
        {
            if (string.IsNullOrEmpty(skinID)) return;
            _emptySkins.Remove(skinID);
            if (!_skinCache.TryGetValue(skinID, out WoundSkinData retired)) return;

            RestoreViewsUsing(retired);
            _skinCache.Remove(skinID);
            DestroyData(retired);
            _lastAppliedByView.Clear();
        }

        /// <summary>清空全部伤势面板贴图缓存并先将面板还原至原版基线。</summary>
        internal static void ClearCaches()
        {
            List<WoundView> views = CollectActiveViews();
            for (int i = 0; i < views.Count; i++)
            {
                WoundView view = views[i];
                if (view != null) ApplyWithData(view, view.body, null);
            }

            foreach (var kv in _skinCache)
            {
                DestroyData(kv.Value);
            }
            _skinCache.Clear();
            _emptySkins.Clear();
            _customSprites.Clear();
            _lastAppliedByView.Clear();
        }

        /// <summary>对指定 WoundView 实例应用或还原自定义伤势贴图。</summary>
        internal static void Apply(WoundView view, Body bodyOverride = null)
        {
            if (view == null) return;
            RegisterActiveView(view);
            Body body = bodyOverride != null ? bodyOverride : view.body;
            int skinVersion = SkinApplier.SkinStateVersion;
            string skinName = SkinApplier.GetActiveSkinForBody(body);
            WoundSkinData data = string.IsNullOrEmpty(skinName) ? null : GetOrLoad(skinName);
            ApplyWithData(view, body, data);

            _lastAppliedByView[view.GetInstanceID()] = new ViewAppliedKey
            {
                BodyId = body != null ? body.GetInstanceID() : 0,
                CharType = body != null ? body.charType : 0,
                SkinVersion = skinVersion,
                SkinName = skinName ?? string.Empty,
            };
        }

        /// <summary>在 WoundView.UpdateView 后检查目标与全局皮肤版本变化，维持彩色肌肉层不被原版底色染黑。</summary>
        internal static void OnUpdateView(WoundView view)
        {
            if (view == null) return;
            RegisterActiveView(view);
            Body body = view.body;
            int bodyId = body != null ? body.GetInstanceID() : 0;
            int charType = body != null ? body.charType : 0;
            int skinVersion = SkinApplier.SkinStateVersion;
            int viewId = view.GetInstanceID();

            if (_lastAppliedByView.TryGetValue(viewId, out ViewAppliedKey prevKey)
                && prevKey.BodyId == bodyId
                && prevKey.CharType == charType
                && prevKey.SkinVersion == skinVersion)
            {
                ApplyCustomMuscleColors(view);
                return;
            }

            string skinName = SkinApplier.GetActiveSkinForBody(body);
            if (!_lastAppliedByView.TryGetValue(viewId, out prevKey)
                || prevKey.BodyId != bodyId
                || prevKey.CharType != charType
                || !string.Equals(prevKey.SkinName, skinName ?? string.Empty, StringComparison.Ordinal))
            {
                Apply(view, body);
            }
            else
            {
                prevKey.SkinVersion = skinVersion;
                _lastAppliedByView[viewId] = prevKey;
            }

            ApplyCustomMuscleColors(view);
        }

        private static void ApplyCustomMuscleColors(WoundView view)
        {
            if (view == null || view.armorMode || view.limbImages == null) return;
            string skinName = _lastAppliedByView.TryGetValue(view.GetInstanceID(), out ViewAppliedKey applied)
                ? applied.SkinName
                : SkinApplier.GetActiveSkinForBody(view.body);
            if (string.IsNullOrEmpty(skinName)) return;

            WoundSkinData data = GetOrLoad(skinName);
            if (data == null || data.ColoredSprites.Count == 0) return;

            Image[] muscles = GetMuscleImages(view);
            if (muscles == null) return;

            for (int i = 0; i < muscles.Length; i++)
            {
                Image middle = muscles[i];
                if (middle == null)
                {
                    Image outer = view.limbImages[i];
                    if (outer != null && outer.transform.childCount > 0)
                    {
                        middle = outer.transform.GetChild(0).GetComponent<Image>();
                        muscles[i] = middle;
                    }
                    if (middle == null) continue;
                }
                if (middle.sprite == null || !data.ColoredSprites.Contains(middle.sprite)) continue;
                Color c = middle.color;
                middle.color = new Color(1f, 1f, 1f, c.a);
            }
        }

        /// <summary>替换动态挂载到肢体框上的伤口贴片并按原图比例缩放。</summary>
        internal static void ApplyDynamicLimbImage(WoundView view, Image added, Body limbBody = null)
        {
            if (view == null || added == null || added.sprite == null) return;
            RegisterActiveView(view);
            PruneDeadImagesIfNeeded();
            Body body = limbBody != null ? limbBody : view.body;
            string skinName = SkinApplier.GetActiveSkinForBody(body);
            if (string.IsNullOrEmpty(skinName)) return;
            WoundSkinData data = GetOrLoad(skinName);
            if (data == null) return;

            int charType = body != null ? Mathf.Clamp(body.charType, 0, 2) : 0;
            Sprite stock = added.sprite;
            int id = added.GetInstanceID();
            Vector2 stockSize = added.rectTransform.sizeDelta;
            _trackedImages[id] = added;
            _origImageSprites[id] = stock;
            _stockImageSizes[id] = stockSize;

            if (TryResolveSprite(data, charType, stock.name, out Sprite custom))
            {
                added.sprite = custom;
                FitToStock(added, stockSize, stock, custom, data.BaseSizes);
            }
        }

        private static void ApplyWithData(WoundView view, Body body, WoundSkinData data)
        {
            if (view == null) return;
            PruneDeadImagesIfNeeded();
            int charType = body != null ? Mathf.Clamp(body.charType, 0, 2) : 0;

            Image headOuter = view.limbImages != null && view.limbImages.Length > 0 ? view.limbImages[0] : null;
            Image headInner = headOuter != null && headOuter.transform.childCount > 0
                ? headOuter.transform.GetChild(0).GetComponent<Image>()
                : null;

            foreach (Image img in view.GetComponentsInChildren<Image>(true))
            {
                if (img == null || img.sprite == null) continue;
                int id = img.GetInstanceID();

                Sprite stockSprite;
                Vector2 stockSize;
                if (img == headOuter && TryGetStockHeadSprite(view, charType, false, out stockSprite))
                {
                    stockSize = ComputeNativeUiSize(img, stockSprite);
                    _trackedImages[id] = img;
                    _origImageSprites[id] = stockSprite;
                    _stockImageSizes[id] = stockSize;
                }
                else if (img == headInner && TryGetStockHeadSprite(view, charType, true, out stockSprite))
                {
                    stockSize = ComputeNativeUiSize(img, stockSprite);
                    _trackedImages[id] = img;
                    _origImageSprites[id] = stockSprite;
                    _stockImageSizes[id] = stockSize;
                }
                else
                {
                    if (!_customSprites.Contains(img.sprite))
                    {
                        _trackedImages[id] = img;
                        _origImageSprites[id] = img.sprite;
                        _stockImageSizes[id] = img.rectTransform.sizeDelta;
                    }
                    if (!_origImageSprites.TryGetValue(id, out stockSprite) || stockSprite == null) continue;
                    if (!_stockImageSizes.TryGetValue(id, out stockSize)) stockSize = img.rectTransform.sizeDelta;
                }

                if (data != null && TryResolveSprite(data, charType, stockSprite.name, out Sprite custom))
                {
                    if (img.sprite != custom)
                    {
                        img.sprite = custom;
                    }
                    FitToStock(img, stockSize, stockSprite, custom, data.BaseSizes);
                }
                else if (_customSprites.Contains(img.sprite))
                {
                    img.sprite = stockSprite;
                    img.rectTransform.sizeDelta = stockSize;
                }
            }

            foreach (SpriteRenderer sr in view.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sr == null || sr.sprite == null) continue;
                int id = sr.GetInstanceID();
                if (!_customSprites.Contains(sr.sprite))
                {
                    _origSrSprites[id] = sr.sprite;
                }
                if (!_origSrSprites.TryGetValue(id, out Sprite stockSr) || stockSr == null) continue;

                if (data != null && TryResolveSprite(data, charType, stockSr.name, out Sprite customSr))
                {
                    sr.sprite = customSr;
                }
                else if (_customSprites.Contains(sr.sprite))
                {
                    sr.sprite = stockSr;
                }
            }
        }

        private static Vector2 ComputeNativeUiSize(Image img, Sprite sp)
        {
            if (sp == null) return Vector2.zero;
            float ppu = sp.pixelsPerUnit > 0f ? sp.pixelsPerUnit : 100f;
            float refPpu = img != null && img.canvas != null && img.canvas.referencePixelsPerUnit > 0f
                ? img.canvas.referencePixelsPerUnit
                : 100f;
            return sp.rect.size * (refPpu / ppu);
        }

        private static void PruneDeadImagesIfNeeded()
        {
            if (_trackedImages.Count <= ImagePruneThreshold) return;
            _deadImageIds.Clear();
            foreach (var kv in _trackedImages)
            {
                if (kv.Value == null)
                {
                    _deadImageIds.Add(kv.Key);
                }
            }
            for (int i = 0; i < _deadImageIds.Count; i++)
            {
                int deadId = _deadImageIds[i];
                _trackedImages.Remove(deadId);
                _origImageSprites.Remove(deadId);
                _stockImageSizes.Remove(deadId);
            }
            _deadImageIds.Clear();
        }

        private static bool TryGetStockHeadSprite(WoundView view, int charType, bool inner, out Sprite sprite)
        {
            sprite = null;
            if (view.headCharSprites == null || view.headCharSprites.Length < 6) return false;
            int idx = Mathf.Clamp(charType, 0, 2) * 2 + (inner ? 1 : 0);
            sprite = view.headCharSprites[idx];
            return sprite != null;
        }

        private static bool TryResolveSprite(WoundSkinData data, int charType, string stockName, out Sprite sprite)
        {
            sprite = null;
            if (data == null || string.IsNullOrEmpty(stockName)) return false;

            var speciesDict = data.SpeciesSprites[Mathf.Clamp(charType, 0, 2)];
            if (speciesDict.TryGetValue(stockName, out sprite) && sprite != null) return true;
            if (data.Sprites.TryGetValue(stockName, out sprite) && sprite != null) return true;

            if (MatchesAny(stockName, HeadOuterNames))
            {
                return TryLookupFallback(speciesDict, HeadOuterNames, stockName, out sprite)
                    || TryLookupFallback(data.Sprites, HeadOuterNames, stockName, out sprite);
            }
            if (MatchesAny(stockName, HeadInnerNames))
            {
                return TryLookupFallback(speciesDict, HeadInnerNames, stockName, out sprite)
                    || TryLookupFallback(data.Sprites, HeadInnerNames, stockName, out sprite);
            }
            return false;
        }

        private static bool MatchesAny(string name, string[] candidates)
        {
            for (int i = 0; i < candidates.Length; i++)
            {
                if (string.Equals(name, candidates[i], StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static bool TryLookupFallback(Dictionary<string, Sprite> dict, string[] candidates, string skip, out Sprite sprite)
        {
            for (int i = 0; i < candidates.Length; i++)
            {
                if (string.Equals(candidates[i], skip, StringComparison.OrdinalIgnoreCase)) continue;
                if (dict.TryGetValue(candidates[i], out sprite) && sprite != null) return true;
            }
            sprite = null;
            return false;
        }

        private static void FitToStock(
            Image image,
            Vector2 stockBox,
            Sprite stock,
            Sprite custom,
            Dictionary<string, Vector2Int> baseSizes)
        {
            float logicalW = custom.rect.width;
            float logicalH = custom.rect.height;
            if (baseSizes != null && baseSizes.TryGetValue(custom.name, out Vector2Int bs) && bs.x > 0 && bs.y > 0)
            {
                logicalW = bs.x;
                logicalH = bs.y;
            }

            float stockW = stock != null ? stock.rect.width : 0f;
            float stockH = stock != null ? stock.rect.height : 0f;
            float scaleX = stockW > 0.5f ? logicalW / stockW : 1f;
            float scaleY = stockH > 0.5f ? logicalH / stockH : 1f;
            image.rectTransform.sizeDelta = new Vector2(stockBox.x * scaleX, stockBox.y * scaleY);
        }

        private static WoundSkinData GetOrLoad(string skinID)
        {
            if (string.IsNullOrEmpty(skinID) || _emptySkins.Contains(skinID)) return null;
            if (_skinCache.TryGetValue(skinID, out WoundSkinData cached)) return cached;

            var data = new WoundSkinData();
            string skinDir = SkinPathResolver.GetSkinDir(skinID);
            if (!string.IsNullOrEmpty(skinDir) && Directory.Exists(skinDir))
            {
                data.BaseSizes = BaseSizesLoader.Load(skinID);
                LoadFromDisk(skinDir, data);
            }
            else
            {
                LoadFromMemoryPack(skinID, data);
            }

            if (data.OwnedSprites.Count == 0)
            {
                _emptySkins.Add(skinID);
                return null;
            }

            _skinCache[skinID] = data;
            return data;
        }

        private static void LoadFromDisk(string skinDir, WoundSkinData data)
        {
            IReadOnlyList<string> files = SkinPathResolver.GetSkinFiles(skinDir);
            if (files == null || files.Count == 0) return;

            for (int i = 0; i < files.Count; i++)
            {
                string file = files[i];
                if (!IsPngSpriteCandidate(file)) continue;
                string rel = MakeRelativePath(skinDir, file);
                if (!IsWoundViewRelPath(rel)) continue;
                TryBuildSprite(rel, file, null, data);
            }

            for (int i = 0; i < files.Count; i++)
            {
                string file = files[i];
                if (!IsPngSpriteCandidate(file)) continue;
                string rel = MakeRelativePath(skinDir, file);
                if (IsWoundViewRelPath(rel)) continue;
                string name = Path.GetFileNameWithoutExtension(file);
                if (!KnownWoundSpriteNames.Contains(name)) continue;
                TryBuildSprite(rel, file, null, data);
            }
        }

        private static bool IsPngSpriteCandidate(string path)
            => !string.IsNullOrEmpty(path)
            && path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            && !path.EndsWith(".zones.png", StringComparison.OrdinalIgnoreCase);

        private static string MakeRelativePath(string baseDir, string fullPath)
        {
            if (string.IsNullOrEmpty(baseDir) || string.IsNullOrEmpty(fullPath)) return string.Empty;
            if (fullPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase) && fullPath.Length > baseDir.Length)
            {
                return fullPath.Substring(baseDir.Length).TrimStart('/', '\\').Replace('\\', '/');
            }
            try
            {
                string normBase = Path.GetFullPath(baseDir).TrimEnd('/', '\\');
                string normFull = Path.GetFullPath(fullPath);
                if (normFull.StartsWith(normBase, StringComparison.OrdinalIgnoreCase) && normFull.Length > normBase.Length)
                {
                    return normFull.Substring(normBase.Length).TrimStart('/', '\\').Replace('\\', '/');
                }
            }
            catch { }
            return string.Empty;
        }

        private static void LoadFromMemoryPack(string skinID, WoundSkinData data)
        {
            var pack = SkinPackCodec.GetInMemory(skinID);
            if (pack == null) return;

            foreach (var kv in pack)
            {
                if (kv.Key.EndsWith("baseSizes.json", StringComparison.OrdinalIgnoreCase))
                {
                    data.BaseSizes = BaseSizesLoader.ParseContent(Encoding.UTF8.GetString(kv.Value));
                }
            }

            foreach (var kv in pack)
            {
                if (!IsPngSpriteCandidate(kv.Key) || !IsWoundViewRelPath(kv.Key)) continue;
                TryBuildSprite(kv.Key, null, kv.Value, data);
            }

            foreach (var kv in pack)
            {
                if (!IsPngSpriteCandidate(kv.Key) || IsWoundViewRelPath(kv.Key)) continue;
                string name = Path.GetFileNameWithoutExtension(kv.Key);
                if (!KnownWoundSpriteNames.Contains(name)) continue;
                TryBuildSprite(kv.Key, null, kv.Value, data);
            }
        }

        internal static bool IsInsideWoundViewSubdir(string baseDir, string fullPath)
        {
            string rel = MakeRelativePath(baseDir, fullPath);
            return IsWoundViewRelPath(rel);
        }

        internal static bool IsWoundViewRelPath(string relPath)
        {
            if (string.IsNullOrEmpty(relPath)) return false;
            string norm = relPath.Replace('\\', '/');
            return norm.StartsWith("WoundView/", StringComparison.OrdinalIgnoreCase)
                || norm.IndexOf("/WoundView/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int DetectSpeciesSubdir(string relPath)
        {
            if (string.IsNullOrEmpty(relPath)) return -1;
            string[] segs = relPath.Replace('\\', '/').Split('/');
            for (int i = 0; i < segs.Length - 1; i++)
            {
                string s = segs[i];
                if (s.Equals("Experiment", StringComparison.OrdinalIgnoreCase) || s.Equals("Exp", StringComparison.OrdinalIgnoreCase))
                    return 0;
                if (s.Equals("Milky", StringComparison.OrdinalIgnoreCase))
                    return 1;
                if (s.Equals("Dune", StringComparison.OrdinalIgnoreCase))
                    return 2;
            }
            return -1;
        }

        private static void TryBuildSprite(string relPath, string diskPath, byte[] rawBytes, WoundSkinData data)
        {
            string spriteName = Path.GetFileNameWithoutExtension(relPath);
            if (string.IsNullOrEmpty(spriteName)) return;

            int species = DetectSpeciesSubdir(relPath);
            Dictionary<string, Sprite> targetDict = species >= 0 ? data.SpeciesSprites[species] : data.Sprites;
            if (targetDict.ContainsKey(spriteName)) return;

            byte[] bytes = rawBytes;
            if (bytes == null)
            {
                try { bytes = File.ReadAllBytes(diskPath); }
                catch { return; }
            }
            if (bytes == null || bytes.Length == 0) return;
            if (!SkinPackCodec.IsPngDimensionsSafe(bytes, spriteName)) return;

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.HideAndDontSave,
            };
            try
            {
                if (!ImageConversion.LoadImage(tex, bytes))
                {
                    UnityEngine.Object.Destroy(tex);
                    return;
                }
            }
            catch
            {
                UnityEngine.Object.Destroy(tex);
                return;
            }

            var sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), PIXELS_PER_UNIT);
            sprite.name = spriteName;
            sprite.hideFlags = HideFlags.HideAndDontSave;

            targetDict[spriteName] = sprite;
            data.OwnedSprites.Add(sprite);
            _customSprites.Add(sprite);
            if (HasColorPixels(tex))
            {
                data.ColoredSprites.Add(sprite);
            }
            // 必须在完成彩色像素扫描后再释放 CPU 可读副本，否则 GetPixels32 会因不可读失败。
            tex.Apply(false, makeNoLongerReadable: true);
        }

        private static bool HasColorPixels(Texture2D tex)
        {
            Color32[] pixels = tex.GetPixels32();
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 p = pixels[i];
                if (p.a < 128) continue;
                if (p.r != p.g || p.g != p.b) return true;
            }
            return false;
        }

        private static void RestoreViewsUsing(WoundSkinData retired)
        {
            if (retired == null || retired.OwnedSprites.Count == 0) return;
            var set = new HashSet<Sprite>(retired.OwnedSprites);
            List<WoundView> views = CollectActiveViews();
            for (int i = 0; i < views.Count; i++)
            {
                WoundView view = views[i];
                if (view == null) continue;
                bool touched = false;
                foreach (Image img in view.GetComponentsInChildren<Image>(true))
                {
                    if (img != null && img.sprite != null && set.Contains(img.sprite)) { touched = true; break; }
                }
                if (!touched)
                {
                    foreach (SpriteRenderer sr in view.GetComponentsInChildren<SpriteRenderer>(true))
                    {
                        if (sr != null && sr.sprite != null && set.Contains(sr.sprite)) { touched = true; break; }
                    }
                }
                if (touched) ApplyWithData(view, view.body, null);
            }
        }

        private static void DestroyData(WoundSkinData data)
        {
            if (data == null) return;
            for (int i = 0; i < data.OwnedSprites.Count; i++)
            {
                Sprite sp = data.OwnedSprites[i];
                if (sp == null) continue;
                _customSprites.Remove(sp);
                if (sp.texture != null) UnityEngine.Object.Destroy(sp.texture);
                UnityEngine.Object.Destroy(sp);
            }
        }
    }
}
