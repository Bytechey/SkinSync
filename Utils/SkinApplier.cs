using BepInEx;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SkinSyncMod
{
    public static class SkinApplier
    {
        private static Dictionary<string, Dictionary<string, Sprite>> _skinCache = new Dictionary<string, Dictionary<string, Sprite>>();

        public static void ApplySkinToPlayer(GameObject playerObj, string characterName)
        {
            if (playerObj == null) return;

            // 加载或获取缓存的皮肤字典
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

            // 替换所有 Limb
            foreach (var limb in playerObj.GetComponentsInChildren<Limb>(true))
                ReplaceLimbSprite(limb, spriteDict);

            // 替换所有 TailScript
            foreach (var tail in playerObj.GetComponentsInChildren<TailScript>(true))
                ReplaceTailSprite(tail, spriteDict);

            // 替换 FacialExpression
            foreach (var face in playerObj.GetComponentsInChildren<FacialExpression>(true))
                ReplaceFacialExpressionSprites(face, spriteDict);
        }

        private static Dictionary<string, Sprite> LoadCharacterSprites(string character)
        {
            var dict = new Dictionary<string, Sprite>();
            string basePath = Path.Combine(Paths.PluginPath, "CustomSprites", character);

            LoadSpritesFromFolder(Path.Combine(basePath, "Body"), dict);
            LoadSpritesFromFolder(Path.Combine(basePath, "Head"), dict);
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
                    Sprite sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 8f);
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
    }
}