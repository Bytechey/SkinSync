using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace SkinSyncMod.Patches
{
    /// <summary>在 WoundView.Start 初始化物种头图后按需挂载自定义伤势贴图，若官方视图钩子已同帧涂装则命中缓存短路。</summary>
    [HarmonyPatch(typeof(WoundView), "Start")]
    public static class WoundViewStartSkinPatch
    {
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
                ModLog.Debug("WoundViewStartSkinPatch failed: " + ex.Message);
            }
        }
    }

    /// <summary>在 WoundView.UpdateView 后检测观察目标变化并维持彩色肌肉伤势贴图显示。</summary>
    [HarmonyPatch(typeof(WoundView), nameof(WoundView.UpdateView))]
    public static class WoundViewUpdateSkinPatch
    {
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
                ModLog.Debug("WoundViewUpdateSkinPatch failed: " + ex.Message);
            }
        }
    }

    /// <summary>在 WoundView.AddImageToLimb 动态生成伤口贴片后替换为皮肤包内的同名自定义贴图。</summary>
    [HarmonyPatch(typeof(WoundView), nameof(WoundView.AddImageToLimb))]
    public static class WoundViewAddImageSkinPatch
    {
        [HarmonyPostfix]
        public static void Postfix(WoundView __instance, Image __result)
        {
            if (__instance == null || __result == null) return;
            try
            {
                WoundViewAttacher.ApplyDynamicLimbImage(__instance, __result);
            }
            catch (System.Exception ex)
            {
                ModLog.Debug("WoundViewAddImageSkinPatch failed: " + ex.Message);
            }
        }
    }
}
