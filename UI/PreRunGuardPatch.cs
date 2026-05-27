using HarmonyLib;
using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>
    /// SkinSync 防穿透 patch：
    /// - AdaptiveButton.overlayActive getter：UiBlocker.IsBlocking 或鼠标在 SkinSync 主菜单按钮区域时强制 true。
    /// - AdaptiveButton.Clicked：同上条件 return false 拦截穿透。
    /// - PlayerCamera.HandleInput：UiBlocker.IsBlocking 时整段 return false 吞输入（含 ESC）。
    /// </summary>
    [HarmonyPatch(typeof(AdaptiveButton), "overlayActive", MethodType.Getter)]
    internal static class SkinSyncAdaptiveButtonOverlayActiveGuard
    {
        private static void Postfix(ref bool __result)
        {
            if (UiBlocker.IsBlocking) { __result = true; return; }
            var rt = MenuButtonInjector.InjectedRect;
            if (rt == null) return;
            try
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(rt, Input.mousePosition, null))
                {
                    __result = true;
                }
            }
            catch { }
        }
    }

    [HarmonyPatch]
    internal static class SkinSyncAdaptiveButtonClickedGuard
    {
        private static System.Reflection.MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(AdaptiveButton), "Clicked");
        }

        private static bool Prefix()
        {
            if (UiBlocker.IsBlocking) return false;
            var rt = MenuButtonInjector.InjectedRect;
            if (rt == null) return true;
            try
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(rt, Input.mousePosition, null))
                {
                    return false;
                }
            }
            catch { }
            return true;
        }
    }

    [HarmonyPatch(typeof(PlayerCamera), "HandleInput")]
    internal static class SkinSyncPlayerCameraHandleInputGuard
    {
        [HarmonyPrefix]
        private static bool Prefix()
        {
            return !UiBlocker.IsBlocking;
        }
    }
}
