using System.Collections.Generic;
using UnityEngine;

namespace SkinSyncMod
{
    // 黑白线条风格 GUISkin 与九宫格纹理，独立窗口与嵌入 CuSaveManager 时共用同一套视觉参数
    internal static class BlackWhiteSkin
    {
        private static GUISkin _skin;
        private static Texture2D _bgPanel;
        private static Texture2D _bgCard;
        private static Texture2D _bgCardCurrent;
        private static Texture2D _bgPreview;
        private static Texture2D _bgInput;
        private static Texture2D _bgInputFocus;
        private static Texture2D _bgButton;
        private static Texture2D _bgButtonHover;
        private static Texture2D _bgButtonActive;
        private static Texture2D _bgControlBtn;
        private static Texture2D _bgControlBtnHover;
        private static Texture2D _bgControlBtnPress;
        private static Texture2D _bgTabActive;
        private static Texture2D _bgControlBtnActive;
        private static Texture2D _bgBadge;
        private static Texture2D _bgSliderTrack;
        private static Texture2D _line;

        private static GUIStyle _headerStyle;
        private static GUIStyle _sectionHeaderStyle;
        private static GUIStyle _headerButtonStyle;
        private static GUIStyle _overlayBtnStyle;
        private static GUIStyle _tabStyle;
        private static GUIStyle _tabActiveStyle;
        private static GUIStyle _buttonStyle;
        private static GUIStyle _controlBtnStyle;
        private static GUIStyle _controlBtnActiveStyle;
        private static GUIStyle _cardStyle;
        private static GUIStyle _cardCurrentStyle;
        private static GUIStyle _skinCellStyle;
        private static GUIStyle _previewBoxStyle;
        private static GUIStyle _rowLabelStyle;
        private static GUIStyle _singleLineLabelStyle;
        private static GUIStyle _skinNameStyle;
        private static GUIStyle _bodyTextStyle;
        private static GUIStyle _mutedLabelStyle;
        private static GUIStyle _mutedRightStyle;
        private static GUIStyle _valueBoxStyle;
        private static GUIStyle _badgeStyle;
        private static GUIStyle _badgeActiveStyle;
        private static GUIStyle _aboutTitleStyle;
        private static GUIStyle _aboutBodyStyle;

        internal static GUIStyle HeaderStyle { get { EnsureBuilt(); return _headerStyle; } }
        internal static GUIStyle SectionHeaderStyle { get { EnsureBuilt(); return _sectionHeaderStyle; } }
        internal static GUIStyle HeaderButtonStyle { get { EnsureBuilt(); return _headerButtonStyle; } }
        internal static GUIStyle OverlayBtnStyle { get { EnsureBuilt(); return _overlayBtnStyle; } }
        internal static GUIStyle TabStyle { get { EnsureBuilt(); return _tabStyle; } }
        internal static GUIStyle TabActiveStyle { get { EnsureBuilt(); return _tabActiveStyle; } }
        internal static GUIStyle ButtonStyle { get { EnsureBuilt(); return _buttonStyle; } }
        internal static GUIStyle ControlBtnStyle { get { EnsureBuilt(); return _controlBtnStyle; } }
        internal static GUIStyle ControlBtnActiveStyle { get { EnsureBuilt(); return _controlBtnActiveStyle; } }
        internal static GUIStyle CardStyle { get { EnsureBuilt(); return _cardStyle; } }
        internal static GUIStyle CardCurrentStyle { get { EnsureBuilt(); return _cardCurrentStyle; } }
        internal static GUIStyle SkinCellStyle { get { EnsureBuilt(); return _skinCellStyle; } }
        internal static GUIStyle PreviewBoxStyle { get { EnsureBuilt(); return _previewBoxStyle; } }
        internal static GUIStyle RowLabelStyle { get { EnsureBuilt(); return _rowLabelStyle; } }
        internal static GUIStyle SingleLineLabelStyle { get { EnsureBuilt(); return _singleLineLabelStyle; } }
        internal static GUIStyle SkinNameStyle { get { EnsureBuilt(); return _skinNameStyle; } }
        internal static GUIStyle BodyTextStyle { get { EnsureBuilt(); return _bodyTextStyle; } }
        internal static GUIStyle MutedLabelStyle { get { EnsureBuilt(); return _mutedLabelStyle; } }
        internal static GUIStyle MutedRightStyle { get { EnsureBuilt(); return _mutedRightStyle; } }
        internal static GUIStyle ValueBoxStyle { get { EnsureBuilt(); return _valueBoxStyle; } }
        internal static GUIStyle BadgeStyle { get { EnsureBuilt(); return _badgeStyle; } }
        internal static GUIStyle BadgeActiveStyle { get { EnsureBuilt(); return _badgeActiveStyle; } }
        internal static GUIStyle AboutTitleStyle { get { EnsureBuilt(); return _aboutTitleStyle; } }
        internal static GUIStyle AboutBodyStyle { get { EnsureBuilt(); return _aboutBodyStyle; } }
        internal static GUIStyle SliderTrackStyle { get { EnsureBuilt(); return _skin.horizontalSlider; } }
        internal static GUIStyle SliderThumbStyle { get { EnsureBuilt(); return _skin.horizontalSliderThumb; } }
        internal static GUIStyle VerticalScrollbarStyle { get { EnsureBuilt(); return _skin.verticalScrollbar; } }
        internal static GUIStyle ScrollViewStyle { get { EnsureBuilt(); return _skin.scrollView; } }

