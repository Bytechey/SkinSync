using BepInEx;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>
    /// 将本地 PNG 与 wings.json 配置应用到玩家 GameObject 的所有 limb / 表情 / 尾巴 / 翅膀。
    /// </summary>
    public static class SkinApplier
    {
        private static Dictionary<string, Dictionary<string, Sprite>> _skinCache = new Dictionary<string, Dictionary<string, Sprite>>();
        private static Dictionary<string, WingsConfigLoader.Config> _wingsCache = new Dictionary<string, WingsConfigLoader.Config>();
        private static string _currentCharacter = null;
        // 多人模式下每个 chara 当前应用的皮肤名——给 BloodPatches 等按 chara 反查 character。
        private static readonly Dictionary<GameObject, string> _byChara = new Dictionary<GameObject, string>();
        private static readonly HashSet<string> _requestedPacks = new HashSet<string>();
        private static readonly Dictionary<int, Sprite> _originalLimbSprites = new Dictionary<int, Sprite>();
        private static readonly Dictionary<int, Sprite> _originalTailSprites = new Dictionary<int, Sprite>();
        private static readonly Dictionary<int, FacialOriginal> _originalFaces = new Dictionary<int, FacialOriginal>();

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

        /// <summary>查指定 chara 当前已应用的皮肤名；未应用时返回 null。</summary>
        public static string GetCharacterByChara(GameObject chara)
        {
            if (chara == null) return null;
            return _byChara.TryGetValue(chara, out var name) ? name : null;
        }

        /// <summary>按 world 坐标找半径内最近 Body 对应的 character；用于落地血迹 / 爆血粒子按 owner 染色。</summary>
        public static bool TryGetCharacterByPosition(Vector2 pos, float radius, out string character)
        {
            character = null;
            float bestDist = radius;
            foreach (var body in UnityEngine.Object.FindObjectsOfType<Body>())
            {
                if (body == null) continue;
                float d = Vector2.Distance(pos, body.transform.position);
                if (d > bestDist) continue;
                GameObject host = body.transform.parent != null ? body.transform.parent.gameObject : body.gameObject;
                if (_byChara.TryGetValue(host, out var name))
                {
                    bestDist = d;
                    character = name;
                }
            }
            return character != null;
        }

        /// <summary>已应用 character 数=1 时直接返回该 character；用作 world 血迹 / 粒子无 owner 链路时的兜底。</summary>
        public static bool TryGetAnyAppliedCharacter(out string character)
        {
            character = null;
            string only = null;
            foreach (var kv in _byChara)
            {
                if (string.IsNullOrEmpty(kv.Value)) continue;
                if (only == null) { only = kv.Value; continue; }
                if (only != kv.Value) return false;
            }
            if (only == null) return false;
            character = only;
            return true;
        }

        /// <summary>当前已应用的 character 对应 sprite 字典，供 ZoneFrameAnimator 等运行时组件解析帧名用。</summary>
        public static Dictionary<string, Sprite> GetSpriteDict()
        {
            if (_currentCharacter != null && _skinCache.TryGetValue(_currentCharacter, out var d)) return d;
            return null;
        }

        private static System.Action<GameObject, bool> _skinApplied;

        /// <summary>注册皮肤应用回调；每次某玩家应用皮肤后回调 (playerObj, 是否含翅膀)。供 ExtraLimbs 等外部 mod 反射订阅。</summary>
        public static void RegisterSkinAppliedListener(System.Action<GameObject, bool> cb)
        {
            if (cb == null) return;
            _skinApplied -= cb;
            _skinApplied += cb;
        }

        public static void UnregisterSkinAppliedListener(System.Action<GameObject, bool> cb)
        {
            if (cb == null) return;
            _skinApplied -= cb;
        }

        /// <summary>判定玩家当前是否挂有翅膀（SkinSync 的 HwWings 容器存在）。</summary>
        public static bool HasWings(GameObject playerObj)
        {
            if (playerObj == null) return false;
            Body body = playerObj.GetComponentInChildren<Body>(true);
            if (body == null) return false;
            foreach (var tf in body.GetComponentsInChildren<Transform>(true))
                if (tf != null && tf.name == "HwWings") return true;
            return false;
        }

        private const float PIXELS_PER_UNIT = 8f;
        private const float UPTORSO_OFFSET_X = 6f;
        private const float UPTORSO_OFFSET_Y = -10f;

        /// <summary>
        /// 入口：刷新 sprite、表情、翅膀挂载，并确保 TailFlowDeform 已附着。
        /// </summary>
        public static void ApplySkinToPlayer(GameObject playerObj, string characterName)
        {
            if (playerObj == null) return;
            if (string.IsNullOrEmpty(characterName)) return;

            bool sameSkin = _byChara.TryGetValue(playerObj, out var current) && current == characterName;

            if (!_skinCache.TryGetValue(characterName, out var spriteDict))
            {
                spriteDict = LoadCharacterSprites(characterName);
                if (spriteDict == null || spriteDict.Count == 0)
                {
                    _byChara[playerObj] = characterName;
                    return;
                }
                _skinCache[characterName] = spriteDict;
            }

            if (!_wingsCache.TryGetValue(characterName, out var wingsCfg))
            {
                string wingsJsonPath = Path.Combine(SkinPathResolver.GetSkinDir(characterName), "wings.json");
                wingsCfg = WingsConfigLoader.Load(wingsJsonPath);
                _wingsCache[characterName] = wingsCfg;
            }

            if (!sameSkin)
            {
                Body bodyForDir = playerObj.GetComponentInChildren<Body>(true);
                bool isRight = bodyForDir != null ? bodyForDir.isRight : true;

                foreach (var limb in playerObj.GetComponentsInChildren<Limb>(true))
                    ReplaceLimbSprite(limb, spriteDict, isRight);

                foreach (var tail in playerObj.GetComponentsInChildren<TailScript>(true))
                    ReplaceTailSprite(tail, spriteDict, isRight);

                foreach (var face in playerObj.GetComponentsInChildren<FacialExpression>(true))
                    ReplaceFacialExpressionSprites(face, spriteDict, isRight);

                EnsureWingsAttached(playerObj, spriteDict, wingsCfg);

                foreach (var tail in playerObj.GetComponentsInChildren<TailScript>(true))
                {
                    if (tail.GetComponent<TailFlowDeform>() == null)
                        tail.gameObject.AddComponent<TailFlowDeform>();
                }
            }

            string accessoriesPath = Path.Combine(SkinPathResolver.GetSkinDir(characterName), "accessories.json");
            var accEntries = AccessoryConfigLoader.Load(accessoriesPath);
            AccessoryAttacher.Apply(playerObj, accEntries, spriteDict, characterName);

            _currentCharacter = characterName;
            _byChara[playerObj] = characterName;
            if (!sameSkin)
            {
                ZonesAttacher.Apply(playerObj, characterName);
                BloodAttacher.Apply(playerObj, characterName);
            }
            EnsureLimbDirWatcher(playerObj);
            _skinApplied?.Invoke(playerObj, HasWings(playerObj));
        }

        /// <summary>朝向变化时调：仅重选 SpriteRenderer.sprite，不重建 GameObject、不重跑 wings/zones/blood/accessories 挂载。</summary>
        public static void RefreshSidedSprites(GameObject playerObj, bool isRight)
        {
            if (playerObj == null) return;
            if (!_byChara.TryGetValue(playerObj, out var character)) return;
            if (!_skinCache.TryGetValue(character, out var spriteDict)) return;

            foreach (var limb in playerObj.GetComponentsInChildren<Limb>(true))
                ReplaceLimbSprite(limb, spriteDict, isRight);
            foreach (var tail in playerObj.GetComponentsInChildren<TailScript>(true))
                ReplaceTailSprite(tail, spriteDict, isRight);
            foreach (var face in playerObj.GetComponentsInChildren<FacialExpression>(true))
                ReplaceFacialExpressionSprites(face, spriteDict, isRight);

            AccessoryAttacher.RefreshSidedSprites(playerObj, spriteDict, isRight);
        }

        private static void EnsureLimbDirWatcher(GameObject playerObj)
        {
            if (playerObj == null) return;
            if (playerObj.GetComponent<LimbDirWatcher>() == null)
                playerObj.AddComponent<LimbDirWatcher>();
        }

        /// <summary>遍历当前场景所有 Body，对每个挂载点调用 ApplySkinToPlayer；用于单机模式无固定 localPlayerObject 时兜底应用皮肤。</summary>
        public static int ApplyToScene(string characterName)
        {
            if (string.IsNullOrEmpty(characterName)) return 0;
            int count = 0;
            foreach (var body in UnityEngine.Object.FindObjectsOfType<Body>())
            {
                if (body == null) continue;
                GameObject host = body.transform.parent != null ? body.transform.parent.gameObject : body.gameObject;
                ApplySkinToPlayer(host, characterName);
                count++;
            }
            return count;
        }

        /// <summary>统一 sprite 选择：朝右优先 R_ 套；候选键序覆盖 base+side 与 base 两层 + 反向 side 兜底；无命中返回 null。</summary>
        internal static Sprite ResolveSidedSprite(Dictionary<string, Sprite> dict, string baseName, char side, bool isRight)
        {
            if (dict == null || string.IsNullOrEmpty(baseName)) return null;
            bool hasSide = side == 'F' || side == 'B';
            if (isRight)
            {
                if (hasSide && dict.TryGetValue("R_" + baseName + side, out var a)) return a;
                if (dict.TryGetValue("R_" + baseName, out var b)) return b;
            }
            if (hasSide && dict.TryGetValue(baseName + side, out var c)) return c;
            if (dict.TryGetValue(baseName, out var d)) return d;
            if (hasSide)
            {
                char opp = side == 'F' ? 'B' : 'F';
                if (isRight && dict.TryGetValue("R_" + baseName + opp, out var e)) return e;
                if (dict.TryGetValue(baseName + opp, out var f)) return f;
            }
            return null;
        }

        private static void ReplaceLimbSprite(Limb limb, Dictionary<string, Sprite> dict, bool isRight)
        {
            var renderer = limb.GetComponent<SpriteRenderer>();
            if (renderer == null) return;
            int id = renderer.GetInstanceID();
            if (!_originalLimbSprites.ContainsKey(id) && renderer.sprite != null)
                _originalLimbSprites[id] = renderer.sprite;
            if (renderer.sprite == null && !_originalLimbSprites.TryGetValue(id, out _)) return;

            Sprite reference = _originalLimbSprites.TryGetValue(id, out var orig) ? orig : renderer.sprite;
            string baseSprite = reference?.name ?? string.Empty;
            string limbName = limb.gameObject.name ?? string.Empty;
            char lastChar = limbName.Length > 0 ? limbName[limbName.Length - 1] : '\0';
            char side = (lastChar == 'F' || lastChar == 'B') ? lastChar : '\0';

            var resolved = ResolveSidedSprite(dict, baseSprite, side, isRight);
            renderer.sprite = resolved ?? reference;
        }

        private static Dictionary<string, Sprite> LoadCharacterSprites(string character)
        {
            var dict = new Dictionary<string, Sprite>();
            string basePath = SkinPathResolver.GetSkinDir(character);
            if (!Directory.Exists(basePath))
            {
                if (TryLoadFromMemoryPack(character, dict)) return dict;
                if (TryRequestSkinPack(character))
                    SkinSyncMod.ModLog.Info($"本机缺少皮肤 {character}，已向其他玩家请求皮肤包");
                else
                    SkinSyncMod.ModLog.Info($"本机缺少皮肤目录 {character}（{basePath}）；请将该皮肤包放入 plugins/CustomSprites/{character}");
                return dict;
            }
            var baseSizes = BaseSizesLoader.Load(character);
            foreach (var filePath in Directory.GetFiles(basePath, "*.png", SearchOption.AllDirectories))
            {
                string fileName = Path.GetFileNameWithoutExtension(filePath);
                if (dict.ContainsKey(fileName)) continue;
                BuildSpriteInto(dict, fileName, File.ReadAllBytes(filePath), baseSizes);
            }
            return dict;
        }

        private static void BuildSpriteInto(Dictionary<string, Sprite> dict, string fileName, byte[] data, Dictionary<string, Vector2Int> baseSizes)
        {
            if (dict.ContainsKey(fileName)) return;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            if (!ImageConversion.LoadImage(tex, data)) return;
            float ppu = PIXELS_PER_UNIT;
            if (baseSizes != null && baseSizes.TryGetValue(fileName, out var baseSize) && baseSize.x > 0)
                ppu = PIXELS_PER_UNIT * (tex.width / (float)baseSize.x);
            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), ppu);
            sprite.name = fileName;
            dict[fileName] = sprite;
        }

        /// <summary>缺磁盘目录时，从 SkinPackCodec 内存缓存（实验性皮肤包同步）构建 sprite 字典；无缓存返回 false。</summary>
        private static bool TryLoadFromMemoryPack(string character, Dictionary<string, Sprite> dict)
        {
            var pack = SkinPackCodec.GetInMemory(character);
            if (pack == null) return false;
            Dictionary<string, Vector2Int> baseSizes = null;
            foreach (var kv in pack)
                if (kv.Key.EndsWith("baseSizes.json", System.StringComparison.OrdinalIgnoreCase))
                    baseSizes = BaseSizesLoader.ParseContent(System.Text.Encoding.UTF8.GetString(kv.Value));
            foreach (var kv in pack)
            {
                if (!kv.Key.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase)) continue;
                BuildSpriteInto(dict, Path.GetFileNameWithoutExtension(kv.Key), kv.Value, baseSizes);
            }
            return dict.Count > 0;
        }

        /// <summary>开启实验性同步且处于多人会话时，向其他玩家请求该皮肤包；每个皮肤只请求一次。</summary>
        private static bool TryRequestSkinPack(string character)
        {
            if (SkinSync.Settings == null || !SkinSync.Settings.EnableSkinPackSync.Value) return false;
            if (!Network.KrokoshaBridge.IsAvailable || !Network.KrokoshaBridge.IsNetworkRunning()) return false;
            if (!_requestedPacks.Add(character)) return false;
            Network.MultiplayerSender.SendSkinPackRequest(character);
            return true;
        }

        /// <summary>皮肤包到达后清该皮肤缓存，对正在使用它的玩家重新应用。</summary>
        public static void ReapplyForSkin(string skinID)
        {
            if (string.IsNullOrEmpty(skinID)) return;
            _skinCache.Remove(skinID);
            _requestedPacks.Remove(skinID);
            var targets = new List<GameObject>();
            foreach (var kv in _byChara)
                if (kv.Value == skinID && kv.Key != null) targets.Add(kv.Key);
            foreach (var go in targets)
            {
                _byChara.Remove(go);
                ApplySkinToPlayer(go, skinID);
            }
        }

        private static void ReplaceTailSprite(TailScript tail, Dictionary<string, Sprite> dict, bool isRight)
        {
            var renderer = tail.GetComponent<SpriteRenderer>();
            if (renderer == null) return;
            int id = renderer.GetInstanceID();
            if (!_originalTailSprites.ContainsKey(id) && renderer.sprite != null)
                _originalTailSprites[id] = renderer.sprite;
            Sprite reference = _originalTailSprites.TryGetValue(id, out var orig) ? orig : renderer.sprite;
            if (reference == null) return;
            renderer.sprite = ResolveSidedSprite(dict, reference.name, '\0', isRight) ?? reference;
        }

        private static void ReplaceFacialExpressionSprites(FacialExpression face, Dictionary<string, Sprite> dict, bool isRight)
        {
            int id = face.GetInstanceID();
            if (!_originalFaces.TryGetValue(id, out var orig))
            {
                orig = new FacialOriginal
                {
                    DefaultHead = face.defaultHead,
                    DefaultHeadMouth = face.defaultHeadMouth,
                    DefaultHeadMouthHalf = face.defaultHeadMouthHalf,
                    EyesGone = face.eyesGone,
                    EyesGoneHealed = face.eyesGoneHealed,
                    DisfiguredHead = (Sprite[])(face.disfiguredHead?.Clone() ?? new Sprite[0]),
                    DisfiguredHeadHeal = (Sprite[])(face.disfiguredHeadHeal?.Clone() ?? new Sprite[0]),
                    Eyes = face.eyeList != null ? face.eyeList.ToArray() : new Eye[0],
                };
                _originalFaces[id] = orig;
            }

            face.defaultHead = GetReplacement(orig.DefaultHead, dict, isRight);
            face.defaultHeadMouth = GetReplacement(orig.DefaultHeadMouth, dict, isRight);
            face.defaultHeadMouthHalf = GetReplacement(orig.DefaultHeadMouthHalf, dict, isRight);
            face.eyesGone = GetReplacement(orig.EyesGone, dict, isRight);
            face.eyesGoneHealed = GetReplacement(orig.EyesGoneHealed, dict, isRight);

            int dhCount = Mathf.Min(face.disfiguredHead.Length, orig.DisfiguredHead.Length);
            for (int i = 0; i < dhCount; i++)
                face.disfiguredHead[i] = GetReplacement(orig.DisfiguredHead[i], dict, isRight);
            int dhhCount = Mathf.Min(face.disfiguredHeadHeal.Length, orig.DisfiguredHeadHeal.Length);
            for (int i = 0; i < dhhCount; i++)
                face.disfiguredHeadHeal[i] = GetReplacement(orig.DisfiguredHeadHeal[i], dict, isRight);
            int eyeCount = Mathf.Min(face.eyeList.Count, orig.Eyes.Length);
            for (int i = 0; i < eyeCount; i++)
            {
                var eye = orig.Eyes[i];
                eye.front = GetReplacement(eye.front, dict, isRight);
                eye.back = GetReplacement(eye.back, dict, isRight);
                face.eyeList[i] = eye;
            }
        }

        private static Sprite GetReplacement(Sprite original, Dictionary<string, Sprite> dict, bool isRight)
        {
            if (original == null) return null;
            return ResolveSidedSprite(dict, original.name, '\0', isRight) ?? original;
        }

        /// <summary>
        /// 将 4 张翼按 wings.json 配置挂载到 UpTorso 子节点，下翼以上翼为父形成关节链。
        /// </summary>
        private static void EnsureWingsAttached(GameObject playerObj, Dictionary<string, Sprite> dict, WingsConfigLoader.Config cfg)
        {
            if (playerObj == null) return;
            Body body = playerObj.GetComponentInChildren<Body>(true);
            if (body == null) return;

            Transform upTorsoTf = FindLimbTransform(body, "UpTorso");
            Transform parent = upTorsoTf != null ? upTorsoTf : body.transform;

            Transform existing = parent.Find("HwWings");
            if (existing != null) UnityEngine.Object.Destroy(existing.gameObject);

            Sprite wingUL = dict.TryGetValue("wingUL", out var ul) ? ul : null;
            Sprite wingUR = dict.TryGetValue("wingUR", out var ur) ? ur : null;
            Sprite wingDL = dict.TryGetValue("wingDL", out var dl) ? dl : null;
            Sprite wingDR = dict.TryGetValue("wingDR", out var dr) ? dr : null;
            if (wingUL == null && wingUR == null && wingDL == null && wingDR == null) return;

            var container = new GameObject("HwWings");
            container.transform.SetParent(parent, worldPositionStays: false);
            container.transform.localPosition = Vector3.zero;
            container.transform.localScale = Vector3.one;

            var bodyRenderer = body.GetComponentInChildren<SpriteRenderer>();
            int sortLayer = bodyRenderer != null ? bodyRenderer.sortingLayerID : 0;
            Material litMat = bodyRenderer != null ? bodyRenderer.sharedMaterial : null;

            GameObject upL = null;
            GameObject upR = null;
            float leftUpperHalfPx = wingUL != null ? wingUL.rect.height * 0.5f : 16f;
            float rightUpperHalfPx = wingUR != null ? wingUR.rect.height * 0.5f : 16f;
            if (wingUL != null) upL = AttachWing(container.transform, "WingUL", wingUL, cfg.WingUL, sortLayer, isLeft: true, litMat: litMat);
            if (wingUR != null) upR = AttachWing(container.transform, "WingUR", wingUR, cfg.WingUR, sortLayer, isLeft: false, litMat: litMat);
            if (wingDL != null && upL != null) AttachWing(upL.transform, "WingDL", wingDL, cfg.WingDL, sortLayer, isLeft: true, isLower: true, upperHalfPx: leftUpperHalfPx, litMat: litMat);
            else if (wingDL != null) AttachWing(container.transform, "WingDL", wingDL, cfg.WingDL, sortLayer, isLeft: true, isLower: true, upperHalfPx: leftUpperHalfPx, litMat: litMat);
            if (wingDR != null && upR != null) AttachWing(upR.transform, "WingDR", wingDR, cfg.WingDR, sortLayer, isLeft: false, isLower: true, upperHalfPx: rightUpperHalfPx, litMat: litMat);
            else if (wingDR != null) AttachWing(container.transform, "WingDR", wingDR, cfg.WingDR, sortLayer, isLeft: false, isLower: true, upperHalfPx: rightUpperHalfPx, litMat: litMat);
        }

        private static Transform FindLimbTransform(Body body, string limbName)
        {
            foreach (var tf in body.GetComponentsInChildren<Transform>(true))
            {
                if (tf.name == limbName) return tf;
                if (tf.name.EndsWith(limbName, System.StringComparison.OrdinalIgnoreCase)) return tf;
            }
            foreach (var sr in body.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sr != null && sr.sprite != null && sr.sprite.name.Contains(limbName)) return sr.transform;
            }
            return null;
        }

        private static GameObject AttachWing(Transform parent, string name, Sprite sprite, WingsConfigLoader.Piece piece,
            int sortLayer, bool isLeft, bool isLower = false, float upperHalfPx = 16f, Material litMat = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            float localPxX = isLower ? piece.X : (piece.X - UPTORSO_OFFSET_X);
            float localPxY = isLower ? piece.Y : (piece.Y - UPTORSO_OFFSET_Y);
            float lx = localPxX / PIXELS_PER_UNIT;
            float ly = -localPxY / PIXELS_PER_UNIT;
            if (isLower) ly += -upperHalfPx / PIXELS_PER_UNIT;
            go.transform.localPosition = new Vector3(lx, ly, 0f);
            go.transform.localScale = Vector3.one;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingLayerID = sortLayer;
            sr.sortingOrder = piece.ZOrder;
            if (litMat != null) sr.sharedMaterial = litMat;

            var script = go.AddComponent<WingScript>();
            script.isLeft = isLeft;
            script.baseAngleDeg = -piece.Rotation;
            ShadowAttacher.Ensure(go);
            return go;
        }
    }
}
