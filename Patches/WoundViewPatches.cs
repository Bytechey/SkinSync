using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace SkinSyncMod.Patches
{
    internal static class WoundViewPatchLog
    {
        private const float WarnCooldownSec = 5f;

        internal static void WarnThrottled(ref float nextWarnAt, string prefix, System.Exception ex)
        {
            float now = Time.realtimeSinceStartup;
            if (now < nextWarnAt) return;
            nextWarnAt = now + WarnCooldownSec;
            ModLog.Warning(prefix + ex.Message);
        }
    }

    /// <summary>在 WoundView.Start 重置物种头图后强制重涂自定义伤势贴图与彩色肌肉层，防止首次激活面板时覆盖官方钩子预涂装结果。</summary>
    [HarmonyPatch(typeof(WoundView), "Start")]
    public static class WoundViewStartSkinPatch
    {
        private static float _nextWarnAt;

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(WoundView __instance)
        {
            if (__instance == null) return;
            try
            {
                WoundViewAttacher.OnTargetApplied(__instance, __instance.body);
            }
            catch (System.Exception ex)
            {
                WoundViewPatchLog.WarnThrottled(ref _nextWarnAt, "WoundViewStartSkinPatch failed: ", ex);
            }
        }
    }

    /// <summary>在 WoundView.UpdateView 后检测观察目标变化并维持彩色肌肉伤势贴图显示。</summary>
    [HarmonyPatch(typeof(WoundView), nameof(WoundView.UpdateView))]
    public static class WoundViewUpdateSkinPatch
    {
        private static float _nextWarnAt;

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(WoundView __instance)
        {
            if (__instance == null) return;
            try
            {
                WoundViewAttacher.OnUpdateView(__instance);
            }
            catch (System.Exception ex)
            {
                WoundViewPatchLog.WarnThrottled(ref _nextWarnAt, "WoundViewUpdateSkinPatch failed: ", ex);
            }
        }
    }

    /// <summary>在 WoundView.AddImageToLimb 动态生成伤口贴片后按受伤肢体所属躯体替换为自定义贴图，规避面板目标还原顺序竞态。</summary>
    [HarmonyPatch(typeof(WoundView), nameof(WoundView.AddImageToLimb))]
    public static class WoundViewAddImageSkinPatch
    {
        private static float _nextWarnAt;

        [HarmonyPostfix]
        public static void Postfix(WoundView __instance, Limb __0, Image __result)
        {
            if (__instance == null || __result == null) return;
            try
            {
                WoundViewAttacher.ApplyDynamicLimbImage(__instance, __result, __0 != null ? __0.body : null);
            }
            catch (System.Exception ex)
            {
                WoundViewPatchLog.WarnThrottled(ref _nextWarnAt, "WoundViewAddImageSkinPatch failed: ", ex);
            }
        }
    }
}
