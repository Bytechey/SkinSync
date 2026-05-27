using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>
    /// 游戏内（ESC 暂停 brightnessPanel 打开时）的 IMGUI 浮动唤起按钮。
    /// 主菜单按钮已迁移到 MenuButtonInjector，本类只负责暂停面板的入口。
    /// </summary>
    internal sealed class InGameOverlay
    {
        private const float ButtonWidth = 320f;
        private const float ButtonHeight = 84f;
        private const float MarginRight = 24f;
        // SaveManager overlay 占屏幕右下 (right=24, bottom=24)，
        // SkinSync 上移一格避免重叠：bottom = 24 + 84 + 16 = 124。
        private const float MarginBottom = 124f;

        internal static bool ShouldShow()
        {
            if (PlayerCamera.main == null) return false;
            var panel = PlayerCamera.main.brightnessPanel;
            return panel != null && panel.activeSelf;
        }

        internal void Draw(System.Action onClick)
        {
            if (!ShouldShow()) return;

            float x = Screen.width - ButtonWidth - MarginRight;
            float y = Screen.height - ButtonHeight - MarginBottom;
            var rect = new Rect(x, y, ButtonWidth, ButtonHeight);

            BlackWhiteSkin.Push();
            try
            {
                if (GUI.Button(rect, SkinSyncI18n.T("app.menu_button"))) onClick?.Invoke();
                BlackWhiteSkin.DrawBorder(rect, 5f);
            }
            finally
            {
                BlackWhiteSkin.Pop();
            }
        }
    }
}