        // 使用栈保存外层皮肤，防止嵌入宿主面板或嵌套绘制时冲掉上层 GUI.skin
        private static readonly Stack<GUISkin> SkinStack = new Stack<GUISkin>(4);

        internal static void Push()
        {
            EnsureBuilt();
            SkinStack.Push(GUI.skin);
            GUI.skin = _skin;
        }

        internal static void Pop()
        {
            if (SkinStack.Count > 0)
            {
                GUI.skin = SkinStack.Pop();
            }
        }

        internal static void Dispose()
        {
            SkinStack.Clear();
            DestroyObject(ref _skin);
            DestroyTexture(ref _bgPanel);
            DestroyTexture(ref _bgCard);
            DestroyTexture(ref _bgCardCurrent);
            DestroyTexture(ref _bgPreview);
            DestroyTexture(ref _bgInput);
            DestroyTexture(ref _bgInputFocus);
            DestroyTexture(ref _bgButton);
            DestroyTexture(ref _bgButtonHover);
            DestroyTexture(ref _bgButtonActive);
            DestroyTexture(ref _bgControlBtn);
            DestroyTexture(ref _bgControlBtnHover);
            DestroyTexture(ref _bgControlBtnPress);
            DestroyTexture(ref _bgTabActive);
            DestroyTexture(ref _bgControlBtnActive);
            DestroyTexture(ref _bgBadge);
            DestroyTexture(ref _bgSliderTrack);
            DestroyTexture(ref _line);

            _headerStyle = null;
            _sectionHeaderStyle = null;
            _headerButtonStyle = null;
            _overlayBtnStyle = null;
            _tabStyle = null;
            _tabActiveStyle = null;
            _buttonStyle = null;
            _controlBtnStyle = null;
            _controlBtnActiveStyle = null;
            _cardStyle = null;
            _cardCurrentStyle = null;
            _skinCellStyle = null;
            _previewBoxStyle = null;
            _rowLabelStyle = null;
            _singleLineLabelStyle = null;
            _skinNameStyle = null;
            _bodyTextStyle = null;
            _mutedLabelStyle = null;
            _mutedRightStyle = null;
            _valueBoxStyle = null;
            _badgeStyle = null;
            _badgeActiveStyle = null;
            _aboutTitleStyle = null;
            _aboutBodyStyle = null;
        }

        internal static void DrawHLine(Rect rect)
        {
            EnsureBuilt();
            GUI.DrawTexture(rect, _line, ScaleMode.StretchToFill);
        }

        internal static void DrawSoftVLine(Rect rect)
        {
            EnsureBuilt();
            Color prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.45f);
            GUI.DrawTexture(rect, _line, ScaleMode.StretchToFill);
            GUI.color = prev;
        }

        internal static void DrawBorder(Rect rect) => DrawBorder(rect, 1f);

