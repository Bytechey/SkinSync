using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>绘制并管理 SkinSync 配置主窗口与外部宿主嵌入面板。</summary>
    internal sealed partial class SkinSyncWindow
    {
        private const int WindowId = 0x5E15E150;
        private const float WindowWidth = 1440f;
        private const float WindowHeight = 900f;
        private const float TitleBarHeight = 64f;
        private const float CloseBtnSize = 52f;
        private const float LabelColW = 240f;
        private const float RowMinHeight = 52f;

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
        private readonly Func<List<string>> _getSkins;
        private readonly Func<string> _getCurrentSkin;
        private readonly Action<string> _onApplySkin;
        private readonly Action _onRescan;
        private readonly Func<List<AccessoryRow>> _getAccessories;
        private readonly Action<string, bool> _onToggleAccessory;
        private readonly Action<string, int, int, float, int> _onSetAccessoryTransform;
        private readonly Action _onSaveTail;
        private readonly Action _onResetTail;
        private readonly Action _onOpened;
        private readonly Action _onClosed;

        private Rect _rect = new Rect(120f, 60f, WindowWidth, WindowHeight);
        private float _drawScale = 1f;
        private int _tab;
        private Vector2 _settingsScroll;
        private Vector2 _skinsScroll;
        private Vector2 _accessoriesScroll;
        private Vector2 _aboutScroll;
        private float _lastGridWidth;
        private bool _tailDirty;

        private bool _capturingPanel;
        private bool _capturingNext;
        private bool _capturingPrev;
        private bool _capturingRescan;
        private bool _loggedDrawForCurrentOpen;

        internal bool Open { get; set; }
        internal bool IsCapturingHotkey => _capturingPanel || _capturingNext || _capturingPrev || _capturingRescan;
        internal Rect WindowRect => new Rect(_rect.x * _drawScale, _rect.y * _drawScale, _rect.width * _drawScale, _rect.height * _drawScale);

        internal void OpenPanel()
        {
            if (Open) return;
            Open = true;
            _loggedDrawForCurrentOpen = false;
            ModLog.Info("SkinSyncWindow.OpenPanel -> Open=true");
            _onOpened?.Invoke();
        }

        internal void ClosePanel()
        {
            if (!Open) return;
            FlushPendingTailSave();
            Open = false;
            _loggedDrawForCurrentOpen = false;
            CancelKeyCapture();
            _onClosed?.Invoke();
        }

        internal bool IsCursorOver()
        {
            if (!Open) return false;
            var mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            var screenRect = new Rect(_rect.x * _drawScale, _rect.y * _drawScale,
                _rect.width * _drawScale, _rect.height * _drawScale);
            return screenRect.Contains(mouse);
        }

        internal SkinSyncWindow(
            SkinSyncSettings cfg,
            Func<List<string>> getSkins,
            Func<string> getCurrentSkin,
            Action<string> onApplySkin,
            Action onRescan,
            Func<List<AccessoryRow>> getAccessories,
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
            _drawScale = ComputeScale();
            if (!_loggedDrawForCurrentOpen)
            {
                _loggedDrawForCurrentOpen = true;
                ModLog.Info($"SkinSyncWindow.Draw screen=({Screen.width}x{Screen.height}) scale={_drawScale:F3} rect=({_rect.x:0},{_rect.y:0},{_rect.width:0},{_rect.height:0})");
            }
            _rect.width = WindowWidth;
            _rect.height = WindowHeight;
            float maxX = Mathf.Max(0f, Screen.width / _drawScale - WindowWidth);
            float maxY = Mathf.Max(0f, Screen.height / _drawScale - WindowHeight);
            _rect.x = Mathf.Clamp(_rect.x, 0f, maxX);
            _rect.y = Mathf.Clamp(_rect.y, 0f, maxY);

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

        private static float ComputeScale()
        {
            float fit = Mathf.Min(Screen.width / WindowWidth, Screen.height / WindowHeight) * 0.92f;
            return Mathf.Clamp(fit, 0.3f, 1f);
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
            BlackWhiteSkin.DrawBorder(new Rect(0f, 0f, WindowWidth, WindowHeight), 6f);

            GUI.Label(new Rect(28f, 14f, WindowWidth - CloseBtnSize - 56f, 40f),
                SkinSyncI18n.T("app.name"), BlackWhiteSkin.HeaderStyle);

            var closeRect = new Rect(WindowWidth - CloseBtnSize - 12f, 8f, CloseBtnSize, CloseBtnSize);
            if (GUI.Button(closeRect, GUIContent.none))
            {
                ClosePanel();
            }
            BlackWhiteSkin.DrawBorder(closeRect, 4f);
            BlackWhiteSkin.DrawCloseX(new Rect(closeRect.x + 13f, closeRect.y + 13f,
                closeRect.width - 26f, closeRect.height - 26f), 6f);

            BlackWhiteSkin.DrawHLine(new Rect(0f, TitleBarHeight, WindowWidth, 4f));
            DrawTabs();
            BlackWhiteSkin.DrawHLine(new Rect(0f, TitleBarHeight + 84f, WindowWidth, 4f));

            float bodyTop = TitleBarHeight + 96f;
            float statusH = 60f;
            var bodyRect = new Rect(24f, bodyTop, WindowWidth - 48f, WindowHeight - bodyTop - statusH);
            GUILayout.BeginArea(bodyRect);
            DrawActiveTabBody();
            GUILayout.EndArea();

            BlackWhiteSkin.DrawHLine(new Rect(0f, WindowHeight - statusH, WindowWidth, 4f));
            string current = _getCurrentSkin?.Invoke();
            string status = string.IsNullOrEmpty(current)
                ? SkinSyncI18n.T("status.no_skin")
                : SkinSyncI18n.F("status.current_skin", current);
            GUI.Label(new Rect(28f, WindowHeight - statusH + 16f, WindowWidth - 56f, 32f), status);

            string version = SkinSync.Version;
            if (!string.IsNullOrEmpty(version))
            {
                GUI.Label(new Rect(WindowWidth - 248f, WindowHeight - statusH + 16f, 224f, 32f),
                    "v" + version, VersionLabelStyle);
            }

            GUI.DragWindow(new Rect(0f, 0f, WindowWidth - CloseBtnSize - 24f, TitleBarHeight));
        }

        private void DrawActiveTabBody()
        {
            if (_tab == 1) DrawSkinsTab();
            else if (_tab == 2) DrawCurrentTab();
            else if (_tab == 3) DrawAboutTab();
            else DrawSettingsTab();
        }

        private static GUIStyle _versionLabelStyle;
        private static GUIStyle VersionLabelStyle =>
            _versionLabelStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleRight };

        private void DrawTabs()
        {
            float tabW = 280f, tabH = 64f, top = TitleBarHeight + 14f;
            DrawTabButton(new Rect(28f, top, tabW, tabH), SkinSyncI18n.T("tab.settings"), 0);
            DrawTabButton(new Rect(28f + (tabW + 16f), top, tabW, tabH), SkinSyncI18n.T("tab.skins"), 1);
            DrawTabButton(new Rect(28f + (tabW + 16f) * 2f, top, tabW, tabH), SkinSyncI18n.T("tab.current"), 2);
            DrawTabButton(new Rect(28f + (tabW + 16f) * 3f, top, tabW, tabH), SkinSyncI18n.T("tab.about"), 3);
        }

        /// <summary>在外部宿主容器（如 CuSaveManager 侧栏页签）中流式绘制面板内容。</summary>
        internal void DrawEmbedded()
        {
            BlackWhiteSkin.Push();
            try
            {
                DrawEmbeddedTabs();
                DrawActiveTabBody();
            }
            finally { BlackWhiteSkin.Pop(); }
        }

        private void DrawEmbeddedTabs()
        {
            GUILayout.BeginHorizontal();
            DrawEmbeddedTabButton(SkinSyncI18n.T("tab.settings"), 0);
            DrawEmbeddedTabButton(SkinSyncI18n.T("tab.skins"), 1);
            DrawEmbeddedTabButton(SkinSyncI18n.T("tab.current"), 2);
            DrawEmbeddedTabButton(SkinSyncI18n.T("tab.about"), 3);
            GUILayout.EndHorizontal();
            GUILayout.Space(8f);
        }

        private void DrawEmbeddedTabButton(string label, int idx)
        {
            var style = idx == _tab ? BlackWhiteSkin.TabActiveStyle : BlackWhiteSkin.TabStyle;
            if (GUILayout.Button(label, style, GUILayout.Height(64f), GUILayout.ExpandWidth(true)))
            {
                FlushPendingTailSave();
                _tab = idx;
                CancelKeyCapture();
            }
        }

        private void DrawTabButton(Rect rect, string label, int idx)
        {
            var style = idx == _tab ? BlackWhiteSkin.TabActiveStyle : BlackWhiteSkin.TabStyle;
            if (GUI.Button(rect, label, style))
            {
                FlushPendingTailSave();
                _tab = idx;
                CancelKeyCapture();
            }
        }

        private void DrawHotkeyRow(string label, ConfigEntry<KeyboardShortcut> entry,
            ref bool capturing, Action onStart)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.MinWidth(LabelColW), GUILayout.ExpandWidth(false),
                GUILayout.MinHeight(RowMinHeight));
            string text = capturing
                ? SkinSyncI18n.T("btn.press_a_key")
                : (SkinSyncSettings.IsBound(entry.Value) ? entry.Value.ToString() : SkinSyncI18n.T("btn.unbound"));
            if (GUILayout.Button(text,
                GUILayout.MinWidth(280f), GUILayout.ExpandWidth(false), GUILayout.MinHeight(RowMinHeight)))
            {
                capturing = true;
                onStart();
            }
            GUILayout.Space(12f);
            if (GUILayout.Button(SkinSyncI18n.T("btn.clear"),
                GUILayout.MinWidth(96f), GUILayout.ExpandWidth(false), GUILayout.MinHeight(RowMinHeight)))
            {
                entry.Value = new KeyboardShortcut(KeyCode.None);
                capturing = false;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(6f);
        }

        private void CaptureKeyDownIfNeeded()
        {
            if (!IsCapturingHotkey) return;
            var ev = Event.current;
            if (ev.type != EventType.KeyDown || ev.keyCode == KeyCode.None) return;
            if (ev.keyCode == KeyCode.Escape)
            {
                CancelKeyCapture();
                ev.Use();
                return;
            }
            var sc = new KeyboardShortcut(ev.keyCode);
            if (_capturingPanel) _cfg.TogglePanelHotkey.Value = sc;
            else if (_capturingNext) _cfg.NextSkinHotkey.Value = sc;
            else if (_capturingPrev) _cfg.PrevSkinHotkey.Value = sc;
            else if (_capturingRescan) _cfg.RescanSkinsHotkey.Value = sc;
            CancelKeyCapture();
            ev.Use();
        }
    }
}
