using System.Collections.Generic;
using SkinSyncMod.Character;
using UnityEngine;

namespace SkinSyncMod.Resolve
{
    /// <summary>
    /// 皮肤贴图解析：给定「游戏里这个部位原本用的贴图名」和「皮肤目录里的贴图字典」，选出实际要用的贴图。
    /// </summary>
    /// <remarks>
    /// 三层查找，顺序固定：
    ///   1) 当前角色的贴图名        —— 为某个角色专门画的皮肤
    ///   2) 部位键名                —— 角色无关的通用命名（UpTorso.png）
    ///   3) 其他角色同一部位的贴图名 —— 跨角色借用（候选来自 CharacterCatalog，不维护任何别名表）
    /// 每层内部再按 R_ 前缀 / F/B 后缀规则挑朝向变体。
    /// 三层都不命中时返回 Miss，调用方保留游戏原图。
    /// </remarks>
    internal static class SkinResolver
    {
        /// <summary>
        /// 按游戏原贴图名解析。
        /// </summary>
        /// <param name="gameSpriteName">该部位在当前角色上的原始贴图名（experimentUpTorso / milkytorso …）</param>
        /// <param name="skin">皮肤字典：文件名（无扩展名）→ Sprite</param>
        /// <param name="currentCharType">当前角色 charType，用于排除自身</param>
        /// <param name="side">F / B 侧向标记，无侧向为 '\0'</param>
        /// <param name="isRight">身体是否朝右</param>
        internal static ResolveOutcome ResolveByName(
            string gameSpriteName,
            Dictionary<string, Sprite> skin,
            int currentCharType,
            char side,
            bool isRight)
        {
            if (string.IsNullOrEmpty(gameSpriteName) || skin == null || skin.Count == 0)
                return ResolveOutcome.Miss();

            // 1) 当前角色的贴图名
            Sprite hit = SidedSpriteLookup.Find(skin, gameSpriteName, side, isRight);
            if (hit != null) return Make(hit, ResolveOrigin.Native, currentCharType);

            // 反查它属于哪个部位键；反查不到说明这张图不在角色档案里，无法跨角色回退
            if (!CharacterCatalog.TryFindPartBySpriteName(gameSpriteName, out string partKey, out int ownerCharType))
                return ResolveOutcome.Miss();

            // 2) 部位键名（角色无关命名）
            if (!string.IsNullOrEmpty(partKey))
            {
                hit = SidedSpriteLookup.Find(skin, partKey, side, isRight);
                if (hit != null) return Make(hit, ResolveOrigin.Generic, currentCharType);
            }

            // 3) 其他角色的同名部位
            var profiles = CharacterCatalog.All;
            for (int i = 0; i < profiles.Count; i++)
            {
                CharacterProfile other = profiles[i];
                if (other == null || other.CharType == ownerCharType) continue;

                PartBinding binding = other.Find(partKey);
                if (binding == null || binding.SpriteName == gameSpriteName) continue;

                hit = SidedSpriteLookup.Find(skin, binding.SpriteName, side, isRight);
                if (hit != null) return Make(hit, ResolveOrigin.CrossCharacter, other.CharType);
            }

            return ResolveOutcome.Miss();
        }

        private static ResolveOutcome Make(Sprite sprite, ResolveOrigin origin, int ownerCharType)
        {
            return new ResolveOutcome
            {
                Sprite = sprite,
                SpriteName = sprite.name,
                Origin = origin,
                OwnerCharType = origin == ResolveOrigin.CrossCharacter ? ownerCharType : -1,
            };
        }
    }
}
