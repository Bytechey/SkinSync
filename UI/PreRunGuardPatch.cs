using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>面板打开或光标悬停在主菜单注入按钮上时，将 AdaptiveButton.overlayActive 置为 true 以阻断悬停穿透。</summary>
    [HarmonyPatch(typeof(AdaptiveButton), "overlayActive", MethodType.Getter)]
    internal static class SkinSyncAdaptiveButtonOverlayActiveGuard
    {
        private static void Postfix(ref bool __result)
        {
            if (UiBlocker.IsBlocking) { __result = true; return; }
            var rt = MenuButtonInjector.InjectedRect;
            if (rt == null || !rt.gameObject.activeInHierarchy) return;
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

    /// <summary>面板打开或光标位于主菜单注入按钮上时拦截 AdaptiveButton.Clicked 点击。</summary>
    [HarmonyPatch]
    internal static class SkinSyncAdaptiveButtonClickedGuard
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(AdaptiveButton), "Clicked");
        }

        private static bool Prefix()
        {
            if (UiBlocker.IsBlocking) return false;
            var rt = MenuButtonInjector.InjectedRect;
            if (rt == null || !rt.gameObject.activeInHierarchy) return true;
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

    /// <summary>面板打开期间阻断 PlayerCamera.HandleInput 键盘与视角输入，并清零残留移动与下蹲状态。</summary>
    [HarmonyPatch(typeof(PlayerCamera), "HandleInput")]
    internal static class SkinSyncPlayerCameraHandleInputGuard
    {
        [HarmonyPrefix]
        private static bool Prefix()
        {
            if (!UiBlocker.IsBlocking) return true;
            var cam = PlayerCamera.main;
            if (cam != null && cam.body != null)
            {
                cam.body.moveDir = Vector2.zero;
                cam.body.crouching = false;
            }
            return false;
        }
    }

    /// <summary>面板打开期间阻断 PlayerCamera 世界物体悬停高亮与右键拖拽拾取，并清理悬停框与拖拽状态。</summary>
    [HarmonyPatch]
    internal static class SkinSyncPlayerCameraMouseGuard
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var m1 = AccessTools.Method(typeof(PlayerCamera), "HandleMouseOver");
            if (m1 != null) yield return m1;
            var m2 = AccessTools.Method(typeof(PlayerCamera), "HandleDragging");
            if (m2 != null) yield return m2;
        }

        [HarmonyPrefix]
        private static bool Prefix()
        {
            if (!UiBlocker.IsBlocking) return true;
            var cam = PlayerCamera.main;
            if (cam != null)
            {
                if (cam.hoverSquare != null) cam.hoverSquare.enabled = false;
                if (cam.dragImage != null) cam.dragImage.enabled = false;
                cam.dragItem = null;
            }
            return false;
        }
    }
}
