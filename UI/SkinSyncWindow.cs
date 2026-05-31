using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>
    /// SkinSync 主面板：黑白线条风格 + 三 tab（设置 / 角色 / 当前角色设置）。
    /// 业务回调由 Plugin 注入；本类只管渲染、本地状态（tab / 滚动 / 改键采集）。
    /// </summary>
    internal sealed class SkinSyncWindow
    {
        private const int WindowId = 0x5E15E150;
        private const float WindowWidth = 1440f;
        private const float WindowHeight = 900f;
        private const float TitleBarHeight = 64f;
        private const float CloseBtnSize = 52f;
        private const float LabelColW = 240f;
        private const float RowMinHeight = 52f;

        /// <summary>当前角色配件视图行：id + 完整 transform + 装备依赖 slot。</summary>
        internal struct AccessoryRow
        {
#pragma warning disable CS0649
            public string Id;
            public bool Enabled;
            public int OffX;
            public int OffY;
            public float Rotation;
            public int ZOrder;
            public string RequireSlot;
#pragma warning restore CS0649
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
        private int _tab;
        private Vector2 _settingsScroll;
        private Vector2 _skinsScroll;
        private Vector2 _accessoriesScroll;
        private Vector2 _aboutScroll;

        private bool _capturingPanel;
        private bool _capturingNext;
        private bool _capturingPrev;
        private bool _capturingRescan;

        internal bool Open { get; set; }
        internal Rect WindowRect => _rect;

        internal void OpenPanel()
        {
            if (Open) return;
            Open = true;
            _onOpened?.Invoke();
        }

        internal void ClosePanel()
        {
            if (!Open) return;
            Open = false;
            CancelKeyCapture();
            _onClosed?.Invoke();
        }

        internal bool IsCursorOver()
        {
            if (!Open) return false;
            var mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            return _rect.Contains(mouse);
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
            BlackWhiteSkin.Push();
            try
            {
                _rect = GUI.ModalWindow(WindowId, _rect, DrawContent, "");
            }
            finally
            {
                BlackWhiteSkin.Pop();
            }
        }

        internal void CancelKeyCapture()
        {
            _capturingPanel = false;
            _capturingNext = false;
            _capturingPrev = false;
            _capturingRescan = false;
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
            if (_tab == 1) DrawSkinsTab();
#if DEBUG
            else if (_tab == 2) DrawCurrentTab();
            else if (_tab == 3) DrawAboutTab();
#else
            else if (_tab == 2) DrawAboutTab();
#endif
            else DrawSettingsTab();
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

        private static GUIStyle _versionLabelStyle;
        private static GUIStyle VersionLabelStyle =>
            _versionLabelStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleRight };

        private void DrawTabs()
        {
            float tabW = 280f, tabH = 64f, top = TitleBarHeight + 14f;
            DrawTabButton(new Rect(28f, top, tabW, tabH), SkinSyncI18n.T("tab.settings"), 0);
            DrawTabButton(new Rect(28f + (tabW + 16f), top, tabW, tabH), SkinSyncI18n.T("tab.skins"), 1);
#if DEBUG
            DrawTabButton(new Rect(28f + (tabW + 16f) * 2f, top, tabW, tabH), SkinSyncI18n.T("tab.current"), 2);
            DrawTabButton(new Rect(28f + (tabW + 16f) * 3f, top, tabW, tabH), SkinSyncI18n.T("tab.about"), 3);
#else
            DrawTabButton(new Rect(28f + (tabW + 16f) * 2f, top, tabW, tabH), SkinSyncI18n.T("tab.about"), 2);
#endif
        }

        private void DrawTabButton(Rect rect, string label, int idx)
        {
            var style = idx == _tab ? BlackWhiteSkin.TabActiveStyle : BlackWhiteSkin.TabStyle;
            if (GUI.Button(rect, label, style))
            {
                _tab = idx;
                CancelKeyCapture();
            }
        }

        private void DrawSettingsTab()
        {
            _settingsScroll = GUILayout.BeginScrollView(_settingsScroll,
                GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            GUILayout.Space(8f);
            GUILayout.Label(SkinSyncI18n.T("sec.visual"), BlackWhiteSkin.HeaderStyle);
            GUILayout.BeginVertical(BlackWhiteSkin.CardStyle);
            bool tailDeform = DrawSwitch(SkinSyncI18n.T("sw.tail_deform"), _cfg.TailDeformEnabled.Value);
            if (tailDeform != _cfg.TailDeformEnabled.Value)
            {
                _cfg.TailDeformEnabled.Value = tailDeform;
                TailDeformConfig.Enabled = tailDeform;
            }
#if DEBUG
            bool hideWear = DrawSwitch(SkinSyncI18n.T("sw.hide_game_wearables"), _cfg.HideGameWearables.Value);
            if (hideWear != _cfg.HideGameWearables.Value)
            {
                _cfg.HideGameWearables.Value = hideWear;
                EquipmentHider.Active = hideWear;
            }
            bool requireEq = DrawSwitch(SkinSyncI18n.T("sw.require_equipment"), _cfg.RequireEquipmentForAccessories.Value);
            if (requireEq != _cfg.RequireEquipmentForAccessories.Value)
            {
                _cfg.RequireEquipmentForAccessories.Value = requireEq;
                AccessoryEnforcer.Active = requireEq;
            }
            bool renderBlood = DrawSwitch(SkinSyncI18n.T("sw.render_custom_blood"), _cfg.RenderCustomBlood.Value);
            if (renderBlood != _cfg.RenderCustomBlood.Value)
            {
                _cfg.RenderCustomBlood.Value = renderBlood;
                BloodRenderConfig.Enabled = renderBlood;
            }
#endif
            GUILayout.EndVertical();

            GUILayout.Space(12f);
            GUILayout.Label(SkinSyncI18n.T("sec.sync"), BlackWhiteSkin.HeaderStyle);
            GUILayout.BeginVertical(BlackWhiteSkin.CardStyle);
            bool mpAvailable = SkinSync.IsMultiplayerSession;
            if (!mpAvailable)
            {
                GUILayout.Label(SkinSyncI18n.T("sync.no_krokmp_hint"));
                GUILayout.Space(4f);
            }
            else if (!SkinSyncMod.Network.KrokoshaBridge.IsNetworkRunning())
            {
                GUILayout.Label(SkinSyncI18n.T("sync.network_off_hint"));
                GUILayout.Space(4f);
            }
            bool prevEnabled = GUI.enabled;
            GUI.enabled = prevEnabled && mpAvailable;
            // 同步模式：两按钮互斥单选。
            GUILayout.BeginHorizontal();
            GUILayout.Label(SkinSyncI18n.T("lbl.sync_mode"),
                GUILayout.MinWidth(LabelColW), GUILayout.ExpandWidth(false), GUILayout.MinHeight(RowMinHeight));
            GUILayout.Space(20f);
            string mode = _cfg.SyncMode.Value;
            bool isOnEnter = mode == "OnEnter";
            if (GUILayout.Button(SkinSyncI18n.T("sync.mode_on_enter"),
                isOnEnter ? BlackWhiteSkin.TabActiveStyle : BlackWhiteSkin.TabStyle,
                GUILayout.MinWidth(220f), GUILayout.ExpandWidth(false), GUILayout.MinHeight(RowMinHeight)))
            {
                if (!isOnEnter) _cfg.SyncMode.Value = "OnEnter";
            }
            GUILayout.Space(8f);
            if (GUILayout.Button(SkinSyncI18n.T("sync.mode_passive"),
                !isOnEnter ? BlackWhiteSkin.TabActiveStyle : BlackWhiteSkin.TabStyle,
                GUILayout.MinWidth(220f), GUILayout.ExpandWidth(false), GUILayout.MinHeight(RowMinHeight)))
            {
                if (isOnEnter) _cfg.SyncMode.Value = "Passive";
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(6f);

#if DEBUG
            bool syncAcc = DrawSwitch(SkinSyncI18n.T("sw.sync_accessories"), _cfg.SyncAccessories.Value);
            if (syncAcc != _cfg.SyncAccessories.Value) _cfg.SyncAccessories.Value = syncAcc;
            bool syncTail = DrawSwitch(SkinSyncI18n.T("sw.sync_tail"), _cfg.SyncTailDeform.Value);
            if (syncTail != _cfg.SyncTailDeform.Value) _cfg.SyncTailDeform.Value = syncTail;
#endif
            GUI.enabled = prevEnabled;
            GUILayout.EndVertical();

            GUILayout.Space(12f);
            GUILayout.Label(SkinSyncI18n.T("sec.hotkeys"), BlackWhiteSkin.HeaderStyle);
            GUILayout.BeginVertical(BlackWhiteSkin.CardStyle);
            DrawHotkeyRow(SkinSyncI18n.T("lbl.hotkey_toggle_panel"), _cfg.TogglePanelHotkey, ref _capturingPanel,
                () => { _capturingNext = false; _capturingPrev = false; _capturingRescan = false; });
            DrawHotkeyRow(SkinSyncI18n.T("lbl.hotkey_next_skin"), _cfg.NextSkinHotkey, ref _capturingNext,
                () => { _capturingPanel = false; _capturingPrev = false; _capturingRescan = false; });
            DrawHotkeyRow(SkinSyncI18n.T("lbl.hotkey_prev_skin"), _cfg.PrevSkinHotkey, ref _capturingPrev,
                () => { _capturingPanel = false; _capturingNext = false; _capturingRescan = false; });
            DrawHotkeyRow(SkinSyncI18n.T("lbl.hotkey_rescan"), _cfg.RescanSkinsHotkey, ref _capturingRescan,
                () => { _capturingPanel = false; _capturingNext = false; _capturingPrev = false; });
            CaptureKeyDownIfNeeded();
            GUILayout.EndVertical();

            GUILayout.Space(12f);
            GUILayout.Label(SkinSyncI18n.T("sec.misc"), BlackWhiteSkin.HeaderStyle);
            GUILayout.BeginVertical(BlackWhiteSkin.CardStyle);
            bool showLog = DrawSwitch(SkinSyncI18n.T("sw.show_log_in_console"), _cfg.ShowLogInConsole.Value);
            if (showLog != _cfg.ShowLogInConsole.Value) _cfg.ShowLogInConsole.Value = showLog;
            bool acceptUpdate = DrawSwitch(SkinSyncI18n.T("sw.accept_update_notice"), _cfg.AcceptUpdateNotice.Value);
            if (acceptUpdate != _cfg.AcceptUpdateNotice.Value)
            {
                _cfg.AcceptUpdateNotice.Value = acceptUpdate;
                SkinSyncMod.Patches.UpdateChecker.Enabled = acceptUpdate;
            }
            GUILayout.EndVertical();

            GUILayout.Space(20f);
            GUILayout.EndScrollView();
        }

        private void DrawSkinsTab()
        {
            var skins = _getSkins?.Invoke() ?? new List<string>();
            string currentSkin = _getCurrentSkin?.Invoke() ?? "";

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            GUILayout.Label(SkinSyncI18n.F("fmt.skins_count", skins.Count),
                GUILayout.MinWidth(220f), GUILayout.ExpandWidth(false), GUILayout.MinHeight(36f));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(SkinSyncI18n.T("btn.rescan"),
                GUILayout.MinWidth(180f), GUILayout.ExpandWidth(false), GUILayout.MinHeight(36f)))
            {
                _onRescan?.Invoke();
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(8f);

            _skinsScroll = GUILayout.BeginScrollView(_skinsScroll,
                GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            if (skins.Count == 0)
            {
                GUILayout.Space(20f);
                GUILayout.Label(SkinSyncI18n.T("msg.no_skins"));
            }
            else
            {
                DrawSkinGrid(skins, currentSkin);
            }

            GUILayout.Space(20f);
            GUILayout.EndScrollView();
        }

        // 一行限定 3 个，cell 宽度按 ScrollView 可用宽度自适应。
        // 计算口径：bodyRect.width = WindowWidth - 48；ScrollView 自身扣 24 scrollbar；网格右侧再留 24 与 scrollbar 拉开距离。
        private const int CellsPerRow = 3;
        private const float CellAspectH = 1.35f;
        private const float GridGap = 16f;
        private const float GridRightPad = 24f;
        private const float CellInnerPad = 12f;

        private void DrawSkinGrid(List<string> skins, string currentSkin)
        {
            float available = WindowWidth - 48f - 24f - GridRightPad;
            float cellW = Mathf.Floor((available - GridGap * (CellsPerRow - 1)) / CellsPerRow);
            float cellH = Mathf.Floor(cellW * CellAspectH);
            float imgSize = cellW - CellInnerPad * 2f;

            int row = 0;
            while (row * CellsPerRow < skins.Count)
            {
                GUILayout.BeginHorizontal();
                for (int c = 0; c < CellsPerRow; c++)
                {
                    int idx = row * CellsPerRow + c;
                    if (idx >= skins.Count)
                    {
                        // 行末不足时占位，让前面 cell 仍按左对齐排列。
                        GUILayoutUtility.GetRect(cellW, cellH, GUILayout.Width(cellW), GUILayout.Height(cellH));
                    }
                    else
                    {
                        DrawSkinCell(skins[idx], currentSkin, cellW, cellH, imgSize);
                    }
                    if (c < CellsPerRow - 1) GUILayout.Space(GridGap);
                }
                GUILayout.Space(GridRightPad);
                GUILayout.EndHorizontal();
                GUILayout.Space(GridGap);
                row++;
            }
        }

        private void DrawSkinCell(string skin, string currentSkin, float cellW, float cellH, float imgSize)
        {
            bool isActive = string.Equals(skin, currentSkin, StringComparison.Ordinal);

            // 用 GetRect 拿整个 cell 精确 rect——所有内部控件 absolute 定位，避开 CardStyle padding 与 GUILayout.Width 的不可预测交互。
            Rect cellRect = GUILayoutUtility.GetRect(cellW, cellH,
                GUILayout.Width(cellW), GUILayout.Height(cellH),
                GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));

            // 背景（深灰）+ 白色矩形边框，与面板风格一致。
            GUI.DrawTexture(cellRect, BlackWhiteSkin.LineTex, ScaleMode.StretchToFill, false, 0f,
                new Color(0.06f, 0.06f, 0.06f, 0.42f), 0f, 0f);
            BlackWhiteSkin.DrawBorder(cellRect, 2f);

            // 预览图：cell 顶部居中。
            float imgX = cellRect.x + (cellRect.width - imgSize) * 0.5f;
            float imgY = cellRect.y + CellInnerPad;
            Rect imgRect = new Rect(imgX, imgY, imgSize, imgSize);
            GUI.DrawTexture(imgRect, BlackWhiteSkin.LineTex, ScaleMode.StretchToFill, false, 0f,
                new Color(0f, 0f, 0f, 0.28f), 0f, 0f);
            BlackWhiteSkin.DrawBorder(imgRect, 2f);

            Texture2D tex = null;
            try
            {
                string skinDir = SkinSyncMod.SkinPathResolver.GetSkinDir(skin);
                tex = SkinPreviewRenderer.GetOrBuild(skin, skinDir, _cfg);
            }
            catch (System.Exception ex)
            {
                SkinSyncMod.ModLog.Warning("preview build failed for " + skin + ": " + ex.Message);
            }
            if (tex != null)
            {
                GUI.DrawTexture(imgRect, tex, ScaleMode.ScaleToFit, true);
            }

            // 名字
            string title = isActive ? skin + "    " + SkinSyncI18n.T("lbl.skin_active") : skin;
            Rect titleRect = new Rect(cellRect.x + CellInnerPad, imgRect.yMax + 8f,
                cellRect.width - CellInnerPad * 2f, 40f);
            GUI.Label(titleRect, title, BlackWhiteSkin.HeaderStyle);

            // 按钮
            float btnH = 48f;
            Rect btnRect = new Rect(cellRect.x + CellInnerPad,
                cellRect.yMax - CellInnerPad - btnH,
                cellRect.width - CellInnerPad * 2f, btnH);
            bool prevEnabled = GUI.enabled;
            GUI.enabled = !isActive;
            if (GUI.Button(btnRect, isActive ? SkinSyncI18n.T("btn.skin_in_use") : SkinSyncI18n.T("btn.apply_skin")))
            {
                _onApplySkin?.Invoke(skin);
            }
            GUI.enabled = prevEnabled;
        }

        private const string UrlModRepo = "https://github.com/Bytechey/SkinSync";
        private const string UrlModVideo = "https://www.bilibili.com/video/BV1p1GJ64EAG";

        private static GUIStyle _centerTitleStyle;
        private static GUIStyle CenterTitleStyle =>
            _centerTitleStyle ??= new GUIStyle(BlackWhiteSkin.HeaderStyle)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 44,
                fontStyle = FontStyle.Bold,
            };
        private static GUIStyle _centerLabelStyle;
        private static GUIStyle CenterLabelStyle =>
            _centerLabelStyle ??= new GUIStyle(BlackWhiteSkin.HeaderStyle)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 30,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
            };
        private static GUIStyle _nameButtonStyle;
        private static GUIStyle NameButtonStyle
        {
            get
            {
                if (_nameButtonStyle != null) return _nameButtonStyle;
                var s = new GUIStyle(BlackWhiteSkin.HeaderStyle)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 32,
                    fontStyle = FontStyle.Bold,
                    border = new RectOffset(0, 0, 0, 0),
                    margin = new RectOffset(0, 0, 0, 0),
                    padding = new RectOffset(6, 6, 6, 6),
                };
                s.normal.background = null;
                s.hover.background = null;
                s.active.background = null;
                s.focused.background = null;
                s.onNormal.background = null;
                s.onHover.background = null;
                s.onActive.background = null;
                s.normal.textColor = Color.white;
                s.hover.textColor = new Color(0.6f, 0.85f, 1f);
                s.active.textColor = new Color(0.4f, 0.7f, 1f);
                s.focused.textColor = Color.white;
                _nameButtonStyle = s;
                return s;
            }
        }

        private void DrawAboutTab()
        {
            _aboutScroll = GUILayout.BeginScrollView(_aboutScroll,
                GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            GUILayout.Space(12f);
            GUILayout.Label(SkinSyncI18n.T("about.title"), CenterTitleStyle, GUILayout.ExpandWidth(true));
            GUILayout.Space(6f);
            GUILayout.Label(SkinSyncI18n.T("about.desc"), CenterLabelStyle, GUILayout.ExpandWidth(true));
            GUILayout.Label(SkinSyncI18n.F("about.version", SkinSync.Version), CenterLabelStyle, GUILayout.ExpandWidth(true));

            GUILayout.Space(16f);
            GUILayout.Label(SkinSyncI18n.T("about.sec_links"), CenterTitleStyle, GUILayout.ExpandWidth(true));
            GUILayout.Space(6f);
            DrawLinkButton(SkinSyncI18n.T("about.link_mod_repo"), UrlModRepo);
            DrawLinkButton(SkinSyncI18n.T("about.link_mod_video"), UrlModVideo);

            GUILayout.Space(16f);
            GUILayout.Label(SkinSyncI18n.T("about.sec_credits"), CenterTitleStyle, GUILayout.ExpandWidth(true));
            GUILayout.Space(6f);
            DrawNameButton("Bytechey", "https://github.com/Bytechey");
            DrawNameButton("huanxin996", "https://github.com/huanxin996");

            GUILayout.Space(16f);
            GUILayout.Label(SkinSyncI18n.T("about.testers"), CenterTitleStyle, GUILayout.ExpandWidth(true));
            GUILayout.Space(6f);
            DrawCreditLine(".....");

            GUILayout.Space(16f);
            GUILayout.Label(SkinSyncI18n.T("about.sec_deps"), CenterTitleStyle, GUILayout.ExpandWidth(true));
            GUILayout.Space(6f);
            DrawLinkButton("KrokoshaCasualtiesMP", "https://github.com/Krokosha666/cas-unk-krokosha-multiplayer-coop");
            DrawLinkButton("BepInEx", "https://github.com/BepInEx/BepInEx");
            DrawLinkButton("HarmonyX", "https://github.com/BepInEx/HarmonyX");

            GUILayout.Space(20f);
            GUILayout.EndScrollView();
        }

        private void DrawLinkButton(string label, string url)
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(label, BlackWhiteSkin.TabStyle,
                GUILayout.MinWidth(560f), GUILayout.ExpandWidth(false), GUILayout.MinHeight(RowMinHeight)))
            {
                try { Application.OpenURL(url); } catch { }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(6f);
        }

        private void DrawCreditLine(string text)
        {
            GUILayout.Label(text, CenterLabelStyle, GUILayout.ExpandWidth(true), GUILayout.MinHeight(36f));
        }

        private void DrawNameButton(string name, string url)
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(name, NameButtonStyle, GUILayout.ExpandWidth(false), GUILayout.MinHeight(44f)))
            {
                try { Application.OpenURL(url); } catch { }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);
        }

        // 当前角色 tab：折叠态字典（key = "tail" 或 "acc:<id>"），默认全部折叠。
        private readonly Dictionary<string, bool> _currentExpanded = new Dictionary<string, bool>();

        private void DrawCurrentTab()
        {
            string currentSkin = _getCurrentSkin?.Invoke() ?? "";
            GUILayout.Space(4f);
            GUILayout.Label(string.IsNullOrEmpty(currentSkin)
                    ? SkinSyncI18n.T("status.no_skin")
                    : SkinSyncI18n.F("status.current_skin", currentSkin),
                BlackWhiteSkin.HeaderStyle);
            GUILayout.Space(8f);

            _accessoriesScroll = GUILayout.BeginScrollView(_accessoriesScroll,
                GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            if (string.IsNullOrEmpty(currentSkin))
            {
                GUILayout.Space(20f);
                GUILayout.Label(SkinSyncI18n.T("msg.no_skin_yet"));
                GUILayout.EndScrollView();
                return;
            }

            DrawTailGroup();
            GUILayout.Space(12f);

            GUILayout.Label(SkinSyncI18n.T("sec.accessories"), BlackWhiteSkin.HeaderStyle);
            var rows = _getAccessories?.Invoke() ?? new List<AccessoryRow>();
            if (rows.Count == 0)
            {
                GUILayout.Space(10f);
                GUILayout.Label(SkinSyncI18n.T("msg.no_accessories"));
            }
            else
            {
                foreach (var row in rows)
                {
                    DrawAccessoryGroup(row);
                    GUILayout.Space(6f);
                }
            }

            GUILayout.Space(20f);
            GUILayout.EndScrollView();
        }

        private void DrawTailGroup()
        {
            const string key = "tail";
            if (!_currentExpanded.ContainsKey(key)) _currentExpanded[key] = false;
            bool expanded = _currentExpanded[key];
            string statusText = _cfg.TailDeformEnabled.Value
                ? SkinSyncI18n.T("lbl.tail_on")
                : SkinSyncI18n.T("lbl.tail_off");
            string label = (expanded ? "▼  " : "▶  ") + SkinSyncI18n.T("sec.tail") + "    " + statusText;
            if (GUILayout.Button(label, BlackWhiteSkin.HeaderButtonStyle,
                GUILayout.ExpandWidth(true), GUILayout.MinHeight(56f)))
            {
                _currentExpanded[key] = !expanded;
            }
            if (!_currentExpanded[key]) return;

            GUILayout.BeginVertical(BlackWhiteSkin.CardStyle);
            bool changed = false;

            bool fg = DrawSwitch(SkinSyncI18n.T("tail.front_guard"), TailDeformConfig.FrontGuard);
            if (fg != TailDeformConfig.FrontGuard) { TailDeformConfig.FrontGuard = fg; changed = true; }

            int seg = (int)DrawSliderRow(SkinSyncI18n.T("tail.segments"), TailDeformConfig.Segments, 4f, 40f, 1f);
            if (seg != TailDeformConfig.Segments) { TailDeformConfig.Segments = seg; changed = true; }
            int it = (int)DrawSliderRow(SkinSyncI18n.T("tail.iters"), TailDeformConfig.ConstraintIters, 1f, 20f, 1f);
            if (it != TailDeformConfig.ConstraintIters) { TailDeformConfig.ConstraintIters = it; changed = true; }

            float dp = DrawSliderRow(SkinSyncI18n.T("tail.damping"), TailDeformConfig.Damping, 0f, 5f, 0.01f);
            if (!Mathf.Approximately(dp, TailDeformConfig.Damping)) { TailDeformConfig.Damping = dp; changed = true; }
            float sdp = DrawSliderRow(SkinSyncI18n.T("tail.speed_damping"), TailDeformConfig.SpeedDamping, 0f, 0.5f, 0.005f);
            if (!Mathf.Approximately(sdp, TailDeformConfig.SpeedDamping)) { TailDeformConfig.SpeedDamping = sdp; changed = true; }
            float st = DrawSliderRow(SkinSyncI18n.T("tail.stiffness"), TailDeformConfig.Stiffness, 0f, 5f, 0.01f);
            if (!Mathf.Approximately(st, TailDeformConfig.Stiffness)) { TailDeformConfig.Stiffness = st; changed = true; }
            float mb = DrawSliderRow(SkinSyncI18n.T("tail.max_bend"), TailDeformConfig.MaxBendDeg, 0f, 180f, 1f);
            if (!Mathf.Approximately(mb, TailDeformConfig.MaxBendDeg)) { TailDeformConfig.MaxBendDeg = mb; changed = true; }
            float af = DrawSliderRow(SkinSyncI18n.T("tail.anchor_follow"), TailDeformConfig.AnchorFollow, 0f, 5f, 0.01f);
            if (!Mathf.Approximately(af, TailDeformConfig.AnchorFollow)) { TailDeformConfig.AnchorFollow = af; changed = true; }
            float sm = DrawSliderRow(SkinSyncI18n.T("tail.smoothness"), TailDeformConfig.Smoothness, 0f, 0.99f, 0.01f);
            if (!Mathf.Approximately(sm, TailDeformConfig.Smoothness)) { TailDeformConfig.Smoothness = sm; changed = true; }
            float ms = DrawSliderRow(SkinSyncI18n.T("tail.max_step"), TailDeformConfig.MaxStep, 0.001f, 2f, 0.005f);
            if (!Mathf.Approximately(ms, TailDeformConfig.MaxStep)) { TailDeformConfig.MaxStep = ms; changed = true; }
            float mf = DrawSliderRow(SkinSyncI18n.T("tail.max_fixed_dt"), TailDeformConfig.MaxFixedDt, 0.005f, 0.1f, 0.001f);
            if (!Mathf.Approximately(mf, TailDeformConfig.MaxFixedDt)) { TailDeformConfig.MaxFixedDt = mf; changed = true; }
            float fm = DrawSliderRow(SkinSyncI18n.T("tail.front_guard_margin"), TailDeformConfig.FrontGuardMargin, 0f, 1f, 0.01f);
            if (!Mathf.Approximately(fm, TailDeformConfig.FrontGuardMargin)) { TailDeformConfig.FrontGuardMargin = fm; changed = true; }

            float gx = DrawSliderRow(SkinSyncI18n.T("tail.gravity_x"), TailDeformConfig.GravityX, -10f, 10f, 0.1f);
            if (!Mathf.Approximately(gx, TailDeformConfig.GravityX)) { TailDeformConfig.GravityX = gx; changed = true; }
            float gy = DrawSliderRow(SkinSyncI18n.T("tail.gravity_y"), TailDeformConfig.GravityY, -20f, 10f, 0.1f);
            if (!Mathf.Approximately(gy, TailDeformConfig.GravityY)) { TailDeformConfig.GravityY = gy; changed = true; }

            float wf = DrawSliderRow(SkinSyncI18n.T("tail.wind_freq"), TailDeformConfig.WindFreq, 0f, 20f, 0.05f);
            if (!Mathf.Approximately(wf, TailDeformConfig.WindFreq)) { TailDeformConfig.WindFreq = wf; changed = true; }
            float wa = DrawSliderRow(SkinSyncI18n.T("tail.wind_amp"), TailDeformConfig.WindAmp, 0f, 1f, 0.001f);
            if (!Mathf.Approximately(wa, TailDeformConfig.WindAmp)) { TailDeformConfig.WindAmp = wa; changed = true; }
            float sd = DrawSliderRow(SkinSyncI18n.T("tail.speed_disturb"), TailDeformConfig.SpeedDisturb, 0f, 0.5f, 0.001f);
            if (!Mathf.Approximately(sd, TailDeformConfig.SpeedDisturb)) { TailDeformConfig.SpeedDisturb = sd; changed = true; }

            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(SkinSyncI18n.T("tail.reset"),
                GUILayout.MinWidth(180f), GUILayout.ExpandWidth(false), GUILayout.MinHeight(44f)))
            {
                _onResetTail?.Invoke();
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            if (changed) _onSaveTail?.Invoke();
        }

        private void DrawAccessoryGroup(AccessoryRow row)
        {
            string key = "acc:" + row.Id;
            if (!_currentExpanded.ContainsKey(key)) _currentExpanded[key] = false;
            bool expanded = _currentExpanded[key];

            GUILayout.BeginHorizontal();
            string statusText = row.Enabled ? SkinSyncI18n.T("sw.on") : SkinSyncI18n.T("sw.off");
            // 配件名走游戏自己的本地化（Locale.GetItem 找不到 key 时自然回落到 id 本身）。
            string displayName;
            try { displayName = Locale.GetItem(row.Id); }
            catch { displayName = row.Id; }
            if (string.IsNullOrEmpty(displayName)) displayName = row.Id;
            string label = (expanded ? "▼  " : "▶  ") + displayName + "    " + statusText;
            if (GUILayout.Button(label, BlackWhiteSkin.HeaderButtonStyle,
                GUILayout.ExpandWidth(true), GUILayout.MinHeight(48f)))
            {
                _currentExpanded[key] = !expanded;
            }
            GUILayout.EndHorizontal();
            if (!_currentExpanded[key]) return;

            GUILayout.BeginVertical(BlackWhiteSkin.CardStyle);

            if (!string.IsNullOrEmpty(row.RequireSlot))
            {
                GUILayout.Label(SkinSyncI18n.F("acc.require_slot",
                    SkinSyncI18n.LocalizeWearSlot(row.RequireSlot)));
                GUILayout.Space(4f);
            }

            bool nextEnabled = DrawSwitch(SkinSyncI18n.T("acc.enabled"), row.Enabled);
            if (nextEnabled != row.Enabled)
            {
                _onToggleAccessory?.Invoke(row.Id, nextEnabled);
            }

            int offX = row.OffX;
            int offY = row.OffY;
            float rot = row.Rotation;
            int z = row.ZOrder;
            bool tChanged = false;

            int newX = DrawIntStepperRow(SkinSyncI18n.T("acc.off_x"), offX, 1);
            if (newX != offX) { offX = newX; tChanged = true; }
            int newY = DrawIntStepperRow(SkinSyncI18n.T("acc.off_y"), offY, 1);
            if (newY != offY) { offY = newY; tChanged = true; }
            float newRot = DrawFloatStepperRow(SkinSyncI18n.T("acc.rotation"), rot, 5f);
            if (!Mathf.Approximately(newRot, rot)) { rot = newRot; tChanged = true; }
            int newZ = DrawIntStepperRow(SkinSyncI18n.T("acc.z_order"), z, 1);
            if (newZ != z) { z = newZ; tChanged = true; }

            if (tChanged)
            {
                _onSetAccessoryTransform?.Invoke(row.Id, offX, offY, rot, z);
            }
            GUILayout.EndVertical();
        }

        private static float DrawSliderRow(string label, float value, float min, float max, float step)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.MinWidth(LabelColW), GUILayout.ExpandWidth(false),
                GUILayout.MinHeight(36f));
            float v = GUILayout.HorizontalSlider(value, min, max,
                GUILayout.ExpandWidth(true), GUILayout.MinHeight(36f));
            GUILayout.Space(8f);
            GUILayout.Label(v.ToString("0.###"),
                GUILayout.MinWidth(96f), GUILayout.ExpandWidth(false), GUILayout.MinHeight(36f));
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);
            if (step > 0f) v = Mathf.Round(v / step) * step;
            return Mathf.Clamp(v, min, max);
        }

        private static int DrawIntStepperRow(string label, int value, int step)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.MinWidth(LabelColW), GUILayout.ExpandWidth(false),
                GUILayout.MinHeight(44f));
            if (GUILayout.Button("−", GUILayout.MinWidth(56f), GUILayout.ExpandWidth(false), GUILayout.MinHeight(44f)))
            {
                value -= step;
            }
            GUILayout.Label(value.ToString(),
                GUILayout.MinWidth(96f), GUILayout.ExpandWidth(false), GUILayout.MinHeight(44f));
            if (GUILayout.Button("+", GUILayout.MinWidth(56f), GUILayout.ExpandWidth(false), GUILayout.MinHeight(44f)))
            {
                value += step;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);
            return value;
        }

        private static float DrawFloatStepperRow(string label, float value, float step)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.MinWidth(LabelColW), GUILayout.ExpandWidth(false),
                GUILayout.MinHeight(44f));
            if (GUILayout.Button("−", GUILayout.MinWidth(56f), GUILayout.ExpandWidth(false), GUILayout.MinHeight(44f)))
            {
                value -= step;
            }
            GUILayout.Label(value.ToString("0.##"),
                GUILayout.MinWidth(96f), GUILayout.ExpandWidth(false), GUILayout.MinHeight(44f));
            if (GUILayout.Button("+", GUILayout.MinWidth(56f), GUILayout.ExpandWidth(false), GUILayout.MinHeight(44f)))
            {
                value += step;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);
            return value;
        }

        private static bool DrawSwitch(string label, bool value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.MinWidth(LabelColW), GUILayout.ExpandWidth(true),
                GUILayout.MinHeight(RowMinHeight));
            GUILayout.Space(20f);

            bool result = value;
            var onStyle = value ? BlackWhiteSkin.TabActiveStyle : BlackWhiteSkin.TabStyle;
            var offStyle = value ? BlackWhiteSkin.TabStyle : BlackWhiteSkin.TabActiveStyle;

            if (GUILayout.Button(SkinSyncI18n.T("sw.on"), onStyle,
                GUILayout.MinWidth(120f), GUILayout.ExpandWidth(false), GUILayout.MinHeight(RowMinHeight)))
            {
                result = true;
            }
            GUILayout.Space(8f);
            if (GUILayout.Button(SkinSyncI18n.T("sw.off"), offStyle,
                GUILayout.MinWidth(120f), GUILayout.ExpandWidth(false), GUILayout.MinHeight(RowMinHeight)))
            {
                result = false;
            }
            GUILayout.EndHorizontal();
            return result;
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
            if (!(_capturingPanel || _capturingNext || _capturingPrev || _capturingRescan)) return;
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
