using System;
using UnityEngine;

namespace SkinSyncMod
{
    // 独立模式下作为暂停表盘按钮挂载失败时的右下角兜底入口，保留 124f 底边距以避让其他模组堆叠槽位
    internal sealed class InGameOverlay
    {
        private const float ButtonWidth = 320f;
        private const float ButtonHeight = 84f;
        private const float MarginRight = 24f;
        private const float MarginBottom = 124f;
        private const float BorderThickness = 5f;

        internal static bool IsGamePaused()
        {
            try
            {
                if (PauseHandler.paused) return true;
                var main = PauseHandler.main;
                return main != null && main.pauseContainer != null && main.pauseContainer.activeSelf;
            }
            catch
            {
                return false;
            }
        }

        internal static bool ShouldShow()
        {
            if (PauseDialButtonInjector.IsMounted) return false;
            if (UiBlocker.IsBlocking) return false;
            try
            {
                if (PlayerCamera.main == null || SettingsMenu.instance != null) return false;
                return IsGamePaused();
            }
            catch
            {
                return false;
            }
        }

        internal void Draw(Action onClick)
        {
            if (!ShouldShow()) return;

            float x = Screen.width - ButtonWidth - MarginRight;
            float y = Mathf.Clamp(Screen.height - ButtonHeight - MarginBottom, 12f, Screen.height - ButtonHeight - 12f);
            var rect = new Rect(x, y, ButtonWidth, ButtonHeight);

            bool clicked = false;
            BlackWhiteSkin.Push();
            try
            {
                clicked = GUI.Button(rect, SkinSyncI18n.T("app.menu_button"), BlackWhiteSkin.OverlayBtnStyle);
                BlackWhiteSkin.DrawBorder(rect, BorderThickness);
            }
            finally
            {
                BlackWhiteSkin.Pop();
            }

            if (clicked)
            {
                try { onClick?.Invoke(); }
                catch (Exception ex) { ModLog.Warning($"暂停菜单入口按钮点击异常：{ex.Message}"); }
                GUIUtility.ExitGUI();
            }
        }
    }
}
