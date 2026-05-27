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

        /// <summary>查指定 chara 当前已应用的皮肤名；未应用时返回 null。</summary>
        public static string GetCharacterByChara(GameObject chara)
        {
            if (chara == null) return null;
            return _byChara.TryGetValue(chara, out var name) ? name : null;
        }

        /// <summary>当前已应用的 character 对应 sprite 字典，供 ZoneFrameAnimator 等运行时组件解析帧名用。</summary>
        public static Dictionary<string, Sprite> GetSpriteDict()
        {
            if (_currentCharacter != null && _skinCache.TryGetValue(_currentCharacter, out var d)) return d;
            return null;
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

            if (!_skinCache.TryGetValue(characterName, out var spriteDict))
            {
                spriteDict = LoadCharacterSprites(characterName);
                if (spriteDict == null || spriteDict.Count == 0)
                {
                    Debug.LogWarning($"[SkinSync] No sprites found for character {characterName}");
                    return;
                }
                _skinCache[characterName] = spriteDict;
            }

            if (!_wingsCache.TryGetValue(characterName, out var wingsCfg))
            {
                string wingsJsonPath = Path.Combine(Paths.PluginPath, "CustomSprites", characterName, "wings.json");
                wingsCfg = WingsConfigLoader.Load(wingsJsonPath);
                _wingsCache[characterName] = wingsCfg;
            }

            foreach (var limb in playerObj.GetComponentsInChildren<Limb>(true))
                ReplaceLimbSprite(limb, spriteDict);

            foreach (var tail in playerObj.GetComponentsInChildren<TailScript>(true))
                ReplaceTailSprite(tail, spriteDict);

            foreach (var face in playerObj.GetComponentsInChildren<FacialExpression>(true))
                ReplaceFacialExpressionSprites(face, spriteDict);

            EnsureWingsAttached(playerObj, spriteDict, wingsCfg);

            foreach (var tail in playerObj.GetComponentsInChildren<TailScript>(true))
            {
                if (tail.GetComponent<TailFlowDeform>() == null)
                    tail.gameObject.AddComponent<TailFlowDeform>();
            }

            string accessoriesPath = Path.Combine(Paths.PluginPath, "CustomSprites", characterName, "accessories.json");
            var accEntries = AccessoryConfigLoader.Load(accessoriesPath);
            AccessoryAttacher.Apply(playerObj, accEntries, spriteDict, characterName);

            _currentCharacter = characterName;
            _byChara[playerObj] = characterName;
            ZonesAttacher.Apply(playerObj, characterName);
            BloodAttacher.Apply(playerObj, characterName);
        }

        /// <summary>
        /// 优先按 limb.gameObject.name 末尾 F/B 后缀挑选 <spriteName><F|B> 独立图，缺失时回退共享 sprite 名。
        /// </summary>
        private static void ReplaceLimbSprite(Limb limb, Dictionary<string, Sprite> dict)
        {
            var renderer = limb.GetComponent<SpriteRenderer>();
            if (renderer?.sprite == null) return;
            string baseSprite = renderer.sprite.name;
            string limbName = limb.gameObject.name ?? string.Empty;
            char lastChar = limbName.Length > 0 ? limbName[limbName.Length - 1] : '\0';
            if ((lastChar == 'F' || lastChar == 'B')
                && dict.TryGetValue(baseSprite + lastChar, out var sided))
            {
                renderer.sprite = sided;
                return;
            }
            if (dict.TryGetValue(baseSprite, out var shared))
                renderer.sprite = shared;
        }

        private static Dictionary<string, Sprite> LoadCharacterSprites(string character)
        {
            var dict = new Dictionary<string, Sprite>();
            string basePath = Path.Combine(Paths.PluginPath, "CustomSprites", character);
            if (!Directory.Exists(basePath)) return dict;
            foreach (var filePath in Directory.GetFiles(basePath, "*.png", SearchOption.AllDirectories))
            {
                string fileName = Path.GetFileNameWithoutExtension(filePath);
                if (dict.ContainsKey(fileName)) continue;
                byte[] data = File.ReadAllBytes(filePath);
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.filterMode = FilterMode.Point;
                if (ImageConversion.LoadImage(tex, data))
                {
                    Sprite sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), PIXELS_PER_UNIT);
                    sprite.name = fileName;
                    dict[fileName] = sprite;
                }
            }
            return dict;
        }

        private static void ReplaceTailSprite(TailScript tail, Dictionary<string, Sprite> dict)
        {
            var renderer = tail.GetComponent<SpriteRenderer>();
            if (renderer?.sprite != null && dict.TryGetValue(renderer.sprite.name, out var newSprite))
                renderer.sprite = newSprite;
        }

        private static void ReplaceFacialExpressionSprites(FacialExpression face, Dictionary<string, Sprite> dict)
        {
            face.defaultHead = GetReplacement(face.defaultHead, dict);
            face.defaultHeadMouth = GetReplacement(face.defaultHeadMouth, dict);
            face.defaultHeadMouthHalf = GetReplacement(face.defaultHeadMouthHalf, dict);
            face.eyesGone = GetReplacement(face.eyesGone, dict);
            face.eyesGoneHealed = GetReplacement(face.eyesGoneHealed, dict);

            for (int i = 0; i < face.disfiguredHead.Length; i++)
                face.disfiguredHead[i] = GetReplacement(face.disfiguredHead[i], dict);
            for (int i = 0; i < face.disfiguredHeadHeal.Length; i++)
                face.disfiguredHeadHeal[i] = GetReplacement(face.disfiguredHeadHeal[i], dict);
            for (int i = 0; i < face.eyeList.Count; i++)
            {
                var eye = face.eyeList[i];
                eye.front = GetReplacement(eye.front, dict);
                eye.back = GetReplacement(eye.back, dict);
                face.eyeList[i] = eye;
            }
        }

        private static Sprite GetReplacement(Sprite original, Dictionary<string, Sprite> dict)
        {
            if (original == null) return null;
            return dict.TryGetValue(original.name, out var replacement) ? replacement : original;
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
