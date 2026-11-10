using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SkinSyncMod.Character
{
    /// <summary>
    /// 角色档案采集器：运行时从 Resources 读取所有角色 prefab，采集每个角色每个部位的贴图名与原生像素尺寸。
    /// </summary>
    /// <remarks>
    /// 三条设计约束，全部来自游戏事实：
    /// 1) 只读 prefab，不实例化——Resources.Load 拿到的是共享资产，任何改写都会污染后续 Utils.Create 生成的角色。
    /// 2) charType 不靠手工映射，直接读 prefab 上 Body.charType（它本来就是序列化值）。
    /// 3) 角色集合用 Resources.LoadAll 枚举（游戏自己在 ConsoleScript 里也这么干），新增角色无需改代码。
    /// 采集结果用静态字段持有，避免场景切换的 UnloadUnusedAssets 把它回收。
    /// </remarks>
    internal static class CharacterCatalog
    {
        /// <summary>角色 prefab 所在目录。</summary>
        private const string RootPath = "Special/Characters";

        private static List<CharacterProfile> _profiles;
        private static Dictionary<int, CharacterProfile> _byCharType;
        private static Dictionary<string, Vector2Int> _spriteSizes;
        /// <summary>贴图名 → (部位键, 归属角色)。跨角色回退时用它反查「这张图属于哪个部位」。</summary>
        private static Dictionary<string, KeyValuePair<string, int>> _spriteOwners;

        /// <summary>已采集的角色档案；未采集时为空列表。</summary>
        internal static IReadOnlyList<CharacterProfile> All
        {
            get
            {
                EnsureLoaded();
                return _profiles;
            }
        }

        /// <summary>按 charType 取角色档案；未知 charType 返回 null（调用方应降级为"只显示原图"）。</summary>
        internal static CharacterProfile ForCharType(int charType)
        {
            EnsureLoaded();
            return _byCharType.TryGetValue(charType, out var profile) ? profile : null;
        }

        /// <summary>查某张贴图在游戏里的原生像素尺寸；未知贴图返回 false。</summary>
        internal static bool TryGetSpriteSize(string spriteName, out Vector2Int size)
        {
            EnsureLoaded();
            size = default;
            return !string.IsNullOrEmpty(spriteName) && _spriteSizes.TryGetValue(spriteName, out size);
        }

        /// <summary>采集一次并缓存。多次调用无副作用。</summary>
        internal static void EnsureLoaded()
        {
            if (_profiles != null) return;

            _profiles = new List<CharacterProfile>();
            _byCharType = new Dictionary<int, CharacterProfile>();
            _spriteSizes = new Dictionary<string, Vector2Int>(StringComparer.Ordinal);
            _spriteOwners = new Dictionary<string, KeyValuePair<string, int>>(StringComparer.Ordinal);

            GameObject[] prefabs;
            try
            {
                prefabs = Resources.LoadAll<GameObject>(RootPath);
            }
            catch (Exception ex)
            {
                SkinSyncMod.ModLog.Warning("枚举角色 prefab 失败：" + ex.Message);
                return;
            }

            if (prefabs == null || prefabs.Length == 0)
            {
                SkinSyncMod.ModLog.Warning($"未在 Resources/{RootPath} 下找到角色 prefab，跨角色贴图映射不可用。");
                return;
            }

            for (int i = 0; i < prefabs.Length; i++)
            {
                GameObject prefab = prefabs[i];
                if (prefab == null) continue;

                Body body = prefab.GetComponentInChildren<Body>(true);
                if (body == null) continue;                       // 目录下的非角色资源

                var profile = new CharacterProfile
                {
                    CharType = body.charType,
                    PrefabPath = RootPath + "/" + prefab.name,
                };

                CollectLimbs(prefab, profile);
                CollectFaces(prefab, profile);
                CollectTail(prefab, profile);

                if (profile.Limbs.Count == 0) continue;           // 结构不符，跳过

                // 登记贴图归属，供按贴图名反查部位（表情走这条）
                foreach (var kv in profile.Limbs) RegisterOwner(kv.Value, profile.CharType);
                foreach (var kv in profile.Faces) RegisterOwner(kv.Value, profile.CharType);
                if (profile.Tail != null) RegisterOwner(profile.Tail, profile.CharType);

                if (!_byCharType.ContainsKey(profile.CharType))
                {
                    _byCharType[profile.CharType] = profile;
                }
                _profiles.Add(profile);
            }
        }

        private static void CollectLimbs(GameObject prefab, CharacterProfile profile)
        {
            foreach (var limb in prefab.GetComponentsInChildren<Limb>(true))
            {
                if (limb == null) continue;
                string partKey = limb.gameObject.name;
                if (string.IsNullOrEmpty(partKey) || profile.Limbs.ContainsKey(partKey)) continue;

                SpriteRenderer renderer = limb.GetComponent<SpriteRenderer>();
                PartBinding binding = MakeBinding(partKey, renderer);
                if (binding != null) profile.Limbs[partKey] = binding;
            }
        }

        private static void CollectFaces(GameObject prefab, CharacterProfile profile)
        {
            FacialExpression face = prefab.GetComponentInChildren<FacialExpression>(true);
            if (face == null) return;

            AddFace(profile, PartKeyNames.DefaultHead, face.defaultHead);
            AddFace(profile, PartKeyNames.DefaultHeadMouth, face.defaultHeadMouth);
            AddFace(profile, PartKeyNames.DefaultHeadMouthHalf, face.defaultHeadMouthHalf);
            AddFace(profile, PartKeyNames.EyesGone, face.eyesGone);
            AddFace(profile, PartKeyNames.EyesGoneHealed, face.eyesGoneHealed);

            if (face.disfiguredHead != null)
            {
                for (int i = 0; i < face.disfiguredHead.Length; i++)
                    AddFace(profile, PartKeyNames.DisfiguredHead(i), face.disfiguredHead[i]);
            }
            if (face.disfiguredHeadHeal != null)
            {
                for (int i = 0; i < face.disfiguredHeadHeal.Length; i++)
                    AddFace(profile, PartKeyNames.DisfiguredHeadHeal(i), face.disfiguredHeadHeal[i]);
            }
            if (face.eyeList != null)
            {
                for (int i = 0; i < face.eyeList.Count; i++)
                {
                    Eye eye = face.eyeList[i];
                    AddFace(profile, PartKeyNames.EyeFront(i), eye.front);
                    AddFace(profile, PartKeyNames.EyeBack(i), eye.back);
                }
            }
        }

        private static void CollectTail(GameObject prefab, CharacterProfile profile)
        {
            TailScript tail = prefab.GetComponentInChildren<TailScript>(true);
            if (tail == null) return;

            SpriteRenderer renderer = tail.GetComponent<SpriteRenderer>();
            PartBinding binding = MakeBinding(PartKeyNames.Tail, renderer);
            if (binding != null) profile.Tail = binding;
        }

        private static void AddFace(CharacterProfile profile, string partKey, Sprite sprite)
        {
            if (sprite == null) return;
            var binding = new PartBinding
            {
                PartKey = partKey,
                SpriteName = sprite.name,
                PixelSize = new Vector2Int(
                    Mathf.RoundToInt(sprite.rect.width), Mathf.RoundToInt(sprite.rect.height)),
                SortingOrder = 0,
            };
            profile.Faces[partKey] = binding;
            if (!_spriteSizes.ContainsKey(binding.SpriteName)) _spriteSizes[binding.SpriteName] = binding.PixelSize;
        }

        private static PartBinding MakeBinding(string partKey, SpriteRenderer renderer)
        {
            Sprite sprite = renderer != null ? renderer.sprite : null;
            if (sprite == null || string.IsNullOrEmpty(sprite.name)) return null;

            var binding = new PartBinding
            {
                PartKey = partKey,
                SpriteName = sprite.name,
                PixelSize = new Vector2Int(
                    Mathf.RoundToInt(sprite.rect.width), Mathf.RoundToInt(sprite.rect.height)),
                SortingOrder = renderer.sortingOrder,
            };
            if (!_spriteSizes.ContainsKey(binding.SpriteName)) _spriteSizes[binding.SpriteName] = binding.PixelSize;
            return binding;
        }

        /// <summary>反查某张贴图属于哪个部位键、哪个角色。同一张贴图（如前后臂共用）只记首次出现。</summary>
        internal static bool TryFindPartBySpriteName(string spriteName, out string partKey, out int charType)
        {
            EnsureLoaded();
            partKey = null;
            charType = -1;
            if (string.IsNullOrEmpty(spriteName)) return false;
            if (!_spriteOwners.TryGetValue(spriteName, out var owner)) return false;
            partKey = owner.Key;
            charType = owner.Value;
            return true;
        }

        private static void RegisterOwner(PartBinding binding, int charType)
        {
            if (binding == null || string.IsNullOrEmpty(binding.SpriteName)) return;
            if (_spriteOwners.ContainsKey(binding.SpriteName)) return;
            _spriteOwners[binding.SpriteName] = new KeyValuePair<string, int>(binding.PartKey, charType);
        }

        /// <summary>诊断用：把采集结果格式化成多行文本，供日志输出核对。</summary>
        internal static string DescribeAll()
        {
            EnsureLoaded();
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
