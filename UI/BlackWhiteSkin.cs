using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>
    /// 黑底白线条风格 GUISkin。OnGUI 顶层 Push、绘制结束 Pop 即可全局生效。
    /// 与 SaveManager 同款风格保持视觉一致；两份 mod 各持一份避免跨 dll 依赖。
    /// </summary>
    internal static class BlackWhiteSkin
    {
        private static GUISkin _skin;
        private static Texture2D _bgPanel;
        private static Texture2D _bgButton;
        private static Texture2D _bgButtonHover;
        private static Texture2D _bgCard;
        private static Texture2D _bgTab;
        private static Texture2D _bgTabActive;
        private static Texture2D _line;

        private static GUIStyle _hStyle;
        private static GUIStyle _hButtonStyle;
        private static GUIStyle _cardStyle;
        private static GUIStyle _tabStyle;
        private static GUIStyle _tabActiveStyle;
        private static GUIStyle _lineStyle;
        private static GUIStyle _iconBtnStyle;

        internal static GUIStyle HeaderStyle => _hStyle;
        internal static GUIStyle HeaderButtonStyle => _hButtonStyle;
        internal static GUIStyle CardStyle => _cardStyle;
        internal static GUIStyle TabStyle => _tabStyle;
        internal static GUIStyle TabActiveStyle => _tabActiveStyle;
        internal static GUIStyle LineStyle => _lineStyle;
        internal static GUIStyle IconBtnStyle => _iconBtnStyle;
        internal static Texture2D LineTex => _line;

        private static GUISkin _previousSkin;

        internal static void Push()
        {
            EnsureBuilt();
            _previousSkin = GUI.skin;
            GUI.skin = _skin;
        }

        internal static void Pop()
        {
            GUI.skin = _previousSkin;
        }

        internal static void DrawHLine(Rect rect)
        {
            EnsureBuilt();
            GUI.DrawTexture(rect, _line, ScaleMode.StretchToFill);
        }

        internal static void DrawBorder(Rect rect)
        {
            EnsureBuilt();
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 1f), _line, ScaleMode.StretchToFill);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), _line, ScaleMode.StretchToFill);
            GUI.DrawTexture(new Rect(rect.x, rect.y, 1f, rect.height), _line, ScaleMode.StretchToFill);
            GUI.DrawTexture(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), _line, ScaleMode.StretchToFill);
        }

        internal static void DrawBorder(Rect rect, float thickness)
        {
            EnsureBuilt();
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), _line, ScaleMode.StretchToFill);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), _line, ScaleMode.StretchToFill);
            GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), _line, ScaleMode.StretchToFill);
            GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), _line, ScaleMode.StretchToFill);
        }

        internal static void DrawCloseX(Rect rect, float thickness = 2f)
        {
            EnsureBuilt();
            DrawDiagonalLine(rect.x, rect.y, rect.xMax, rect.yMax, thickness);
            DrawDiagonalLine(rect.x, rect.yMax, rect.xMax, rect.y, thickness);
        }

        private static void DrawDiagonalLine(float x1, float y1, float x2, float y2, float thickness)
        {
            float dx = x2 - x1;
            float dy = y2 - y1;
            float length = Mathf.Sqrt(dx * dx + dy * dy);
            float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
            var pivot = new Vector2(x1, y1);
            var matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, pivot);
            GUI.DrawTexture(new Rect(x1, y1 - thickness * 0.5f, length, thickness), _line, ScaleMode.StretchToFill);
            GUI.matrix = matrix;
        }

        private static void EnsureBuilt()
        {
            if (_skin != null) return;

            _bgPanel = MakeColorTex(0, 0, 0, 235);
            _bgButton = MakeColorTex(0, 0, 0, 220);
            _bgButtonHover = MakeColorTex(40, 40, 40, 235);
            _bgCard = MakeColorTex(15, 15, 15, 230);
            _bgTab = MakeColorTex(0, 0, 0, 220);
            _bgTabActive = MakeColorTex(255, 255, 255, 235);
            _line = MakeColorTex(255, 255, 255, 255);

            _skin = ScriptableObject.CreateInstance<GUISkin>();
            CopyDefaultsInto(_skin);

            ApplyMonochrome(_skin.box, _bgPanel, Color.white);
            ApplyMonochrome(_skin.window, _bgPanel, Color.white);
            ApplyMonochrome(_skin.button, _bgButton, Color.white, _bgButtonHover);
            ApplyMonochrome(_skin.label, null, Color.white);
            ApplyMonochrome(_skin.textField, _bgCard, Color.white);
            ApplyMonochrome(_skin.textArea, _bgCard, Color.white);
            ApplyMonochrome(_skin.toggle, null, Color.white);
            ApplyMonochrome(_skin.horizontalSlider, _bgCard, Color.white);
            ApplyMonochrome(_skin.horizontalSliderThumb, _line, Color.black);
            ApplyMonochrome(_skin.verticalScrollbar, _bgCard, Color.white);
            ApplyMonochrome(_skin.verticalScrollbarThumb, _line, Color.black);
            ApplyMonochrome(_skin.scrollView, _bgPanel, Color.white);

            _skin.horizontalSlider.fixedHeight = 28f;
            _skin.horizontalSlider.padding = new RectOffset(2, 2, 10, 10);
            _skin.horizontalSliderThumb.fixedHeight = 44f;
            _skin.horizontalSliderThumb.fixedWidth = 36f;
            _skin.verticalScrollbar.fixedWidth = 24f;
            _skin.verticalScrollbarThumb.fixedWidth = 24f;

            _skin.button.alignment = TextAnchor.MiddleCenter;
            _skin.button.fontSize = 20;
            _skin.button.fontStyle = FontStyle.Bold;
            _skin.button.padding = new RectOffset(14, 14, 10, 10);
            _skin.label.fontSize = 20;
            _skin.label.fontStyle = FontStyle.Bold;
            _skin.textField.fontSize = 20;
            _skin.textField.fontStyle = FontStyle.Bold;
            _skin.textField.padding = new RectOffset(12, 12, 8, 8);
            _skin.toggle.fontSize = 20;
            _skin.toggle.fontStyle = FontStyle.Bold;
            _skin.box.fontSize = 20;
            _skin.box.fontStyle = FontStyle.Bold;

            _hStyle = new GUIStyle(_skin.label)
            {
                fontSize = 32,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
            };

            _hButtonStyle = new GUIStyle(_skin.button)
            {
                fontSize = 28,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(20, 20, 12, 12),
            };
            ApplyMonochrome(_hButtonStyle, _bgButton, Color.white, _bgButtonHover);

            _cardStyle = new GUIStyle(_skin.box) { padding = new RectOffset(20, 20, 18, 18) };
            ApplyMonochrome(_cardStyle, _bgCard, Color.white);

            _tabStyle = new GUIStyle(_skin.button)
            {
                fontSize = 26,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
            ApplyMonochrome(_tabStyle, _bgTab, Color.white);

            _tabActiveStyle = new GUIStyle(_tabStyle);
            ApplyMonochrome(_tabActiveStyle, _bgTabActive, Color.black);

            _iconBtnStyle = new GUIStyle(_skin.button)
            {
                padding = new RectOffset(2, 2, 2, 2),
                margin = new RectOffset(2, 2, 0, 0),
                alignment = TextAnchor.MiddleCenter,
                fontSize = 22,
                fontStyle = FontStyle.Bold,
            };
            ApplyMonochrome(_iconBtnStyle, _bgButton, Color.white, _bgButtonHover);

            _lineStyle = new GUIStyle(_skin.label) { fixedHeight = 1f };
        }

        private static void CopyDefaultsInto(GUISkin dst)
        {
            var defaults = GUI.skin;
            dst.font = defaults.font;
            dst.box = new GUIStyle(defaults.box);
            dst.window = new GUIStyle(defaults.window);
            dst.button = new GUIStyle(defaults.button);
            dst.label = new GUIStyle(defaults.label);
            dst.textField = new GUIStyle(defaults.textField);
            dst.textArea = new GUIStyle(defaults.textArea);
            dst.toggle = new GUIStyle(defaults.toggle);
            dst.horizontalSlider = new GUIStyle(defaults.horizontalSlider);
            dst.horizontalSliderThumb = new GUIStyle(defaults.horizontalSliderThumb);
            dst.verticalScrollbar = new GUIStyle(defaults.verticalScrollbar);
            dst.verticalScrollbarThumb = new GUIStyle(defaults.verticalScrollbarThumb);
            dst.scrollView = new GUIStyle(defaults.scrollView);
        }

        private static void ApplyMonochrome(GUIStyle s, Texture2D bg, Color text, Texture2D hoverBg = null)
        {
            if (bg != null)
            {
                s.normal.background = bg;
                s.onNormal.background = bg;
                s.active.background = hoverBg ?? bg;
                s.onActive.background = hoverBg ?? bg;
                s.focused.background = bg;
                s.onFocused.background = bg;
                s.hover.background = hoverBg ?? bg;
                s.onHover.background = hoverBg ?? bg;
            }
            s.normal.textColor = text;
            s.onNormal.textColor = text;
            s.active.textColor = text;
            s.onActive.textColor = text;
            s.focused.textColor = text;
            s.onFocused.textColor = text;
            s.hover.textColor = text;
            s.onHover.textColor = text;
        }

        private static Texture2D MakeColorTex(byte r, byte g, byte b, byte a)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            var c = new Color32(r, g, b, a);
            var pixels = new Color32[4] { c, c, c, c };
            t.SetPixels32(pixels);
            t.Apply();
            return t;
        }
    }
}
