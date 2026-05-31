using HarmonyLib;
using UnityEngine;

namespace SkinSyncMod.Patches
{
    /// <summary>Limb.Awake 实例化后按 owner 设受伤血色，仅玩家肢体生效。</summary>
    [HarmonyPatch(typeof(Limb), "Awake")]
    public static class LimbAwakeBleedColorPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Limb __instance)
        {
            if (!BloodRenderConfig.Enabled) return;
            try
            {
                if (__instance == null) return;
                string character = ResolveCharacter(__instance.transform);
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
}