        internal static void DrawBorder(Rect rect, float thickness)
        {
            EnsureBuilt();
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), _line, ScaleMode.StretchToFill);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), _line, ScaleMode.StretchToFill);
            GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), _line, ScaleMode.StretchToFill);
            GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), _line, ScaleMode.StretchToFill);
        }

        internal static void DrawCloseX(Rect rect, float thickness = 5f)
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
            // 沿对角线铺轴对齐方块，避免外层缩放矩阵下旋转绘制产生纹理走样
            int steps = Mathf.Max(2, Mathf.CeilToInt(length / Mathf.Max(0.5f, thickness * 0.5f)));
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float x = x1 + dx * t;
                float y = y1 + dy * t;
                GUI.DrawTexture(new Rect(x - thickness * 0.5f, y - thickness * 0.5f, thickness, thickness),
                    _line, ScaleMode.StretchToFill);
            }
        }

        private static void EnsureBuilt()
        {
            if (_skin != null) return;

            _bgPanel = MakeColorTex(0, 0, 0, 125);
            _bgCard = MakeBorderedTex(15, 15, 15, 105, 255, 255, 255, 100, 1);
            _bgCardCurrent = MakeBorderedTex(15, 15, 15, 125, 255, 255, 255, 255, 2);
            _bgPreview = MakeBorderedTex(0, 0, 0, 130, 255, 255, 255, 130, 1);
            _bgInput = MakeBorderedTex(0, 0, 0, 150, 255, 255, 255, 130, 1);
            _bgInputFocus = MakeBorderedTex(0, 0, 0, 150, 255, 255, 255, 255, 2);
            _bgButton = MakeBorderedTex(0, 0, 0, 150, 255, 255, 255, 220, 2);
            _bgButtonHover = MakeBorderedTex(60, 60, 60, 190, 255, 255, 255, 255, 2);
            _bgButtonActive = MakeBorderedTex(90, 90, 90, 210, 255, 255, 255, 255, 2);
            _bgControlBtn = MakeBorderedTex(0, 0, 0, 130, 255, 255, 255, 160, 1);
            _bgControlBtnHover = MakeBorderedTex(60, 60, 60, 190, 255, 255, 255, 240, 1);
            _bgControlBtnPress = MakeBorderedTex(90, 90, 90, 210, 255, 255, 255, 255, 1);
            _bgTabActive = MakeBorderedTex(255, 255, 255, 235, 255, 255, 255, 255, 2);
            _bgControlBtnActive = MakeBorderedTex(255, 255, 255, 235, 255, 255, 255, 255, 2);
            _bgBadge = MakeBorderedTex(0, 0, 0, 150, 255, 255, 255, 160, 1);
            _bgSliderTrack = MakeBorderedTex(0, 0, 0, 150, 255, 255, 255, 130, 1);
            _line = MakeColorTex(255, 255, 255, 255);

            _skin = ScriptableObject.CreateInstance<GUISkin>();
            _skin.hideFlags = HideFlags.HideAndDontSave;
            CopyDefaultsInto(_skin);

            ApplyMonochrome(_skin.box, _bgPanel, Color.white);
            ApplyMonochrome(_skin.window, _bgPanel, Color.white);
            ApplyMonochrome(_skin.button, _bgButton, Color.white, _bgButtonHover, _bgButtonActive);
            ApplyMonochrome(_skin.label, null, Color.white);
            ApplyInputStates(_skin.textField, _bgInput, _bgInputFocus, Color.white);
            ApplyInputStates(_skin.textArea, _bgInput, _bgInputFocus, Color.white);
            ApplyMonochrome(_skin.toggle, null, Color.white);
            ApplyMonochrome(_skin.horizontalSlider, _bgSliderTrack, Color.white);
            ApplyMonochrome(_skin.horizontalSliderThumb, _line, Color.black);
            ApplyMonochrome(_skin.verticalScrollbar, _bgSliderTrack, Color.white);
            ApplyMonochrome(_skin.verticalScrollbarThumb, _line, Color.black);

            // scrollView 背景置空，避免嵌入 CuSaveManager 时与宿主面板底色二次叠暗
            ClearBackgrounds(_skin.scrollView);
            _skin.scrollView.border = new RectOffset(0, 0, 0, 0);
            _skin.scrollView.padding = new RectOffset(0, 0, 0, 0);
            _skin.scrollView.margin = new RectOffset(0, 0, 0, 0);

            // CopyDefaultsInto 首帧可能从外部宿主 skin 拷贝，必须显式写死所用布局字段以消除跨模组耦合
            // 14px 导轨配上下 -7 padding，使 28px 纯白滑块头垂直居中跨骑于导轨
            _skin.horizontalSlider.fixedHeight = 14f;
            _skin.horizontalSlider.fixedWidth = 0f;
            _skin.horizontalSlider.border = new RectOffset(1, 1, 1, 1);
            _skin.horizontalSlider.margin = new RectOffset(4, 4, 16, 16);
            _skin.horizontalSlider.padding = new RectOffset(0, 0, -7, -7);
            _skin.horizontalSlider.overflow = new RectOffset(0, 0, 0, 0);

            _skin.horizontalSliderThumb.fixedWidth = 16f;
            _skin.horizontalSliderThumb.fixedHeight = 28f;
            _skin.horizontalSliderThumb.border = new RectOffset(0, 0, 0, 0);
            _skin.horizontalSliderThumb.margin = new RectOffset(0, 0, 0, 0);
            _skin.horizontalSliderThumb.padding = new RectOffset(0, 0, 0, 0);
            _skin.horizontalSliderThumb.overflow = new RectOffset(0, 0, 0, 0);

            _skin.verticalScrollbar.fixedWidth = 14f;
            _skin.verticalScrollbar.border = new RectOffset(1, 1, 1, 1);
            _skin.verticalScrollbar.margin = new RectOffset(0, 0, 0, 0);
            _skin.verticalScrollbar.padding = new RectOffset(0, 0, 0, 0);
            _skin.verticalScrollbarThumb.fixedWidth = 14f;
            _skin.verticalScrollbarThumb.border = new RectOffset(0, 0, 0, 0);
            _skin.verticalScrollbarThumb.margin = new RectOffset(0, 0, 0, 0);
            _skin.verticalScrollbarThumb.padding = new RectOffset(0, 0, 0, 0);

            _skin.button.alignment = TextAnchor.MiddleCenter;
            _skin.button.fontSize = 20;
            _skin.button.fontStyle = FontStyle.Bold;
            _skin.button.border = new RectOffset(2, 2, 2, 2);
            _skin.button.padding = new RectOffset(14, 14, 5, 5);
            _skin.button.margin = new RectOffset(0, 0, 0, 0);
            _skin.button.wordWrap = false;
            _skin.button.clipping = TextClipping.Overflow;

            _skin.label.alignment = TextAnchor.MiddleLeft;
            _skin.label.fontSize = 20;
            _skin.label.fontStyle = FontStyle.Bold;
            _skin.label.padding = new RectOffset(2, 2, 2, 2);
            _skin.label.margin = new RectOffset(0, 0, 0, 0);
            _skin.label.wordWrap = false;
            _skin.label.clipping = TextClipping.Overflow;

            _skin.textField.alignment = TextAnchor.MiddleLeft;
            _skin.textField.fontSize = 20;
            _skin.textField.fontStyle = FontStyle.Bold;
            _skin.textField.border = new RectOffset(1, 1, 1, 1);
            _skin.textField.padding = new RectOffset(10, 10, 5, 5);
            _skin.textField.margin = new RectOffset(0, 0, 0, 0);
            _skin.textField.wordWrap = false;
            _skin.textField.clipping = TextClipping.Clip;

            _skin.toggle.fontSize = 20;
            _skin.toggle.fontStyle = FontStyle.Bold;
            _skin.toggle.wordWrap = false;
            _skin.box.fontSize = 20;
            _skin.box.fontStyle = FontStyle.Bold;
            _skin.box.wordWrap = false;
            _skin.box.border = new RectOffset(0, 0, 0, 0);

            BuildStyles();
        }

        private static void BuildStyles()
        {
            _headerStyle = new GUIStyle(_skin.label)
            {
                fontSize = 32,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false,
                clipping = TextClipping.Overflow,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
            };

            _sectionHeaderStyle = new GUIStyle(_skin.label)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false,
                clipping = TextClipping.Overflow,
                padding = new RectOffset(2, 2, 2, 2),
                margin = new RectOffset(0, 0, 0, 0),
            };

            _headerButtonStyle = new GUIStyle(_skin.button)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                border = new RectOffset(2, 2, 2, 2),
                padding = new RectOffset(16, 16, 6, 6),
                margin = new RectOffset(0, 0, 0, 0),
                wordWrap = false,
                clipping = TextClipping.Clip,
            };
            ApplyMonochrome(_headerButtonStyle, _bgButton, Color.white, _bgButtonHover, _bgButtonActive);

            _overlayBtnStyle = new GUIStyle(_skin.button)
            {
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(2, 2, 2, 2),
                padding = new RectOffset(16, 16, 8, 8),
                margin = new RectOffset(0, 0, 0, 0),
                wordWrap = false,
                clipping = TextClipping.Overflow,
            };
            ApplyMonochrome(_overlayBtnStyle, _bgButton, Color.white, _bgButtonHover, _bgButtonActive);

            _tabStyle = new GUIStyle(_skin.button)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(2, 2, 2, 2),
                padding = new RectOffset(14, 14, 6, 6),
                margin = new RectOffset(0, 0, 0, 0),
                wordWrap = false,
                clipping = TextClipping.Overflow,
            };
            ApplyMonochrome(_tabStyle, _bgButton, Color.white, _bgButtonHover, _bgButtonActive);

            _tabActiveStyle = new GUIStyle(_tabStyle);
            ApplyMonochrome(_tabActiveStyle, _bgTabActive, Color.black);

            _buttonStyle = new GUIStyle(_skin.button)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(2, 2, 2, 2),
                padding = new RectOffset(14, 14, 5, 5),
                margin = new RectOffset(0, 0, 0, 0),
                wordWrap = false,
                clipping = TextClipping.Overflow,
            };
            ApplyMonochrome(_buttonStyle, _bgButton, Color.white, _bgButtonHover, _bgButtonActive);

            _controlBtnStyle = new GUIStyle(_skin.button)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(1, 1, 1, 1),
                padding = new RectOffset(10, 10, 4, 4),
                margin = new RectOffset(0, 0, 0, 0),
                wordWrap = false,
                clipping = TextClipping.Overflow,
            };
            ApplyMonochrome(_controlBtnStyle, _bgControlBtn, Color.white, _bgControlBtnHover, _bgControlBtnPress);

            _controlBtnActiveStyle = new GUIStyle(_controlBtnStyle)
            {
                border = new RectOffset(2, 2, 2, 2),
            };
            ApplyMonochrome(_controlBtnActiveStyle, _bgControlBtnActive, Color.black);

            _cardStyle = new GUIStyle(_skin.box)
            {
                border = new RectOffset(1, 1, 1, 1),
                padding = new RectOffset(16, 16, 12, 12),
                margin = new RectOffset(0, 0, 2, 2),
            };
            ApplyMonochrome(_cardStyle, _bgCard, Color.white);

            _skinCellStyle = new GUIStyle(_skin.box)
            {
                border = new RectOffset(1, 1, 1, 1),
                padding = new RectOffset(10, 10, 10, 10),
                margin = new RectOffset(0, 0, 0, 0),
            };
            ApplyMonochrome(_skinCellStyle, _bgCard, Color.white);

            _cardCurrentStyle = new GUIStyle(_skin.box)
            {
                border = new RectOffset(2, 2, 2, 2),
                padding = new RectOffset(10, 10, 10, 10),
                margin = new RectOffset(0, 0, 0, 0),
            };
            ApplyMonochrome(_cardCurrentStyle, _bgCardCurrent, Color.white);

            _previewBoxStyle = new GUIStyle(_skin.box)
            {
                border = new RectOffset(1, 1, 1, 1),
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
            };
            ApplyMonochrome(_previewBoxStyle, _bgPreview, Color.white);

            _rowLabelStyle = new GUIStyle(_skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true,
                clipping = TextClipping.Clip,
                padding = new RectOffset(0, 8, 4, 4),
                margin = new RectOffset(0, 0, 0, 0),
            };

            _singleLineLabelStyle = new GUIStyle(_skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false,
                clipping = TextClipping.Clip,
                padding = new RectOffset(0, 6, 2, 2),
                margin = new RectOffset(0, 0, 0, 0),
            };

            _skinNameStyle = new GUIStyle(_skin.label)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
                clipping = TextClipping.Clip,
                padding = new RectOffset(4, 4, 2, 2),
                margin = new RectOffset(0, 0, 0, 0),
            };

            _bodyTextStyle = new GUIStyle(_skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true,
                clipping = TextClipping.Clip,
                padding = new RectOffset(2, 2, 4, 4),
                margin = new RectOffset(0, 0, 0, 0),
            };
            ApplyMonochrome(_bodyTextStyle, null, new Color(1f, 1f, 1f, 0.88f));

            _mutedLabelStyle = new GUIStyle(_skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false,
                clipping = TextClipping.Overflow,
                padding = new RectOffset(2, 2, 2, 2),
                margin = new RectOffset(0, 0, 0, 0),
            };
            ApplyMonochrome(_mutedLabelStyle, null, new Color(1f, 1f, 1f, 0.80f));

            _mutedRightStyle = new GUIStyle(_mutedLabelStyle)
            {
                alignment = TextAnchor.MiddleRight,
                padding = new RectOffset(4, 2, 2, 2),
            };

            _valueBoxStyle = new GUIStyle(_skin.box)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(1, 1, 1, 1),
                padding = new RectOffset(8, 8, 4, 4),
                margin = new RectOffset(0, 0, 0, 0),
                wordWrap = false,
                clipping = TextClipping.Overflow,
            };
            ApplyMonochrome(_valueBoxStyle, _bgBadge, Color.white);

            _badgeStyle = new GUIStyle(_skin.box)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(1, 1, 1, 1),
                padding = new RectOffset(8, 8, 3, 3),
                margin = new RectOffset(0, 0, 0, 0),
                wordWrap = false,
                clipping = TextClipping.Overflow,
            };
            ApplyMonochrome(_badgeStyle, _bgBadge, Color.white);

            _badgeActiveStyle = new GUIStyle(_badgeStyle)
            {
                border = new RectOffset(2, 2, 2, 2),
            };
            ApplyMonochrome(_badgeActiveStyle, _bgControlBtnActive, Color.black);

            _aboutTitleStyle = new GUIStyle(_skin.label)
            {
                fontSize = 32,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
                clipping = TextClipping.Overflow,
                padding = new RectOffset(4, 4, 2, 2),
                margin = new RectOffset(0, 0, 0, 0),
            };

            _aboutBodyStyle = new GUIStyle(_skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                clipping = TextClipping.Clip,
                padding = new RectOffset(4, 4, 4, 4),
                margin = new RectOffset(0, 0, 0, 0),
            };
            ApplyMonochrome(_aboutBodyStyle, null, new Color(1f, 1f, 1f, 0.88f));
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

        private static void ClearBackgrounds(GUIStyle s)
        {
            s.normal.background = null;
            s.onNormal.background = null;
            s.active.background = null;
            s.onActive.background = null;
            s.focused.background = null;
            s.onFocused.background = null;
            s.hover.background = null;
            s.onHover.background = null;
        }

        private static void ApplyInputStates(GUIStyle s, Texture2D normalBg, Texture2D focusBg, Color text)
        {
            s.normal.background = normalBg;
            s.onNormal.background = normalBg;
            s.hover.background = normalBg;
            s.onHover.background = normalBg;
            s.active.background = focusBg;
            s.onActive.background = focusBg;
            s.focused.background = focusBg;
            s.onFocused.background = focusBg;
            SetAllTextColors(s, text);
        }

        private static void ApplyMonochrome(GUIStyle s, Texture2D bg, Color text,
            Texture2D hoverBg = null, Texture2D activeBg = null)
        {
            if (bg != null)
            {
                Texture2D hBg = hoverBg != null ? hoverBg : bg;
                Texture2D aBg = activeBg != null ? activeBg : hBg;
                s.normal.background = bg;
                s.onNormal.background = bg;
                s.hover.background = hBg;
                s.onHover.background = hBg;
                s.active.background = aBg;
                s.onActive.background = aBg;
                s.focused.background = bg;
                s.onFocused.background = bg;
            }
            else
            {
                ClearBackgrounds(s);
            }
            SetAllTextColors(s, text);
        }

        private static void SetAllTextColors(GUIStyle s, Color text)
        {
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
            t.SetPixels32(new[] { c, c, c, c });
            t.Apply();
            return t;
        }

        private static Texture2D MakeBorderedTex(byte fr, byte fg, byte fb, byte fa,
            byte br, byte bg, byte bb, byte ba, int borderPx)
        {
            // 内部保留 4x4 纯色中心区，保证九宫格 border 与 borderPx 一致且拉伸不晕染
            int size = borderPx * 2 + 4;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            var fill = new Color32(fr, fg, fb, fa);
            var edge = new Color32(br, bg, bb, ba);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool isEdge = x < borderPx || x >= size - borderPx || y < borderPx || y >= size - borderPx;
                    pixels[y * size + x] = isEdge ? edge : fill;
                }
            }
            t.SetPixels32(pixels);
            t.Apply();
            return t;
        }

        private static void DestroyTexture(ref Texture2D tex)
        {
            if (tex != null)
            {
                try { Object.Destroy(tex); } catch { }
                tex = null;
            }
        }

        private static void DestroyObject<T>(ref T obj) where T : Object
        {
            if (obj != null)
            {
                try { Object.Destroy(obj); } catch { }
                obj = null;
            }
        }
    }
}
