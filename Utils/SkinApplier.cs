using BepInEx;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SkinSyncMod
{
    public static class SkinApplier
    {
        private static Dictionary<string, Dictionary<string, Sprite>> _skinCache = new Dictionary<string, Dictionary<string, Sprite>>();
        private static Dictionary<string, WingsConfigLoader.Config> _wingsCache = new Dictionary<string, WingsConfigLoader.Config>();

        // 编辑器画布 100×100，假定 8 像素 = 1 Unity 单位（与 Sprite.Create pixelsPerUnit 一致）。
        private const float PIXELS_PER_UNIT = 8f;

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
        }

        private static Dictionary<string, Sprite> LoadCharacterSprites(string character)
        {
            var dict = new Dictionary<string, Sprite>();
            string basePath = Path.Combine(Paths.PluginPath, "CustomSprites", character);
            LoadSpritesFromFolder(Path.Combine(basePath, "Body"), dict);
            LoadSpritesFromFolder(Path.Combine(basePath, "Head"), dict);
            LoadSpritesFromFolder(Path.Combine(basePath, "Wings"), dict);
            return dict;
        }

        private static void LoadSpritesFromFolder(string folderPath, Dictionary<string, Sprite> dict)
        {
            if (!Directory.Exists(folderPath)) return;
            foreach (var filePath in Directory.GetFiles(folderPath, "*.png"))
            {
                string fileName = Path.GetFileNameWithoutExtension(filePath);
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
        }

        private static void ReplaceLimbSprite(Limb limb, Dictionary<string, Sprite> dict)
        {
            var renderer = limb.GetComponent<SpriteRenderer>();
            if (renderer?.sprite != null && dict.TryGetValue(renderer.sprite.name, out var newSprite))
                renderer.sprite = newSprite;
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
        /// 4 张翼按 wings.json 配置独立挂载到 UpTorso limb 子节点下，让翼始终跟随上躯干（下蹲/躺下/弯腰都跟）。
        /// 编辑器 wings.json 的 (x, y) 是相对画布中心，UpTorso 在装配表中默认偏移 (6, -10)，
        /// 故挂载时坐标减去这一偏移即得"相对 UpTorso 中心"的局部坐标。
        /// 编辑器像素 ÷ pixelsPerUnit(8) 转 Unity 单位；编辑器 Y 向下、Unity Y 向上故取负。
        /// </summary>
        private static void EnsureWingsAttached(GameObject playerObj, Dictionary<string, Sprite> dict, WingsConfigLoader.Config cfg)
        {
            if (playerObj == null) return;
            Body body = playerObj.GetComponentInChildren<Body>(true);
            if (body == null) return;

            // UpTorso limb 是翼根的真正父节点：弯腰 / 下蹲 / 躺下时它的 transform 跟着动，翼自然跟随。
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

            // 上翼挂在 container 下；下翼挂在对应上翼下，实现父子关节链。
            GameObject upL = null;
            GameObject upR = null;
            if (wingUL != null) upL = AttachWing(container.transform, "WingUL", wingUL, cfg.WingUL, sortLayer, isLeft: true);
            if (wingUR != null) upR = AttachWing(container.transform, "WingUR", wingUR, cfg.WingUR, sortLayer, isLeft: false);
            if (wingDL != null && upL != null) AttachWing(upL.transform, "WingDL", wingDL, cfg.WingDL, sortLayer, isLeft: true, isLower: true);
            else if (wingDL != null) AttachWing(container.transform, "WingDL", wingDL, cfg.WingDL, sortLayer, isLeft: true, isLower: true);
            if (wingDR != null && upR != null) AttachWing(upR.transform, "WingDR", wingDR, cfg.WingDR, sortLayer, isLeft: false, isLower: true);
            else if (wingDR != null) AttachWing(container.transform, "WingDR", wingDR, cfg.WingDR, sortLayer, isLeft: false, isLower: true);
        }

        // 在 body 子树里找 limb：先匹配 GameObject.name，再 fallback 匹配 SpriteRenderer.sprite.name 含 limbName。
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

        // assemblyTable.rs 里 experimentUpTorso 默认偏移 (6, -10)（相对画布中心）。
        // wings.json 坐标也是相对画布中心，故挂到 UpTorso 子节点时减去此偏移得到相对 UpTorso 的局部偏移。
        private const float UPTORSO_OFFSET_X = 6f;
        private const float UPTORSO_OFFSET_Y = -10f;

        private static GameObject AttachWing(Transform parent, string name, Sprite sprite, WingsConfigLoader.Piece piece,
            int sortLayer, bool isLeft, bool isLower = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            // (画布坐标 - UpTorso 偏移) → UpTorso 局部像素 → ÷ pixelsPerUnit(8) → Unity 单位；Y 取负。
            // 下翼挂在上翼子节点下，parent 已是上翼自身——下翼坐标是相对上翼根的偏移，不再减 UpTorso。
            float localPxX = isLower ? piece.X : (piece.X - UPTORSO_OFFSET_X);
            float localPxY = isLower ? piece.Y : (piece.Y - UPTORSO_OFFSET_Y);
            float lx = localPxX / PIXELS_PER_UNIT;
            float ly = -localPxY / PIXELS_PER_UNIT;
            // 下翼默认挂在上翼"末端"基准（PNG 半高 16px = 2 单位，向下）。
            if (isLower) ly += -16f / PIXELS_PER_UNIT;
            go.transform.localPosition = new Vector3(lx, ly, 0f);
            go.transform.localScale = Vector3.one;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingLayerID = sortLayer;
            sr.sortingOrder = piece.ZOrder;

            var script = go.AddComponent<WingScript>();
            script.isLeft = isLeft;
            // 编辑器 rotation 是屏幕坐标系顺时针为正（与 Unity Z 旋转相反），取负转 Unity 语义。
            script.baseAngleDeg = -piece.Rotation;
            return go;
        }
    }
}
