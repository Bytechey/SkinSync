using HarmonyLib;
using SkinSyncMod.Network;
using System.Reflection;
using UnityEngine;

namespace SkinSyncMod.Patches
{
    /// <summary>给 limb 的 wallBleed / groundBleed prefab 字段换上染色副本，使落地血 / 墙血显示 character 自定义色。</summary>
    [HarmonyPatch(typeof(BleedParticle), "Start")]
    public static class BleedParticleStartPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BleedParticle __instance)
        {
            try { BleedParticleRecolor.RecolorByOwner(__instance); }
            catch { }
        }
    }

    /// <summary>Limb.Awake 实例化 BleedParticle 后立刻按 owner 染色 + 设受伤血色，覆盖玩家先就绪 / ragdoll 重组的时序。</summary>
    [HarmonyPatch(typeof(Limb), "Awake")]
    public static class LimbAwakeBleedColorPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Limb __instance)
        {
            try
            {
                if (__instance == null) return;
                string character = ResolveCharacter(__instance.transform);
                foreach (var bp in __instance.GetComponentsInChildren<BleedParticle>(true))
                {
                    BleedParticleRecolor.RecolorByOwner(bp);
                }
                if (character != null)
                {
                    BloodAttacher.ApplyToLimbByCharacter(__instance, character);
                }
            }
            catch { }
        }

        private static string ResolveCharacter(Transform t)
        {
            while (t != null)
            {
                var name = SkinApplier.GetCharacterByChara(t.gameObject);
                if (!string.IsNullOrEmpty(name)) return name;
                t = t.parent;
            }
            return null;
        }
    }

    /// <summary>BleedParticle prefab 染色的实际入口；BleedParticle.Start 与 BloodAttacher.Apply 都调它。</summary>
    public static class BleedParticleRecolor
    {
        private static readonly FieldInfo _wallBleedField =
            typeof(BleedParticle).GetField("wallBleed", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo _groundBleedField =
            typeof(BleedParticle).GetField("groundBleed", BindingFlags.NonPublic | BindingFlags.Instance);

        public static void RecolorByCharacter(BleedParticle inst, string character)
        {
            if (inst == null || string.IsNullOrEmpty(character)) return;
            if (!BloodAttacher.TryLoadCharacterBlood(character, out var cfg, out _)) return;
            Color32? main = cfg.ParticleStartColor ?? cfg.BloodLight;
            if (!main.HasValue) return;
            ApplyColor(inst, (Color)main.Value);
            SkinSyncMod.SkinSync.LogBoth($"[SkinSync] BleedParticle prefab 已染色：{character} → ({main.Value.r},{main.Value.g},{main.Value.b})");
        }

        public static void RecolorByOwner(BleedParticle inst)
        {
            if (inst == null) return;
            string character = ResolveCharacter(inst);
            if (character != null) RecolorByCharacter(inst, character);
        }

        private static string ResolveCharacter(BleedParticle inst)
        {
            Transform t = inst.transform;
            while (t != null)
            {
                var name = SkinApplier.GetCharacterByChara(t.gameObject);
                if (!string.IsNullOrEmpty(name)) return name;
                t = t.parent;
            }
            return null;
        }

        private static void ApplyColor(BleedParticle inst, Color color)
        {
            ReplacePrefab(_wallBleedField, inst, color);
            ReplacePrefab(_groundBleedField, inst, color);
        }

        private static void ReplacePrefab(FieldInfo field, BleedParticle inst, Color color)
        {
            if (field == null) return;
            var prefab = field.GetValue(inst) as GameObject;
            if (prefab == null) return;
            const string TaggedSuffix = "__SkinSyncColored";
            if (prefab.name.EndsWith(TaggedSuffix)) {
                var sr0 = prefab.GetComponent<SpriteRenderer>();
                if (sr0 != null) sr0.color = new Color(color.r, color.g, color.b, sr0.color.a);
                return;
            }
            var copy = UnityEngine.Object.Instantiate(prefab);
            copy.name = prefab.name + TaggedSuffix;
            copy.hideFlags = HideFlags.HideInHierarchy | HideFlags.NotEditable;
            copy.transform.position = new Vector3(99999f, 99999f, 99999f);
            UnityEngine.Object.DontDestroyOnLoad(copy);
            var sr = copy.GetComponent<SpriteRenderer>();
            if (sr != null) sr.color = new Color(color.r, color.g, color.b, sr.color.a);
            field.SetValue(inst, copy);
        }
    }
}
