using HarmonyLib;
using UnityEngine;

namespace SkinSyncMod.Patches
{
    /// <summary>在 Limb.Awake 后为已套皮肤角色的动态生成肢体设置自定义血色。</summary>
    [HarmonyPatch(typeof(Limb), "Awake")]
    public static class LimbAwakeBleedColorPatch
    {
        private const float WarnCooldownSeconds = 5f;
        private static float _nextWarnTime;

        [HarmonyPostfix]
        public static void Postfix(Limb __instance)
        {
            if (!BloodRenderConfig.Enabled || __instance == null) return;
            try
            {
                string character = BleedSpawnTranspiler.ResolveCharacter(__instance.transform);
                if (character != null)
                {
                    BloodAttacher.ApplyToLimbByCharacter(__instance, character);
                }
            }
            catch (System.Exception ex)
            {
                float now = Time.unscaledTime;
                if (now >= _nextWarnTime)
                {
                    _nextWarnTime = now + WarnCooldownSeconds;
                    ModLog.Warning("LimbAwakeBleedColorPatch failed: " + ex.Message);
                }
            }
        }
    }

    /// <summary>在 FacialExpression.Start 末尾重刷已套皮肤角色的表情贴图，或为联机新就绪躯体触发皮肤恢复与净身。</summary>
    [HarmonyPatch(typeof(FacialExpression), "Start")]
    public static class FacialExpressionStartSkinPatch
    {
        private const float WarnCooldownSeconds = 5f;
        private static float _nextWarnTime;

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
                    SkinSync.OnExternalBodyReadyOrRebuilt(__instance.body, 0u, __instance.body.charType, isRebuild: false);
                }
            }
            catch (System.Exception ex)
            {
                float now = Time.unscaledTime;
                if (now >= _nextWarnTime)
                {
                    _nextWarnTime = now + WarnCooldownSeconds;
                    ModLog.Warning("FacialExpressionStartSkinPatch failed: " + ex.Message);
                }
            }
        }
    }
}
