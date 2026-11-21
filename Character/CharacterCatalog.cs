using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SkinSyncMod.Character
{
    /// <summary>从 Resources 采集所有角色预制体的原生部位贴图与像素尺寸。</summary>
    internal static class CharacterCatalog
    {
        private const string RootPath = "Special/Characters";

        private static List<CharacterProfile> _profiles;
        private static Dictionary<int, CharacterProfile> _byCharType;
        private static Dictionary<string, Vector2Int> _spriteSizes;
        private static Dictionary<string, KeyValuePair<string, int>> _spriteOwners;

        internal static IReadOnlyList<CharacterProfile> All
        {
            get
            {
                EnsureLoaded();
                return _profiles ?? (IReadOnlyList<CharacterProfile>)Array.Empty<CharacterProfile>();
            }
        }

        /// <summary>按 charType 查询角色档案，未知类型返回 null。</summary>
        internal static CharacterProfile ForCharType(int charType)
        {
            EnsureLoaded();
            return _byCharType != null && _byCharType.TryGetValue(charType, out var profile) ? profile : null;
        }

        /// <summary>查询贴图在游戏预制体中的原生像素尺寸。</summary>
        internal static bool TryGetSpriteSize(string spriteName, out Vector2Int size)
        {
            EnsureLoaded();
            size = default;
            return !string.IsNullOrEmpty(spriteName) && _spriteSizes != null && _spriteSizes.TryGetValue(spriteName, out size);
        }

        /// <summary>加载并缓存全部角色档案。</summary>
        internal static void EnsureLoaded()
        {
            if (_profiles != null) return;

            var profiles = new List<CharacterProfile>();
            var byCharType = new Dictionary<int, CharacterProfile>();
            var spriteSizes = new Dictionary<string, Vector2Int>(StringComparer.Ordinal);
            var spriteOwners = new Dictionary<string, KeyValuePair<string, int>>(StringComparer.Ordinal);

            GameObject[] prefabs;
            try
            {
                prefabs = Resources.LoadAll<GameObject>(RootPath);
            }
            catch (Exception ex)
            {
                SkinSyncMod.ModLog.Warning("枚举角色 prefab 失败：" + ex.Message);
                _profiles = profiles;
                _byCharType = byCharType;
                _spriteSizes = spriteSizes;
                _spriteOwners = spriteOwners;
                return;
            }

            if (prefabs == null || prefabs.Length == 0)
            {
                SkinSyncMod.ModLog.Warning($"未在 Resources/{RootPath} 下找到角色 prefab，跨角色贴图映射不可用。");
                _profiles = profiles;
                _byCharType = byCharType;
                _spriteSizes = spriteSizes;
                _spriteOwners = spriteOwners;
                return;
            }

            for (int i = 0; i < prefabs.Length; i++)
            {
                GameObject prefab = prefabs[i];
                if (prefab == null) continue;

                Body body = prefab.GetComponentInChildren<Body>(true);
                if (body == null) continue;

                var profile = new CharacterProfile
                {
                    CharType = body.charType,
                    PrefabPath = RootPath + "/" + prefab.name,
                };

                CollectLimbs(prefab, profile, spriteSizes);
                CollectFaces(prefab, profile, spriteSizes);
                CollectTail(prefab, profile, spriteSizes);

                if (profile.Limbs.Count == 0) continue;

                foreach (var kv in profile.Limbs) RegisterOwner(spriteOwners, kv.Value, profile.CharType);
                foreach (var kv in profile.Faces) RegisterOwner(spriteOwners, kv.Value, profile.CharType);
                if (profile.Tail != null) RegisterOwner(spriteOwners, profile.Tail, profile.CharType);

                if (!byCharType.ContainsKey(profile.CharType))
                {
                    byCharType[profile.CharType] = profile;
                }
                profiles.Add(profile);
            }

            _profiles = profiles;
            _byCharType = byCharType;
            _spriteSizes = spriteSizes;
            _spriteOwners = spriteOwners;
        }

        private static void CollectLimbs(GameObject prefab, CharacterProfile profile, Dictionary<string, Vector2Int> spriteSizes)
        {
            foreach (var limb in prefab.GetComponentsInChildren<Limb>(true))
            {
                if (limb == null) continue;
                string partKey = limb.gameObject.name;
                if (string.IsNullOrEmpty(partKey) || profile.Limbs.ContainsKey(partKey)) continue;

                SpriteRenderer renderer = limb.GetComponent<SpriteRenderer>();
                PartBinding binding = MakeBinding(partKey, renderer, spriteSizes);
                if (binding != null) profile.Limbs[partKey] = binding;
            }
        }

        private static void CollectFaces(GameObject prefab, CharacterProfile profile, Dictionary<string, Vector2Int> spriteSizes)
        {
            FacialExpression face = prefab.GetComponentInChildren<FacialExpression>(true);
            if (face == null) return;

            AddFace(profile, PartKeyNames.DefaultHead, face.defaultHead, spriteSizes);
            AddFace(profile, PartKeyNames.DefaultHeadMouth, face.defaultHeadMouth, spriteSizes);
            AddFace(profile, PartKeyNames.DefaultHeadMouthHalf, face.defaultHeadMouthHalf, spriteSizes);
            AddFace(profile, PartKeyNames.EyesGone, face.eyesGone, spriteSizes);
            AddFace(profile, PartKeyNames.EyesGoneHealed, face.eyesGoneHealed, spriteSizes);

            if (face.disfiguredHead != null)
            {
                for (int i = 0; i < face.disfiguredHead.Length; i++)
                    AddFace(profile, PartKeyNames.DisfiguredHead(i), face.disfiguredHead[i], spriteSizes);
            }
            if (face.disfiguredHeadHeal != null)
            {
                for (int i = 0; i < face.disfiguredHeadHeal.Length; i++)
                    AddFace(profile, PartKeyNames.DisfiguredHeadHeal(i), face.disfiguredHeadHeal[i], spriteSizes);
            }
            if (face.eyeList != null)
            {
                for (int i = 0; i < face.eyeList.Count; i++)
                {
                    Eye eye = face.eyeList[i];
                    AddFace(profile, PartKeyNames.EyeFront(i), eye.front, spriteSizes);
                    AddFace(profile, PartKeyNames.EyeBack(i), eye.back, spriteSizes);
                }
            }
        }

        private static void CollectTail(GameObject prefab, CharacterProfile profile, Dictionary<string, Vector2Int> spriteSizes)
        {
            TailScript tail = prefab.GetComponentInChildren<TailScript>(true);
            if (tail == null) return;

            SpriteRenderer renderer = tail.GetComponent<SpriteRenderer>();
            PartBinding binding = MakeBinding(PartKeyNames.Tail, renderer, spriteSizes);
            if (binding != null) profile.Tail = binding;
        }

        private static void AddFace(CharacterProfile profile, string partKey, Sprite sprite, Dictionary<string, Vector2Int> spriteSizes)
        {
            if (sprite == null || string.IsNullOrEmpty(sprite.name)) return;
            var binding = new PartBinding
            {
                PartKey = partKey,
                SpriteName = sprite.name,
                StockSprite = sprite,
                PixelSize = new Vector2Int(
                    Mathf.RoundToInt(sprite.rect.width), Mathf.RoundToInt(sprite.rect.height)),
                SortingOrder = 0,
            };
            profile.Faces[partKey] = binding;
            if (!spriteSizes.ContainsKey(binding.SpriteName)) spriteSizes[binding.SpriteName] = binding.PixelSize;
        }

        private static PartBinding MakeBinding(string partKey, SpriteRenderer renderer, Dictionary<string, Vector2Int> spriteSizes)
        {
            Sprite sprite = renderer != null ? renderer.sprite : null;
            if (sprite == null || string.IsNullOrEmpty(sprite.name)) return null;

            var binding = new PartBinding
            {
                PartKey = partKey,
                SpriteName = sprite.name,
                StockSprite = sprite,
                PixelSize = new Vector2Int(
                    Mathf.RoundToInt(sprite.rect.width), Mathf.RoundToInt(sprite.rect.height)),
                SortingOrder = renderer.sortingOrder,
            };
            if (!spriteSizes.ContainsKey(binding.SpriteName)) spriteSizes[binding.SpriteName] = binding.PixelSize;
            return binding;
        }

        /// <summary>按原版贴图名反查所属部位键与角色类型。</summary>
        internal static bool TryFindPartBySpriteName(string spriteName, out string partKey, out int charType)
        {
            EnsureLoaded();
            partKey = null;
            charType = -1;
            if (string.IsNullOrEmpty(spriteName) || _spriteOwners == null) return false;
            if (!_spriteOwners.TryGetValue(spriteName, out var owner)) return false;
            partKey = owner.Key;
            charType = owner.Value;
            return true;
        }

        private static void RegisterOwner(Dictionary<string, KeyValuePair<string, int>> spriteOwners, PartBinding binding, int charType)
        {
            if (binding == null || string.IsNullOrEmpty(binding.SpriteName)) return;
            if (spriteOwners.ContainsKey(binding.SpriteName)) return;
            spriteOwners[binding.SpriteName] = new KeyValuePair<string, int>(binding.PartKey, charType);
        }

        /// <summary>将采集到的角色档案格式化为诊断日志文本。</summary>
        internal static string DescribeAll()
        {
            EnsureLoaded();
            if (_profiles == null) return "角色档案：未加载";
            var sb = new StringBuilder();
            sb.Append("角色档案：").Append(_profiles.Count).Append(" 个，贴图尺寸表 ").Append(_spriteSizes.Count).Append(" 条");
            for (int i = 0; i < _profiles.Count; i++)
            {
                CharacterProfile p = _profiles[i];
                sb.Append('\n').Append("  charType=").Append(p.CharType).Append("  ").Append(p.PrefabPath)
                  .Append("  肢体 ").Append(p.Limbs.Count).Append("  表情 ").Append(p.Faces.Count)
                  .Append("  尾巴 ").Append(p.Tail != null ? p.Tail.SpriteName : "无");

                foreach (var kv in p.Limbs)
                {
                    sb.Append('\n').Append("    limb ").Append(kv.Key.PadRight(10))
                      .Append(kv.Value.SpriteName.PadRight(26))
                      .Append(kv.Value.PixelSize.x).Append('x').Append(kv.Value.PixelSize.y);
                }
                foreach (var kv in p.Faces)
                {
                    sb.Append('\n').Append("    face ").Append(kv.Key.PadRight(24))
                      .Append(kv.Value.SpriteName.PadRight(30))
                      .Append(kv.Value.PixelSize.x).Append('x').Append(kv.Value.PixelSize.y);
                }
            }
            return sb.ToString();
        }
    }
}
