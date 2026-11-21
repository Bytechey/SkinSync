using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>绘制并管理 SkinSync 配置主窗口与外部宿主嵌入面板。</summary>
    internal sealed partial class SkinSyncWindow
    {
        private const int WindowId = 0x5E15E150;
        private const float BaseWinW = 1440f;
        private const float BaseWinH = 900f;
        private const float MinWinW = 1180f;
        private const float MinWinH = 720f;
        private const float TitleBarHeight = 68f;
        private const float CloseBtnSize = 50f;
        private const float StatusBarHeight = 56f;

        private const float EmbeddedDefaultOuterW = 1140f;
        private const float ScrollbarReserveW = 26f;
        private const float LabelColW = 288f;
        private const float TabBarHeight = 48f;
        private const float ButtonHeight = 44f;
        private const float RowMinHeight = 46f;
        private const float ControlHeight = 40f;
        private const float BadgeHeight = 36f;
        private const float RowGap = 6f;
        private const float SubIndentW = 16f;

        private static readonly int[] ScalePresets = { 90, 100, 115, 130 };
        private static readonly string[] ScalePresetLabels = { "90%", "100%", "115%", "130%" };

        private static readonly string[] TabKeys =
        {
            "tab.settings",
            "tab.skins",
            "tab.current",
            "tab.about",
        };

        private static readonly GUIContent MeasureContent = new GUIContent();

        internal struct AccessoryRow
        {
            public string Id;
            public bool Enabled;
            public int OffX;
            public int OffY;
            public float Rotation;
            public int ZOrder;
            public string RequireSlot;
        }

        private readonly SkinSyncSettings _cfg;
        private readonly Func<IReadOnlyList<string>> _getSkins;
        private readonly Func<string> _getCurrentSkin;
        private readonly Action<string> _onApplySkin;
        private readonly Action _onRescan;
        private readonly Func<IReadOnlyList<AccessoryRow>> _getAccessories;
        private readonly Action<string, bool> _onToggleAccessory;
        private readonly Action<string, int, int, float, int> _onSetAccessoryTransform;
        private readonly Action _onSaveTail;
        private readonly Action _onResetTail;
        private readonly Action _onOpened;
        private readonly Action _onClosed;

        private Rect _rect = new Rect(120f, 60f, BaseWinW, BaseWinH);
        private float _drawScale = 1f;
        private float _winW = BaseWinW;
        private float _winH = BaseWinH;
        private float _lastDrawScale = 1f;
        private float _lastWinW = BaseWinW;
        private float _lastWinH = BaseWinH;
        private bool _posInitialized;
        private readonly float[] _scaleBtnWidths = new float[4];

        private int _tab;
        private Vector2 _settingsScroll;
        private Vector2 _skinsScroll;
        private Vector2 _accessoriesScroll;
        private Vector2 _aboutScroll;

        // 仅在 Layout 阶段同步外层容器实测宽度与倒计时快照，确保同帧 Layout/Repaint 控件几何一致
        private bool _drawingEmbedded;
        private bool _lastSnapshotEmbedded;
        private bool _hasLayoutSnapshot;
        private float _measuredStandaloneOuterW;
        private float _measuredEmbeddedOuterW;
        private float _layoutGridWidth;
        private float _measuredSkinsViewHeight;
        private float _layoutSkinsViewHeight;
        private float _tailResetConfirmUntil;
        private bool _tailResetConfirmActive;
        private bool _tailDirty;

        private bool _capturingPanel;
        private bool _capturingNext;
        private bool _capturingPrev;
        private bool _capturingRescan;
        private int _capturedFrame = -1;
        private bool _loggedDrawForCurrentOpen;

        internal bool Open { get; set; }
        internal bool IsCapturingHotkey => _capturingPanel || _capturingNext || _capturingPrev || _capturingRescan;
        internal bool WasHotkeyCapturedThisFrame => _capturedFrame == Time.frameCount;

        internal void OpenPanel()
        {
            if (Open) return;
            Open = true;
            _hasLayoutSnapshot = false;
            _tailResetConfirmUntil = 0f;
            _loggedDrawForCurrentOpen = false;
            ModLog.Debug("SkinSyncWindow.OpenPanel -> Open=true");
            _onOpened?.Invoke();
        }

        internal void ClosePanel()
        {
            if (!Open) return;
            FlushPendingTailSave();
            Open = false;
            _hasLayoutSnapshot = false;
            _tailResetConfirmUntil = 0f;
            _loggedDrawForCurrentOpen = false;
            CancelKeyCapture();
            ClearTextFocus();
            _onClosed?.Invoke();
        }

        internal SkinSyncWindow(
            SkinSyncSettings cfg,
            Func<IReadOnlyList<string>> getSkins,
            Func<string> getCurrentSkin,
            Action<string> onApplySkin,
            Action onRescan,
            Func<IReadOnlyList<AccessoryRow>> getAccessories,
            Action<string, bool> onToggleAccessory,
            Action<string, int, int, float, int> onSetAccessoryTransform,
            Action onSaveTail,
            Action onResetTail,
            Action onOpened,
            Action onClosed)
        {
            _cfg = cfg;
            _getSkins = getSkins;
            _getCurrentSkin = getCurrentSkin;
            _onApplySkin = onApplySkin;
            _onRescan = onRescan;
            _getAccessories = getAccessories;
            _onToggleAccessory = onToggleAccessory;
            _onSetAccessoryTransform = onSetAccessoryTransform;
            _onSaveTail = onSaveTail;
            _onResetTail = onResetTail;
            _onOpened = onOpened;
            _onClosed = onClosed;
        }

        internal void Draw()
        {
            if (!Open) return;
            ComputeWindowTransform();
            if (!_loggedDrawForCurrentOpen)
            {
                _loggedDrawForCurrentOpen = true;
                ModLog.Debug($"SkinSyncWindow.Draw screen=({Screen.width}x{Screen.height}) scale={_drawScale:F3} rect=({_rect.x:0},{_rect.y:0},{_rect.width:0},{_rect.height:0})");
            }

            BlackWhiteSkin.Push();
            Matrix4x4 prev = GUI.matrix;
            try
            {
                GUI.matrix = Matrix4x4.Scale(new Vector3(_drawScale, _drawScale, 1f));
                _rect = GUI.ModalWindow(WindowId, _rect, DrawContent, "");
            }
            catch (ExitGUIException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ModLog.Warning($"SkinSyncWindow.Draw 失败：{ex}");
                ClosePanel();
            }
            finally
            {
                GUI.matrix = prev;
                BlackWhiteSkin.Pop();
            }
        }

        // 按高分屏 DPI 基准与用户缩放百分比计算矩阵缩放与窗口尺寸，并在缩放切换时保持屏幕中心不漂移
        private void ComputeWindowTransform()
        {
            int effectiveScalePercent = Patches.SaveManagerTabBridge.GetEffectiveUiScalePercent(_cfg);
            float dpiBase = Mathf.Min(Screen.width / 1706.67f, Screen.height / 1066.67f);
            float desired = dpiBase * (effectiveScalePercent / 100f);
            float maxScaleW = Mathf.Max(0.4f, (Screen.width - 24f) / MinWinW);
            float maxScaleH = Mathf.Max(0.4f, (Screen.height - 24f) / MinWinH);
            float drawScale = Mathf.Clamp(desired, 0.4f, Mathf.Min(maxScaleW, maxScaleH));
            float winW = Mathf.Clamp(Mathf.Floor((Screen.width - 24f) / drawScale), MinWinW, BaseWinW);
            float winH = Mathf.Clamp(Mathf.Floor((Screen.height - 24f) / drawScale), MinWinH, BaseWinH);

            if (!_posInitialized)
            {
                _rect = new Rect(
                    (Screen.width / drawScale - winW) * 0.5f,
                    (Screen.height / drawScale - winH) * 0.5f,
                    winW,
                    winH);
                _posInitialized = true;
            }
            else if (!Mathf.Approximately(drawScale, _lastDrawScale)
                || !Mathf.Approximately(winW, _lastWinW)
                || !Mathf.Approximately(winH, _lastWinH))
            {
                float oldScreenCx = (_rect.x + _lastWinW * 0.5f) * _lastDrawScale;
                float oldScreenCy = (_rect.y + _lastWinH * 0.5f) * _lastDrawScale;
                _rect.x = oldScreenCx / drawScale - winW * 0.5f;
                _rect.y = oldScreenCy / drawScale - winH * 0.5f;
                _measuredStandaloneOuterW = 0f;
                _measuredSkinsViewHeight = 0f;
                _hasLayoutSnapshot = false;
            }

            _drawScale = drawScale;
            _winW = winW;
            _winH = winH;
            _lastDrawScale = drawScale;
            _lastWinW = winW;
            _lastWinH = winH;

            _rect.width = winW;
            _rect.height = winH;
            float maxX = Mathf.Max(0f, Screen.width / drawScale - winW);
            float maxY = Mathf.Max(0f, Screen.height / drawScale - winH);
            _rect.x = Mathf.Clamp(_rect.x, 0f, maxX);
            _rect.y = Mathf.Clamp(_rect.y, 0f, maxY);
        }

        private void SnapshotLayoutState(bool embedded)
        {
            if (_hasLayoutSnapshot && _lastSnapshotEmbedded == embedded
                && Event.current != null && Event.current.type != EventType.Layout)
            {
                return;
            }

            _hasLayoutSnapshot = true;
            _lastSnapshotEmbedded = embedded;
            _tailResetConfirmActive = Time.unscaledTime < _tailResetConfirmUntil;

            float measuredOuter = embedded ? _measuredEmbeddedOuterW : _measuredStandaloneOuterW;
            float fallbackOuter = embedded ? EmbeddedDefaultOuterW : (_winW - 48f);
            float outerW = measuredOuter > 200f ? measuredOuter : fallbackOuter;
            _layoutGridWidth = Mathf.Max(200f, outerW - ScrollbarReserveW);

            if (_measuredSkinsViewHeight > 100f)
            {
                _layoutSkinsViewHeight = _measuredSkinsViewHeight;
            }
        }

        internal void CancelKeyCapture()
        {
            _capturingPanel = false;
            _capturingNext = false;
            _capturingPrev = false;
            _capturingRescan = false;
        }

        private void FlushPendingTailSave()
        {
            if (!_tailDirty) return;
            _tailDirty = false;
            _onSaveTail?.Invoke();
        }

        private void DrawContent(int id)
        {
            // GUI.ModalWindow 原生回调会静默吞掉普通异常，必须在内部捕获并关窗释放输入锁
            try
            {
                _drawingEmbedded = false;
                SnapshotLayoutState(embedded: false);
                BlackWhiteSkin.DrawBorder(new Rect(0f, 0f, _winW, _winH), 6f);

                float closeY = (TitleBarHeight - CloseBtnSize) * 0.5f;
                var closeRect = new Rect(_winW - CloseBtnSize - 14f, closeY, CloseBtnSize, CloseBtnSize);
                float scaleGroupLeftX = DrawTitleScaleGroup(closeRect.x - 14f);

                float titleY = (TitleBarHeight - 52f) * 0.5f;
                float titleW = Mathf.Max(160f, scaleGroupLeftX - 28f - 16f);
                DrawShadowedTitle(new Rect(28f, titleY, titleW, 52f),
                    SkinSyncI18n.T("app.name"), BlackWhiteSkin.HeaderStyle);

                if (GUI.Button(closeRect, GUIContent.none, BlackWhiteSkin.ButtonStyle))
                {
                    ClearTextFocus();
                    ClosePanel();
                    GUIUtility.ExitGUI();
                }
                BlackWhiteSkin.DrawBorder(closeRect, 4f);
                BlackWhiteSkin.DrawCloseX(new Rect(closeRect.x + 13f, closeRect.y + 13f,
                    closeRect.width - 26f, closeRect.height - 26f), 5f);

                BlackWhiteSkin.DrawHLine(new Rect(0f, TitleBarHeight, _winW, 4f));

                float bodyTop = TitleBarHeight + 14f;
                var bodyRect = new Rect(24f, bodyTop, _winW - 48f, _winH - bodyTop - StatusBarHeight - 8f);
                GUILayout.BeginArea(bodyRect);
                try
                {
                    DrawTabbedView();
                }
                finally
                {
                    GUILayout.EndArea();
                }

                BlackWhiteSkin.DrawHLine(new Rect(0f, _winH - StatusBarHeight, _winW, 4f));
                DrawStatusBar();

                // 拖拽区必须在缩放控件左边缘之前截止，防止覆盖缩放按钮与关闭按钮抢走点击事件
                GUI.DragWindow(new Rect(0f, 0f, Mathf.Max(80f, scaleGroupLeftX - 8f), TitleBarHeight));
            }
            catch (ExitGUIException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ModLog.Warning($"SkinSyncWindow.DrawContent 异常：{ex}");
                ClosePanel();
            }
        }

        private float DrawTitleScaleGroup(float rightLimitX)
        {
            const float btnH = 40f;
            const float gap = 6f;
            const float labelGap = 8f;
            float y = (TitleBarHeight - btnH) * 0.5f;

            int currentPct = Patches.SaveManagerTabBridge.GetEffectiveUiScalePercent(_cfg);
            string scaleLabel = SkinSyncI18n.T("lbl.ui_scale");
            MeasureContent.text = scaleLabel;
            float labelW = Mathf.Ceil(BlackWhiteSkin.MutedLabelStyle.CalcSize(MeasureContent).x) + 6f;

            float totalBtnsW = 0f;
            for (int i = 0; i < ScalePresets.Length; i++)
            {
                float w = CalcButtonWidth(BlackWhiteSkin.ControlBtnStyle, ScalePresetLabels[i], 66f, 12f);
                _scaleBtnWidths[i] = w;
                totalBtnsW += w;
                if (i > 0) totalBtnsW += gap;
            }

            float leftX = rightLimitX - totalBtnsW - labelGap - labelW;
            GUI.Label(new Rect(leftX, y, labelW, btnH), scaleLabel, BlackWhiteSkin.MutedLabelStyle);

            float curX = leftX + labelW + labelGap;
            for (int i = 0; i < ScalePresets.Length; i++)
            {
                int preset = ScalePresets[i];
                bool active = Mathf.Abs(currentPct - preset) <= 2;
                GUIStyle style = active ? BlackWhiteSkin.ControlBtnActiveStyle : BlackWhiteSkin.ControlBtnStyle;
                if (GUI.Button(new Rect(curX, y, _scaleBtnWidths[i], btnH), ScalePresetLabels[i], style))
                {
                    ClearTextFocus();
                    Patches.SaveManagerTabBridge.SetEffectiveUiScalePercent(_cfg, preset);
                    GUIUtility.ExitGUI();
                }
                curX += _scaleBtnWidths[i] + gap;
            }

            return leftX;
        }

        private static void DrawShadowedTitle(Rect rect, string text, GUIStyle style)
        {
            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, style);
            GUI.color = prev;
            GUI.Label(rect, text, style);
        }

        private void DrawStatusBar()
        {
            const float labelH = 40f;
            float y = _winH - StatusBarHeight + (StatusBarHeight - labelH) * 0.5f;
            string current = _getCurrentSkin?.Invoke();
            string status = string.IsNullOrEmpty(current)
                ? SkinSyncI18n.T("status.no_skin")
                : SkinSyncI18n.F("status.current_skin", current);
            GUI.Label(new Rect(28f, y, _winW - 296f, labelH), status, BlackWhiteSkin.SingleLineLabelStyle);

            string version = SkinSync.Version;
            if (!string.IsNullOrEmpty(version))
            {
                GUI.Label(new Rect(_winW - 252f, y, 224f, labelH),
                    "v" + version, BlackWhiteSkin.MutedRightStyle);
            }
        }

        /// <summary>在外部宿主容器（如 CuSaveManager 侧栏页签）中流式绘制面板内容。</summary>
        internal void DrawEmbedded()
        {
            _drawingEmbedded = true;
            SnapshotLayoutState(embedded: true);
            BlackWhiteSkin.Push();
            try
            {
                DrawTabbedView();
            }
            finally
            {
                _drawingEmbedded = false;
                BlackWhiteSkin.Pop();
            }
        }

        private void DrawTabbedView()
        {
            DrawTabBar();
            GUILayout.Space(8f);
            DrawActiveTabBody();
        }

        private void DrawTabBar()
        {
            GUILayout.BeginHorizontal();
            for (int i = 0; i < TabKeys.Length; i++)
            {
                if (i > 0) GUILayout.Space(8f);
                GUIStyle style = _tab == i ? BlackWhiteSkin.TabActiveStyle : BlackWhiteSkin.TabStyle;
                string label = SkinSyncI18n.T(TabKeys[i]);
                float tabW = CalcButtonWidth(style, label, 180f, 24f);
                if (GUILayout.Button(label, style, GUILayout.Width(tabW), GUILayout.Height(TabBarHeight)) && _tab != i)
                {
                    ClearTextFocus();
                    FlushPendingTailSave();
                    _tab = i;
                    _tailResetConfirmUntil = 0f;
                    _hasLayoutSnapshot = false;
                    CancelKeyCapture();
                    GUILayout.EndHorizontal();
                    GUIUtility.ExitGUI();
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            Rect lineRect = GUILayoutUtility.GetRect(1f, 2f, GUILayout.ExpandWidth(true));
            BlackWhiteSkin.DrawHLine(lineRect);
        }

        private void DrawActiveTabBody()
        {
            if (_tab == 1) DrawSkinsTab();
            else if (_tab == 2) DrawCurrentTab();
            else if (_tab == 3) DrawAboutTab();
            else DrawSettingsTab();
        }

        // 显式禁用横向滚动条，防止子元素反向撑宽 ScrollView 虚拟横轴
        private static Vector2 BeginVerticalScroll(Vector2 scroll)
        {
            return GUILayout.BeginScrollView(
                scroll,
                false,
                false,
                GUIStyle.none,
                BlackWhiteSkin.VerticalScrollbarStyle,
                BlackWhiteSkin.ScrollViewStyle,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true));
        }

        // 点击按钮后清理 keyboardControl，避免残留焦点拦截模组全局热键
        private static void ClearTextFocus()
        {
            GUIUtility.keyboardControl = 0;
        }

        // 用显式实测宽度传给 GUILayout.Width，避免 GUILayout.MinWidth 在窄容器中被压缩截断文字
        private static float CalcButtonWidth(GUIStyle style, string text, float minWidth, float extraPad = 16f)
        {
            MeasureContent.text = text ?? string.Empty;
            float w = Mathf.Ceil(style.CalcSize(MeasureContent).x) + extraPad;
            return Mathf.Max(minWidth, w);
        }

        private static Rect ReserveVerticalCenterSlot(float width, float height)
        {
            Rect slot = GUILayoutUtility.GetRect(width, RowMinHeight, GUILayout.Width(width), GUILayout.ExpandHeight(true));
            float y = slot.y + Mathf.Max(0f, Mathf.Floor((slot.height - height) * 0.5f));
            return new Rect(slot.x, y, width, height);
        }

        private static void DrawSectionHeader(string title, bool first = false)
        {
            GUILayout.Space(first ? 2f : 12f);
            GUILayout.Label(title, BlackWhiteSkin.SectionHeaderStyle, GUILayout.MinHeight(42f));
            GUILayout.Space(4f);
        }

        private static void BeginSubBlock()
        {
            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            GUILayout.Space(SubIndentW);
            GUILayout.BeginVertical();
        }

        private static void EndSubBlock()
        {
            GUILayout.EndVertical();
            Rect subRect = GUILayoutUtility.GetLastRect();
            GUILayout.EndHorizontal();
            if (Event.current != null && Event.current.type == EventType.Repaint)
            {
                BlackWhiteSkin.DrawSoftVLine(new Rect(
                    subRect.x - 10f,
                    subRect.y + 2f,
                    2f,
                    Mathf.Max(0f, subRect.height - 4f)));
            }
            GUILayout.Space(2f);
        }

        private static bool DrawSwitch(string label, bool value)
        {
            string onText = SkinSyncI18n.T("sw.on");
            string offText = SkinSyncI18n.T("sw.off");
            float btnW = Mathf.Max(
                CalcButtonWidth(BlackWhiteSkin.ControlBtnStyle, onText, 66f, 12f),
                CalcButtonWidth(BlackWhiteSkin.ControlBtnStyle, offText, 66f, 12f));
            const float gap = 6f;
            float groupW = btnW * 2f + gap;

            GUILayout.BeginHorizontal();
            GUILayout.Label(label, BlackWhiteSkin.RowLabelStyle,
                GUILayout.ExpandWidth(true), GUILayout.MinHeight(RowMinHeight));
            GUILayout.Space(12f);

            Rect slot = ReserveVerticalCenterSlot(groupW, ControlHeight);
            bool result = value;
            GUIStyle onStyle = value ? BlackWhiteSkin.ControlBtnActiveStyle : BlackWhiteSkin.ControlBtnStyle;
            GUIStyle offStyle = value ? BlackWhiteSkin.ControlBtnStyle : BlackWhiteSkin.ControlBtnActiveStyle;

            if (GUI.Button(new Rect(slot.x, slot.y, btnW, slot.height), onText, onStyle))
            {
                ClearTextFocus();
                result = true;
            }
            if (GUI.Button(new Rect(slot.x + btnW + gap, slot.y, btnW, slot.height), offText, offStyle))
            {
                ClearTextFocus();
                result = false;
            }
            GUILayout.EndHorizontal();
            return result;
        }

        private static float DrawSliderRow(string label, float value, float min, float max, float step)
        {
            bool prevChanged = GUI.changed;
            GUI.changed = false;

            string display = value.ToString("0.###", CultureInfo.InvariantCulture);
            float valW = CalcButtonWidth(BlackWhiteSkin.ValueBoxStyle, display, 88f, 12f);

            GUILayout.BeginHorizontal();
            GUILayout.Label(label, BlackWhiteSkin.RowLabelStyle,
                GUILayout.Width(LabelColW), GUILayout.MinHeight(RowMinHeight));
            GUILayout.Space(12f);
            float raw = GUILayout.HorizontalSlider(value, min, max,
                BlackWhiteSkin.SliderTrackStyle, BlackWhiteSkin.SliderThumbStyle,
                GUILayout.MinWidth(140f), GUILayout.ExpandWidth(true));
            bool moved = GUI.changed;
            GUILayout.Space(12f);
            Rect valSlot = ReserveVerticalCenterSlot(valW, ControlHeight);
            GUI.Label(valSlot, display, BlackWhiteSkin.ValueBoxStyle);
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);

            float result = value;
            if (moved)
            {
                ClearTextFocus();
                float snapped = step > 0f ? Mathf.Round(raw / step) * step : raw;
                snapped = Mathf.Clamp(snapped, min, max);
                snapped = (float)Math.Round(snapped, 4);
                if (Mathf.Abs(snapped - value) > 0.0001f)
                {
                    result = snapped;
                    prevChanged = true;
                }
            }

            GUI.changed = prevChanged;
            return result;
        }

        private static int DrawIntStepperRow(string label, int value, int step)
        {
            int delta = DrawStepperRow(label, value.ToString(CultureInfo.InvariantCulture));
            return value + delta * step;
        }

        private static float DrawFloatStepperRow(string label, float value, float step)
        {
            int delta = DrawStepperRow(label, value.ToString("0.##", CultureInfo.InvariantCulture));
            return value + delta * step;
        }

        private static int DrawStepperRow(string label, string display)
        {
            const float stepBtnW = 50f;
            const float gap = 8f;
            float valW = CalcButtonWidth(BlackWhiteSkin.ValueBoxStyle, display, 96f, 12f);
            float totalW = stepBtnW * 2f + valW + gap * 2f;
            int delta = 0;

            GUILayout.BeginHorizontal();
            GUILayout.Label(label, BlackWhiteSkin.RowLabelStyle,
                GUILayout.Width(LabelColW), GUILayout.MinHeight(RowMinHeight));
            GUILayout.Space(12f);
            Rect slot = ReserveVerticalCenterSlot(totalW, ControlHeight);
            if (GUI.Button(new Rect(slot.x, slot.y, stepBtnW, slot.height), "−", BlackWhiteSkin.ControlBtnStyle))
            {
                ClearTextFocus();
                delta = -1;
            }
            GUI.Label(new Rect(slot.x + stepBtnW + gap, slot.y, valW, slot.height), display, BlackWhiteSkin.ValueBoxStyle);
            if (GUI.Button(new Rect(slot.x + stepBtnW + gap + valW + gap, slot.y, stepBtnW, slot.height), "+", BlackWhiteSkin.ControlBtnStyle))
            {
                ClearTextFocus();
                delta = 1;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);
            return delta;
        }

        private void DrawHotkeyRow(string label, ConfigEntry<KeyboardShortcut> entry,
            ref bool capturing, Action onStart)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, BlackWhiteSkin.RowLabelStyle,
                GUILayout.Width(LabelColW), GUILayout.MinHeight(RowMinHeight));
            GUILayout.Space(12f);

            string text = capturing
                ? SkinSyncI18n.T("btn.press_a_key")
                : (SkinSyncSettings.IsBound(entry.Value) ? entry.Value.ToString() : SkinSyncI18n.T("btn.unbound"));
            GUIStyle keyStyle = capturing ? BlackWhiteSkin.ControlBtnActiveStyle : BlackWhiteSkin.ControlBtnStyle;
            float keyW = CalcButtonWidth(keyStyle, text, 232f, 18f);
            string clearText = SkinSyncI18n.T("btn.clear");
            float clearW = CalcButtonWidth(BlackWhiteSkin.ControlBtnStyle, clearText, 92f, 16f);

            Rect slot = ReserveVerticalCenterSlot(keyW + 10f + clearW, ControlHeight);
            if (GUI.Button(new Rect(slot.x, slot.y, keyW, slot.height), text, keyStyle))
            {
                ClearTextFocus();
                capturing = true;
                onStart();
            }
            if (GUI.Button(new Rect(slot.x + keyW + 10f, slot.y, clearW, slot.height), clearText, BlackWhiteSkin.ControlBtnStyle))
            {
                ClearTextFocus();
                entry.Value = new KeyboardShortcut(KeyCode.None);
                capturing = false;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(RowGap);
        }

        private void CaptureKeyDownIfNeeded()
        {
            if (!IsCapturingHotkey) return;
            var ev = Event.current;
            if (ev == null || ev.type != EventType.KeyDown || ev.keyCode == KeyCode.None) return;
            if (ev.keyCode == KeyCode.Escape)
            {
                _capturedFrame = Time.frameCount;
                CancelKeyCapture();
                ev.Use();
                return;
            }
            if (IsModifierKey(ev.keyCode)) return;

            var mods = new List<KeyCode>(4);
            bool hasCtrl = false;
            if (Input.GetKey(KeyCode.LeftControl)) { mods.Add(KeyCode.LeftControl); hasCtrl = true; }
            if (Input.GetKey(KeyCode.RightControl)) { mods.Add(KeyCode.RightControl); hasCtrl = true; }
            if (!hasCtrl && ev.control) mods.Add(KeyCode.LeftControl);

            bool hasShift = false;
            if (Input.GetKey(KeyCode.LeftShift)) { mods.Add(KeyCode.LeftShift); hasShift = true; }
            if (Input.GetKey(KeyCode.RightShift)) { mods.Add(KeyCode.RightShift); hasShift = true; }
            if (!hasShift && ev.shift) mods.Add(KeyCode.LeftShift);

            bool hasAlt = false;
            if (Input.GetKey(KeyCode.LeftAlt)) { mods.Add(KeyCode.LeftAlt); hasAlt = true; }
            if (Input.GetKey(KeyCode.RightAlt)) { mods.Add(KeyCode.RightAlt); hasAlt = true; }
            if (!hasAlt && ev.alt) mods.Add(KeyCode.LeftAlt);

            var sc = mods.Count > 0
                ? new KeyboardShortcut(ev.keyCode, mods.ToArray())
                : new KeyboardShortcut(ev.keyCode);
            if (_capturingPanel) _cfg.TogglePanelHotkey.Value = sc;
            else if (_capturingNext) _cfg.NextSkinHotkey.Value = sc;
            else if (_capturingPrev) _cfg.PrevSkinHotkey.Value = sc;
            else if (_capturingRescan) _cfg.RescanSkinsHotkey.Value = sc;
            _capturedFrame = Time.frameCount;
            CancelKeyCapture();
            ev.Use();
        }

        private static bool IsModifierKey(KeyCode key)
        {
            return key == KeyCode.LeftShift || key == KeyCode.RightShift
                || key == KeyCode.LeftControl || key == KeyCode.RightControl
                || key == KeyCode.LeftAlt || key == KeyCode.RightAlt;
        }
    }
}
