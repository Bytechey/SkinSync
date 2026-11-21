using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace SkinSyncMod
{
    public static partial class SkinApplier
    {
        private static readonly Dictionary<int, Sprite> _originalLimbSprites = new Dictionary<int, Sprite>();
        private static readonly Dictionary<int, Sprite> _originalTailSprites = new Dictionary<int, Sprite>();
        private static readonly Dictionary<int, FacialOriginal> _originalFaces = new Dictionary<int, FacialOriginal>();
        private static readonly Dictionary<long, Sprite> _fittedSprites = new Dictionary<long, Sprite>();

        private static MethodInfo _replacerRestoreMethod;
        private static PropertyInfo _replacerActiveProp;
        private static bool _replacerReflected;

        private static FieldInfo _pauseOrigEyesField;
        private static FieldInfo _pausePausedEyesField;
        private static FieldInfo _pauseLastAliveField;
        private static bool _pauseReflected;

        private struct FacialOriginal
        {
            public Sprite DefaultHead;
            public Sprite DefaultHeadMouth;
            public Sprite DefaultHeadMouthHalf;
            public Sprite EyesGone;
            public Sprite EyesGoneHealed;
            public Sprite[] DisfiguredHead;
            public Sprite[] DisfiguredHeadHeal;
            public Eye[] Eyes;
        }

        private static void ClearBaselineDictionaries()
        {
            _originalLimbSprites.Clear();
            _originalTailSprites.Clear();
            _originalFaces.Clear();
        }

        private static void RestoreVanillaParts(GameObject playerObj)
        {
            foreach (var limb in playerObj.GetComponentsInChildren<Limb>(true))
            {
                var renderer = limb.GetComponent<SpriteRenderer>();
                if (renderer == null) continue;
                if (_originalLimbSprites.TryGetValue(renderer.GetInstanceID(), out var orig) && orig != null)
                    renderer.sprite = orig;
            }

            foreach (var tail in playerObj.GetComponentsInChildren<TailScript>(true))
            {
                foreach (var deform in tail.GetComponents<TailFlowDeform>())
                {
                    if (deform != null) deform.enabled = false;
                }

                var renderer = tail.GetComponent<SpriteRenderer>();
                if (renderer != null && _originalTailSprites.TryGetValue(renderer.GetInstanceID(), out var orig) && orig != null)
                    renderer.sprite = orig;
            }

            foreach (var face in playerObj.GetComponentsInChildren<FacialExpression>(true))
            {
                if (!_originalFaces.TryGetValue(face.GetInstanceID(), out var f)) continue;
                face.defaultHead = f.DefaultHead;
                face.defaultHeadMouth = f.DefaultHeadMouth;
                face.defaultHeadMouthHalf = f.DefaultHeadMouthHalf;
                face.eyesGone = f.EyesGone;
                face.eyesGoneHealed = f.EyesGoneHealed;
                if (face.disfiguredHead != null && f.DisfiguredHead != null)
                {
                    int n = Mathf.Min(face.disfiguredHead.Length, f.DisfiguredHead.Length);
                    for (int i = 0; i < n; i++) face.disfiguredHead[i] = f.DisfiguredHead[i];
                }
                if (face.disfiguredHeadHeal != null && f.DisfiguredHeadHeal != null)
                {
                    int n = Mathf.Min(face.disfiguredHeadHeal.Length, f.DisfiguredHeadHeal.Length);
                    for (int i = 0; i < n; i++) face.disfiguredHeadHeal[i] = f.DisfiguredHeadHeal[i];
                }
                RestoreEyeList(face, f.Eyes);
                // 死亡或暂停期间 FacialExpression 不再每帧刷新渲染器，需立即回写实时贴图以防热重载销毁旧字典后残留悬空引用。
                if (face.head != null && f.DefaultHead != null)
                    face.head.sprite = f.DefaultHead;
                if (face.eyes != null && f.Eyes != null && f.Eyes.Length > 0)
                    face.eyes.sprite = f.Eyes[0].front;
            }
        }

        private static bool ReplaceLimbSprite(Limb limb, Dictionary<string, Sprite> dict, bool isRight, int charType, float scale)
        {
            var renderer = limb.GetComponent<SpriteRenderer>();
            if (renderer == null) return false;
            int id = renderer.GetInstanceID();
            if (!_originalLimbSprites.ContainsKey(id))
            {
                var profile = Character.CharacterCatalog.ForCharType(charType);
                Sprite baseline = ResolveLimbStockSprite(profile, limb, renderer.sprite);
                if (baseline != null) _originalLimbSprites[id] = baseline;
            }
            if (renderer.sprite == null && !_originalLimbSprites.TryGetValue(id, out _)) return false;

            Sprite reference = _originalLimbSprites.TryGetValue(id, out var orig) ? orig : renderer.sprite;
            string baseSprite = reference?.name ?? string.Empty;
            string limbName = limb.gameObject.name ?? string.Empty;
            char lastChar = limbName.Length > 0 ? limbName[limbName.Length - 1] : '\0';
            char side = (lastChar == 'F' || lastChar == 'B') ? lastChar : '\0';

            var outcome = Resolve.SkinResolver.ResolveByName(baseSprite, dict, charType, side, isRight);
            renderer.sprite = outcome.Hit ? FitToTarget(outcome.Sprite, reference, scale) : reference;
            return outcome.Hit;
        }

        private static Sprite ResolveLimbStockSprite(Character.CharacterProfile profile, Limb limb, Sprite fallback)
        {
            if (profile != null && limb != null && !string.IsNullOrEmpty(limb.gameObject.name)
                && profile.Limbs.TryGetValue(limb.gameObject.name, out var binding)
                && binding != null && binding.StockSprite != null)
            {
                return binding.StockSprite;
            }
            return fallback;
        }

        private static Sprite ResolveFaceStockSprite(Character.CharacterProfile profile, string partKey, Sprite fallback)
        {
            if (profile != null && partKey != null
                && profile.Faces.TryGetValue(partKey, out var binding)
                && binding != null && binding.StockSprite != null)
            {
                return binding.StockSprite;
            }
            return fallback;
        }

        /// <summary>按统一全局倍率重采样皮肤贴图，保留原图的 pivot 与 pixelsPerUnit。</summary>
        private static Sprite FitToTarget(Sprite skin, Sprite reference, float scale)
        {
            if (skin == null || reference == null) return skin;
            if (!NormalizeSizeEnabled) return skin;
            if (Mathf.Abs(scale - 1f) < 0.001f) return skin;

            int targetW = Mathf.Max(1, Mathf.RoundToInt(reference.rect.width * scale));
            int targetH = Mathf.Max(1, Mathf.RoundToInt(reference.rect.height * scale));

            int srcW = Mathf.RoundToInt(skin.textureRect.width);
            int srcH = Mathf.RoundToInt(skin.textureRect.height);
            if (srcW == targetW && srcH == targetH) return skin;

            long key = ((long)skin.texture.GetInstanceID() << 40) ^ ((long)targetW << 20) ^ (uint)targetH;
            if (_fittedSprites.TryGetValue(key, out var cached) && cached != null) return cached;

            if (_fittedSprites.Count > 128) ClearFittedSprites();

            Texture2D resampled = ResampleRegion(skin.texture, skin.textureRect, targetW, targetH);
            if (resampled == null) return skin;

            var pivot = new Vector2(reference.pivot.x / reference.rect.width,
                                    reference.pivot.y / reference.rect.height);
            var sprite = Sprite.Create(resampled, new Rect(0f, 0f, targetW, targetH), pivot, reference.pixelsPerUnit);
            sprite.name = skin.name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            _fittedSprites[key] = sprite;
            ModLog.Debug($"[归一] {skin.name} 画布 {srcW}x{srcH} -> {targetW}x{targetH}（全局倍率 {scale:F3}）");
            return sprite;
        }

        private static void ClearFittedSprites()
        {
            foreach (var kv in _fittedSprites)
            {
                if (kv.Value != null)
                {
                    if (kv.Value.texture != null) UnityEngine.Object.Destroy(kv.Value.texture);
                    UnityEngine.Object.Destroy(kv.Value);
                }
            }
            _fittedSprites.Clear();
        }

        private static float GlobalFitScale(Dictionary<string, Sprite> dict)
        {
            if (dict == null || dict.Count == 0) return 1f;
            return 1f;
        }

        /// <summary>按所有肢体原图高度之和与皮肤贴图高度之和计算统一缩放倍率。</summary>
        private static float ComputeFitScaleByHeight(Dictionary<string, Sprite> dict)
        {
            if (dict == null || dict.Count == 0) return 1f;

            float wantSum = 0f;
            float gotSum = 0f;
            foreach (var kv in dict)
            {
                if (kv.Value == null) continue;
                string lower = kv.Key.ToLowerInvariant();
                if (lower.Contains("disfigured") || lower.Contains("mouth") || lower.Contains("eye")) continue;
                if (!(lower.Contains("torso") || lower.Contains("arm") || lower.Contains("hand")
                      || lower.Contains("thigh") || lower.Contains("crus") || lower.Contains("foot")
                      || lower.Contains("head") || lower.Contains("tail"))) continue;
                if (!Character.CharacterCatalog.TryGetSpriteSize(kv.Key, out Vector2Int original)) continue;

                wantSum += original.y;
                gotSum += kv.Value.textureRect.height;
            }

            if (gotSum < 1f || wantSum < 1f) return 1f;
            return Mathf.Clamp(wantSum / gotSum, 0.2f, 2f);
        }

        private static Texture2D ResampleRegion(Texture2D src, Rect region, int dstW, int dstH)
        {
            if (src == null || dstW <= 0 || dstH <= 0) return null;
            int srcW = Mathf.RoundToInt(region.width);
            int srcH = Mathf.RoundToInt(region.height);
            if (srcW <= 0 || srcH <= 0) return null;
            int originX = Mathf.RoundToInt(region.x);
            int originY = Mathf.RoundToInt(region.y);

            try
            {
                Color32[] srcPixels = src.GetPixels32();
                if (srcPixels == null) return null;
                int stride = src.width;
                if (srcPixels.Length < stride * src.height) return null;

                var dst = new Texture2D(dstW, dstH, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave,
                };
                var dstPixels = new Color32[dstW * dstH];
                for (int y = 0; y < dstH; y++)
                {
                    int sy = originY + (int)((long)y * srcH / dstH);
                    if (sy >= originY + srcH) sy = originY + srcH - 1;
                    int srcRow = sy * stride;
                    int dstRow = y * dstW;
                    for (int x = 0; x < dstW; x++)
                    {
                        int sx = originX + (int)((long)x * srcW / dstW);
                        if (sx >= originX + srcW) sx = originX + srcW - 1;
                        dstPixels[dstRow + x] = srcPixels[srcRow + sx];
                    }
                }
                dst.SetPixels32(dstPixels);
                dst.Apply(false, false);
                return dst;
            }
            catch (Exception ex)
            {
                ModLog.Warning("重采样皮肤贴图失败: " + ex.Message);
                return null;
            }
        }

        private static void ReplaceTailSprite(TailScript tail, Dictionary<string, Sprite> dict, bool isRight, int charType, float scale)
        {
            var renderer = tail.GetComponent<SpriteRenderer>();
            if (renderer == null) return;
            int id = renderer.GetInstanceID();
            if (!_originalTailSprites.ContainsKey(id))
            {
                var profile = Character.CharacterCatalog.ForCharType(charType);
                Sprite baseline = profile?.Tail?.StockSprite != null ? profile.Tail.StockSprite : renderer.sprite;
                if (baseline != null) _originalTailSprites[id] = baseline;
            }
            Sprite reference = _originalTailSprites.TryGetValue(id, out var orig) ? orig : renderer.sprite;
            if (reference == null) return;

            var outcome = Resolve.SkinResolver.ResolveByName(reference.name, dict, charType, '\0', isRight);
            renderer.sprite = outcome.Hit ? FitToTarget(outcome.Sprite, reference, scale) : reference;
        }

        private static void ReplaceFacialExpressionSprites(FacialExpression face, Dictionary<string, Sprite> dict, bool isRight, int charType, float scale)
        {
            List<Eye> liveEyeList = GetUnderlyingEyeList(face, out Component pauseComp, out bool isPaused);

            int id = face.GetInstanceID();
            if (!_originalFaces.TryGetValue(id, out var orig))
            {
                orig = CaptureFacialBaseline(face, liveEyeList, charType);
                _originalFaces[id] = orig;
            }

            face.defaultHead = GetReplacement(orig.DefaultHead, dict, isRight, charType, scale);
            face.defaultHeadMouth = GetReplacement(orig.DefaultHeadMouth, dict, isRight, charType, scale);
            face.defaultHeadMouthHalf = GetReplacement(orig.DefaultHeadMouthHalf, dict, isRight, charType, scale);
            face.eyesGone = GetReplacement(orig.EyesGone, dict, isRight, charType, scale);
            face.eyesGoneHealed = GetReplacement(orig.EyesGoneHealed, dict, isRight, charType, scale);

            if (face.disfiguredHead != null && orig.DisfiguredHead != null)
            {
                int dhCount = Mathf.Min(face.disfiguredHead.Length, orig.DisfiguredHead.Length);
                for (int i = 0; i < dhCount; i++)
                    face.disfiguredHead[i] = GetReplacement(orig.DisfiguredHead[i], dict, isRight, charType, scale);
            }
            if (face.disfiguredHeadHeal != null && orig.DisfiguredHeadHeal != null)
            {
                int dhhCount = Mathf.Min(face.disfiguredHeadHeal.Length, orig.DisfiguredHeadHeal.Length);
                for (int i = 0; i < dhhCount; i++)
                    face.disfiguredHeadHeal[i] = GetReplacement(orig.DisfiguredHeadHeal[i], dict, isRight, charType, scale);
            }
            if (liveEyeList != null && orig.Eyes != null)
            {
                int eyeCount = Mathf.Min(liveEyeList.Count, orig.Eyes.Length);
                for (int i = 0; i < eyeCount; i++)
                {
                    var eye = orig.Eyes[i];
                    eye.front = GetReplacement(eye.front, dict, isRight, charType, scale);
                    eye.back = GetReplacement(eye.back, dict, isRight, charType, scale);
                    liveEyeList[i] = eye;
                }
            }

            RefreshPausedEyesIfNeeded(pauseComp, isPaused);
        }

        private static FacialOriginal CaptureFacialBaseline(FacialExpression face, List<Eye> liveEyeList, int charType)
        {
            var profile = Character.CharacterCatalog.ForCharType(charType);
            var dh = (Sprite[])(face.disfiguredHead?.Clone() ?? Array.Empty<Sprite>());
            for (int i = 0; i < dh.Length; i++)
                dh[i] = ResolveFaceStockSprite(profile, Character.PartKeyNames.DisfiguredHead(i), dh[i]);

            var dhh = (Sprite[])(face.disfiguredHeadHeal?.Clone() ?? Array.Empty<Sprite>());
            for (int i = 0; i < dhh.Length; i++)
                dhh[i] = ResolveFaceStockSprite(profile, Character.PartKeyNames.DisfiguredHeadHeal(i), dhh[i]);

            var eyes = liveEyeList != null ? liveEyeList.ToArray() : Array.Empty<Eye>();
            for (int i = 0; i < eyes.Length; i++)
            {
                var eye = eyes[i];
                eye.front = ResolveFaceStockSprite(profile, Character.PartKeyNames.EyeFront(i), eye.front);
                eye.back = ResolveFaceStockSprite(profile, Character.PartKeyNames.EyeBack(i), eye.back);
                eyes[i] = eye;
            }

            return new FacialOriginal
            {
                DefaultHead = ResolveFaceStockSprite(profile, Character.PartKeyNames.DefaultHead, face.defaultHead),
                DefaultHeadMouth = ResolveFaceStockSprite(profile, Character.PartKeyNames.DefaultHeadMouth, face.defaultHeadMouth),
                DefaultHeadMouthHalf = ResolveFaceStockSprite(profile, Character.PartKeyNames.DefaultHeadMouthHalf, face.defaultHeadMouthHalf),
                EyesGone = ResolveFaceStockSprite(profile, Character.PartKeyNames.EyesGone, face.eyesGone),
                EyesGoneHealed = ResolveFaceStockSprite(profile, Character.PartKeyNames.EyesGoneHealed, face.eyesGoneHealed),
                DisfiguredHead = dh,
                DisfiguredHeadHeal = dhh,
                Eyes = eyes,
            };
        }

        private static bool NeedsStockRestore(Sprite current, Sprite stock)
        {
            if (stock == null) return false;
            if (current == null) return true;
            if (ReferenceEquals(current, stock)) return false;
            if ((current.hideFlags & HideFlags.HideAndDontSave) != 0) return true;
            return current != stock;
        }

        private static void SanitizeClonedBodyToStock(GameObject playerObj, int charType)
        {
            if (playerObj == null) return;

            foreach (var watcher in playerObj.GetComponentsInChildren<LimbDirWatcher>(true))
            {
                if (watcher == null) continue;
                watcher.enabled = false;
                UnityEngine.Object.Destroy(watcher);
            }

            foreach (var vomit in playerObj.GetComponentsInChildren<BloodVomitWatcher>(true))
            {
                if (vomit == null) continue;
                vomit.MarkDisposed();
                UnityEngine.Object.Destroy(vomit);
            }

            foreach (var tail in playerObj.GetComponentsInChildren<TailScript>(true))
            {
                if (tail == null) continue;
                foreach (var deform in tail.GetComponents<TailFlowDeform>())
                {
                    if (deform == null) continue;
                    deform.MarkDisposed();
                    UnityEngine.Object.Destroy(deform);
                }
                var tailSr = tail.GetComponent<SpriteRenderer>();
                if (tail.enabled && tailSr != null && !tailSr.enabled)
                    tailSr.enabled = true;
            }

            // 清除克隆母版时可能残留且不再受存活 TailFlowDeform 托管的孤立尾巴网格子物体。
            var orphanMeshes = new List<GameObject>();
            foreach (var tf in playerObj.GetComponentsInChildren<Transform>(true))
            {
                if (tf != null && tf.name == "TailDeformMesh")
                    orphanMeshes.Add(tf.gameObject);
            }
            foreach (var go in orphanMeshes)
            {
                if (go == null) continue;
                var mr = go.GetComponent<MeshRenderer>();
                if (mr != null) mr.enabled = false;
                UnityEngine.Object.Destroy(go);
            }

            BloodAttacher.Restore(playerObj);
            AccessoryAttacher.Apply(playerObj, null, null, null);
            DestroyModContainers(playerObj);

            Body body = playerObj.GetComponentInChildren<Body>(true);
            int resolvedType = charType >= 0 ? charType : (body != null ? body.charType : 0);
            var profile = Character.CharacterCatalog.ForCharType(resolvedType);
            if (profile == null) return;

            foreach (var limb in playerObj.GetComponentsInChildren<Limb>(true))
            {
                if (limb == null) continue;
                var sr = limb.GetComponent<SpriteRenderer>();
                if (sr == null) continue;
                Sprite stock = ResolveLimbStockSprite(profile, limb, null);
                if (NeedsStockRestore(sr.sprite, stock))
                    sr.sprite = stock;
                _originalLimbSprites.Remove(sr.GetInstanceID());
            }

            Sprite tailStock = profile.Tail?.StockSprite;
            if (tailStock != null)
            {
                foreach (var tail in playerObj.GetComponentsInChildren<TailScript>(true))
                {
                    if (tail == null) continue;
                    var sr = tail.GetComponent<SpriteRenderer>();
                    if (sr == null) continue;
                    if (NeedsStockRestore(sr.sprite, tailStock))
                        sr.sprite = tailStock;
                    _originalTailSprites.Remove(sr.GetInstanceID());
                }
            }

            foreach (var face in playerObj.GetComponentsInChildren<FacialExpression>(true))
            {
                if (face == null) continue;
                SanitizeFaceToStock(face, profile);
                _originalFaces.Remove(face.GetInstanceID());
            }
        }

        private static void SanitizeFaceToStock(FacialExpression face, Character.CharacterProfile profile)
        {
            bool dirty = false;

            Sprite sHead = ResolveFaceStockSprite(profile, Character.PartKeyNames.DefaultHead, null);
            if (NeedsStockRestore(face.defaultHead, sHead)) { face.defaultHead = sHead; dirty = true; }

            Sprite sMouth = ResolveFaceStockSprite(profile, Character.PartKeyNames.DefaultHeadMouth, null);
            if (NeedsStockRestore(face.defaultHeadMouth, sMouth)) { face.defaultHeadMouth = sMouth; dirty = true; }

            Sprite sHalf = ResolveFaceStockSprite(profile, Character.PartKeyNames.DefaultHeadMouthHalf, null);
            if (NeedsStockRestore(face.defaultHeadMouthHalf, sHalf)) { face.defaultHeadMouthHalf = sHalf; dirty = true; }

            Sprite sGone = ResolveFaceStockSprite(profile, Character.PartKeyNames.EyesGone, null);
            if (NeedsStockRestore(face.eyesGone, sGone)) { face.eyesGone = sGone; dirty = true; }

            Sprite sGoneHeal = ResolveFaceStockSprite(profile, Character.PartKeyNames.EyesGoneHealed, null);
            if (NeedsStockRestore(face.eyesGoneHealed, sGoneHeal)) { face.eyesGoneHealed = sGoneHeal; dirty = true; }

            if (face.disfiguredHead != null)
            {
                for (int i = 0; i < face.disfiguredHead.Length; i++)
                {
                    Sprite s = ResolveFaceStockSprite(profile, Character.PartKeyNames.DisfiguredHead(i), null);
                    if (NeedsStockRestore(face.disfiguredHead[i], s)) { face.disfiguredHead[i] = s; dirty = true; }
                }
            }
            if (face.disfiguredHeadHeal != null)
            {
                for (int i = 0; i < face.disfiguredHeadHeal.Length; i++)
                {
                    Sprite s = ResolveFaceStockSprite(profile, Character.PartKeyNames.DisfiguredHeadHeal(i), null);
                    if (NeedsStockRestore(face.disfiguredHeadHeal[i], s)) { face.disfiguredHeadHeal[i] = s; dirty = true; }
                }
            }

            List<Eye> liveEyes = GetUnderlyingEyeList(face, out Component pauseComp, out bool isPaused);
            if (liveEyes != null)
            {
                for (int i = 0; i < liveEyes.Count; i++)
                {
                    Eye eye = liveEyes[i];
                    bool eyeChanged = false;
                    Sprite sf = ResolveFaceStockSprite(profile, Character.PartKeyNames.EyeFront(i), null);
                    if (NeedsStockRestore(eye.front, sf)) { eye.front = sf; eyeChanged = true; }
                    Sprite sb = ResolveFaceStockSprite(profile, Character.PartKeyNames.EyeBack(i), null);
                    if (NeedsStockRestore(eye.back, sb)) { eye.back = sb; eyeChanged = true; }
                    if (eyeChanged) { liveEyes[i] = eye; dirty = true; }
                }
            }

            if (dirty) RefreshPausedEyesIfNeeded(pauseComp, isPaused);

            if (face.head != null && sHead != null
                && (dirty || (face.head.sprite != null && (face.head.sprite.hideFlags & HideFlags.HideAndDontSave) != 0)))
            {
                face.head.sprite = sHead;
            }
            Sprite sEye0 = ResolveFaceStockSprite(profile, Character.PartKeyNames.EyeFront(0), null);
            if (face.eyes != null && sEye0 != null
                && (dirty || (face.eyes.sprite != null && (face.eyes.sprite.hideFlags & HideFlags.HideAndDontSave) != 0)))
            {
                face.eyes.sprite = sEye0;
            }
        }

        private static void RestoreEyeList(FacialExpression face, Eye[] origEyes)
        {
            if (origEyes == null) return;
            List<Eye> liveEyeList = GetUnderlyingEyeList(face, out Component pauseComp, out bool isPaused);
            if (liveEyeList == null) return;
            int n = Mathf.Min(liveEyeList.Count, origEyes.Length);
            for (int i = 0; i < n; i++) liveEyeList[i] = origEyes[i];
            RefreshPausedEyesIfNeeded(pauseComp, isPaused);
        }

        private static List<Eye> GetUnderlyingEyeList(FacialExpression face, out Component pauseComp, out bool isPaused)
        {
            pauseComp = null;
            isPaused = false;
            if (face == null) return null;

            foreach (var mb in face.GetComponents<MonoBehaviour>())
            {
                if (mb != null && mb.GetType().Name == "FacialExpressionPause")
                {
                    pauseComp = mb;
                    break;
                }
            }
            if (pauseComp == null) return face.eyeList;

            if (!_pauseReflected)
            {
                var t = pauseComp.GetType();
                _pauseOrigEyesField = t.GetField("originalEyeList", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                _pausePausedEyesField = t.GetField("pausedEyeList", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                _pauseLastAliveField = t.GetField("lastAlive", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                _pauseReflected = true;
                if (_pauseOrigEyesField == null || _pausePausedEyesField == null || _pauseLastAliveField == null)
                {
                    ModLog.Warning("FacialExpressionPause 内部字段反射不完整，暂停期间眼部贴图同步可能受影响。");
                }
            }

            var origList = _pauseOrigEyesField?.GetValue(pauseComp) as List<Eye>;
            var pausedList = _pausePausedEyesField?.GetValue(pauseComp) as List<Eye>;
            isPaused = pausedList != null && ReferenceEquals(face.eyeList, pausedList);
            return origList ?? face.eyeList;
        }

        private static void RefreshPausedEyesIfNeeded(Component pauseComp, bool isPaused)
        {
            if (!isPaused || pauseComp == null) return;
            _pausePausedEyesField?.SetValue(pauseComp, null);
            _pauseLastAliveField?.SetValue(pauseComp, true);
        }

        private static void DisableConflictingReplacers(GameObject root)
        {
            if (root == null || !SkinSync.TogetherInstalled || Patches.LegacyTogetherSkinSuppressor.NoLegacyDetected) return;
            foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null || mb.GetType().Name != "SkinSpriteReplacer") continue;
                if (!_replacerReflected)
                {
                    var t = mb.GetType();
                    _replacerRestoreMethod = t.GetMethod("Restore", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                    _replacerActiveProp = t.GetProperty("Active", BindingFlags.Public | BindingFlags.Instance);
                    _replacerReflected = true;
                }
                try
                {
                    bool active = _replacerActiveProp != null && (bool)_replacerActiveProp.GetValue(mb, null);
                    if (active) _replacerRestoreMethod?.Invoke(mb, null);
                }
                catch { }
            }
        }

        private static Sprite GetReplacement(Sprite original, Dictionary<string, Sprite> dict, bool isRight, int charType, float scale)
        {
            if (original == null) return null;
            var outcome = Resolve.SkinResolver.ResolveByName(original.name, dict, charType, '\0', isRight);
            return outcome.Hit ? FitToTarget(outcome.Sprite, original, scale) : original;
        }

        private static void EnsureWingsAttached(GameObject playerObj, Dictionary<string, Sprite> dict, WingsConfigLoader.Config cfg)
        {
            if (playerObj == null) return;
            Body body = playerObj.GetComponentInChildren<Body>(true);
            if (body == null) return;

            Transform upTorsoTf = FindLimbTransform(body, "UpTorso");
            Transform parent = upTorsoTf != null ? upTorsoTf : body.transform;

            Transform existing = parent.Find(WingsContainerName);
            if (existing != null) UnityEngine.Object.Destroy(existing.gameObject);

            Sprite wingUL = dict.TryGetValue("wingUL", out var ul) ? ul : null;
            Sprite wingUR = dict.TryGetValue("wingUR", out var ur) ? ur : null;
            Sprite wingDL = dict.TryGetValue("wingDL", out var dl) ? dl : null;
            Sprite wingDR = dict.TryGetValue("wingDR", out var dr) ? dr : null;
            if (wingUL == null && wingUR == null && wingDL == null && wingDR == null) return;

            var container = new GameObject(WingsContainerName);
            container.transform.SetParent(parent, worldPositionStays: false);
            container.transform.localPosition = Vector3.zero;
            container.transform.localScale = Vector3.one;

            var bodyRenderer = body.GetComponentInChildren<SpriteRenderer>();
            int sortLayer = bodyRenderer != null ? bodyRenderer.sortingLayerID : 0;
            Material litMat = GetSharedCleanLitMaterial(bodyRenderer);

            GameObject upL = null;
            GameObject upR = null;
            float leftUpperHalfPx = wingUL != null ? wingUL.rect.height * 0.5f : 16f;
            float rightUpperHalfPx = wingUR != null ? wingUR.rect.height * 0.5f : 16f;
            if (wingUL != null) upL = AttachWing(container.transform, "WingUL", wingUL, cfg.WingUL, sortLayer, isLeft: true, parentedToUpper: false, upperHalfPx: 0f, litMat: litMat);
            if (wingUR != null) upR = AttachWing(container.transform, "WingUR", wingUR, cfg.WingUR, sortLayer, isLeft: false, parentedToUpper: false, upperHalfPx: 0f, litMat: litMat);
            if (wingDL != null && upL != null) AttachWing(upL.transform, "WingDL", wingDL, cfg.WingDL, sortLayer, isLeft: true, parentedToUpper: true, upperHalfPx: leftUpperHalfPx, litMat: litMat);
            else if (wingDL != null) AttachWing(container.transform, "WingDL", wingDL, cfg.WingDL, sortLayer, isLeft: true, parentedToUpper: false, upperHalfPx: 0f, litMat: litMat);
            if (wingDR != null && upR != null) AttachWing(upR.transform, "WingDR", wingDR, cfg.WingDR, sortLayer, isLeft: false, parentedToUpper: true, upperHalfPx: rightUpperHalfPx, litMat: litMat);
            else if (wingDR != null) AttachWing(container.transform, "WingDR", wingDR, cfg.WingDR, sortLayer, isLeft: false, parentedToUpper: false, upperHalfPx: 0f, litMat: litMat);
        }

        private static Transform FindLimbTransform(Body body, string limbName)
        {
            foreach (var tf in body.GetComponentsInChildren<Transform>(true))
            {
                if (tf.name == limbName) return tf;
                if (tf.name.EndsWith(limbName, StringComparison.OrdinalIgnoreCase)) return tf;
            }
            foreach (var sr in body.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sr != null && sr.sprite != null && sr.sprite.name.Contains(limbName)) return sr.transform;
            }
            return null;
        }

        private static GameObject AttachWing(Transform parent, string name, Sprite sprite, WingsConfigLoader.Piece piece,
            int sortLayer, bool isLeft, bool parentedToUpper, float upperHalfPx, Material litMat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            float localPxX = parentedToUpper ? piece.X : (piece.X - UPTORSO_OFFSET_X);
            float localPxY = parentedToUpper ? piece.Y : (piece.Y - UPTORSO_OFFSET_Y);
            float lx = localPxX / PIXELS_PER_UNIT;
            float ly = -localPxY / PIXELS_PER_UNIT;
            if (parentedToUpper) ly += -upperHalfPx / PIXELS_PER_UNIT;
            go.transform.localPosition = new Vector3(lx, ly, 0f);
            go.transform.localScale = Vector3.one;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingLayerID = sortLayer;
            sr.sortingOrder = piece.ZOrder;
            if (litMat != null) sr.sharedMaterial = litMat;

            var script = go.AddComponent<WingScript>();
            script.isLeft = isLeft;
            script.isLowerWing = parentedToUpper;
            script.baseAngleDeg = -piece.Rotation;
            ShadowAttacher.Ensure(go);
            return go;
        }
    }
}
