using HarmonyLib;
using UnityEngine;

namespace SkinSyncMod.Patches
{
    /// <summary>在 Limb.Awake 后为已套皮肤角色的动态生成肢体设置自定义血色。</summary>
    [HarmonyPatch(typeof(Limb), "Awake")]
    public static class LimbAwakeBleedColorPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Limb __instance)
        {
            if (!BloodRenderConfig.Enabled || __instance == null) return;
            try
            {
                string character = ResolveCharacter(__instance.transform);
                if (character != null)
                {
                    BloodAttacher.ApplyToLimbByCharacter(__instance, character);
                }
            }
            catch (System.Exception ex)
            {
                ModLog.Debug("LimbAwakeBleedColorPatch failed: " + ex.Message);
            }
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

    /// <summary>在 FacialExpression.Start 末尾重刷已套皮肤角色的表情贴图，或为同 netId 重建的新躯体恢复皮肤。</summary>
    [HarmonyPatch(typeof(FacialExpression), "Start")]
    public static class FacialExpressionStartSkinPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(FacialExpression __instance)
        {
            if (__instance == null) return;
            try
            {
                if (SkinApplier.ReapplyFaceIfNeeded(__instance)) return;
                if (__instance.body != null && Interop.Consumers.SessionConsumer.IsRunning)
                {
                    SkinSync.OnExternalBodyReadyOrRebuilt(__instance.body, 0u, __instance.body.charType, isRebuild: true);
                }
            }
            catch (System.Exception ex)
            {
                ModLog.Debug("FacialExpressionStartSkinPatch failed: " + ex.Message);
            }
        }
    }
}
