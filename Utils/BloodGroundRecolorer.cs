using System.Collections.Generic;
using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>全局 watcher：按位置反查 character 给落地血迹（GroundBlood / wallblood / blockblood / wallvomit / blockvomit）与爆血粒子（BloodExplosion）染色。</summary>
    [DisallowMultipleComponent]
    public class BloodGroundRecolorer : MonoBehaviour
    {
        private const float TickIntervalSec = 0.25f;
        private const float OwnerSearchRadius = 12f;
        private const int MaxAttempts = 16;
        private float _nextTick;
        private readonly Dictionary<int, int> _spriteAttempts = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _particleAttempts = new Dictionary<int, int>();
        private readonly HashSet<int> _doneSprites = new HashSet<int>();
        private readonly HashSet<int> _doneParticles = new HashSet<int>();

        private struct CachedColors
        {
            public Color Light;
            public bool Available;
        }
        private readonly Dictionary<string, CachedColors> _spriteColorCache = new Dictionary<string, CachedColors>();

        private void Update()
        {
            if (!BloodRenderConfig.Enabled) return;
            if (Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + TickIntervalSec;

            ScanGroundSprites();
            ScanBloodExplosionParticles();
        }

        private void ScanGroundSprites()
        {
            foreach (var gb in FindObjectsOfType<GroundBlood>())
            {
                if (gb == null || gb.gameObject == null) continue;
                int id = gb.gameObject.GetInstanceID();
                if (_doneSprites.Contains(id)) continue;
                if (BumpAndCheckGiveUp(_spriteAttempts, id)) { _doneSprites.Add(id); continue; }
                if (TryApplyByOwner(gb.gameObject, gb.transform.position)) _doneSprites.Add(id);
            }

            foreach (var sr in FindObjectsOfType<SpriteRenderer>())
            {
                if (sr == null || sr.sprite == null) continue;
                int id = sr.gameObject.GetInstanceID();
                if (_doneSprites.Contains(id)) continue;
                if (!IsBloodLikeSpriteName(sr.sprite.name)) continue;
                if (BumpAndCheckGiveUp(_spriteAttempts, id)) { _doneSprites.Add(id); continue; }
                if (TryApplyByOwner(sr.gameObject, sr.transform.position, sr)) _doneSprites.Add(id);
            }
        }

        private void ScanBloodExplosionParticles()
        {
            foreach (var ps in FindObjectsOfType<ParticleSystem>())
            {
                if (ps == null) continue;
                int id = ps.GetInstanceID();
                if (_doneParticles.Contains(id)) continue;
                string n = ps.gameObject.name ?? "";
                if (n.IndexOf("BloodExplosion", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (BumpAndCheckGiveUp(_particleAttempts, id)) { _doneParticles.Add(id); continue; }
                if (!TryResolveCharacter(ps.transform.position, out string character)) continue;
                BloodAttacher.ApplyToParticleByCharacter(ps, character);
                _doneParticles.Add(id);
            }
        }

        private static bool IsBloodLikeSpriteName(string n)
        {
            return n.IndexOf("wallblood", System.StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("blockblood", System.StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("wallvomit", System.StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("blockvomit", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool TryApplyByOwner(GameObject go, Vector2 pos, SpriteRenderer providedSr = null)
        {
            if (!TryResolveCharacter(pos, out string character)) return false;
            if (!TryGetSpriteColor(character, out Color light)) return false;
            var sr = providedSr ?? go.GetComponent<SpriteRenderer>();
            if (sr == null) return false;
            float a = sr.color.a;
            sr.color = new Color(light.r, light.g, light.b, a);
            return true;
        }

        private static bool TryResolveCharacter(Vector2 pos, out string character)
        {
            if (SkinApplier.TryGetCharacterByPosition(pos, OwnerSearchRadius, out character))
            {
                SkinSyncMod.ModLog.Info($"血迹 owner 反查：位置 {pos} → {character}（按距离）");
                return true;
            }
            if (SkinApplier.TryGetAnyAppliedCharacter(out character))
            {
                SkinSyncMod.ModLog.Info($"血迹 owner 反查：位置 {pos} → {character}（单角色兜底）");
                return true;
            }
            SkinSyncMod.ModLog.Info($"血迹 owner 反查失败：位置 {pos}（多角色且半径 {OwnerSearchRadius} 内无匹配 Body）");
            return false;
        }

        private static bool BumpAndCheckGiveUp(Dictionary<int, int> attempts, int id)
        {
            attempts.TryGetValue(id, out int n);
            n++;
            attempts[id] = n;
            return n > MaxAttempts;
        }

        private bool TryGetSpriteColor(string character, out Color light)
        {
            light = Color.white;
            if (_spriteColorCache.TryGetValue(character, out var cached))
            {
                light = cached.Light;
                return cached.Available;
            }
            var entry = new CachedColors();
            if (BloodAttacher.TryLoadCharacterBlood(character, out var cfg, out _))
            {
                Color32? main = cfg.ParticleStartColor ?? cfg.BloodLight;
                if (main.HasValue)
                {
                    entry.Light = (Color)main.Value;
                    entry.Available = true;
                }
            }
            _spriteColorCache[character] = entry;
            light = entry.Light;
            return entry.Available;
        }
    }
}
