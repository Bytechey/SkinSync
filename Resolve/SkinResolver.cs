using System.Collections.Generic;
using SkinSyncMod.Character;
using UnityEngine;

namespace SkinSyncMod.Resolve
{
    /// <summary>按当前角色名、通用部位键与其他角色同名部位三层顺序解析皮肤贴图。</summary>
    internal static class SkinResolver
    {
        /// <summary>根据游戏原贴图名在皮肤字典中查找替代贴图，未命中返回 Miss。</summary>
        internal static ResolveOutcome ResolveByName(
            string gameSpriteName,
            Dictionary<string, Sprite> skin,
            int currentCharType,
            char side,
            bool isRight)
        {
            if (string.IsNullOrEmpty(gameSpriteName) || skin == null || skin.Count == 0)
                return ResolveOutcome.Miss();

            Sprite hit = SidedSpriteLookup.Find(skin, gameSpriteName, side, isRight);
            if (hit != null) return Make(hit, ResolveOrigin.Native, currentCharType);

            if (!CharacterCatalog.TryFindPartBySpriteName(gameSpriteName, out string partKey, out int ownerCharType))
                return ResolveOutcome.Miss();

            if (!string.IsNullOrEmpty(partKey))
            {
                hit = SidedSpriteLookup.Find(skin, partKey, side, isRight);
                if (hit != null) return Make(hit, ResolveOrigin.Generic, currentCharType);
            }

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
