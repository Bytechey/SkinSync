using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using UnityEngine;

namespace SkinSyncMod
{
    internal sealed partial class SkinSyncWindow
    {
        private const int CellsPerRow = 3;
        private const float CellChromeH = 108f;
        private const float GridGap = 14f;
        private const float GridRightPad = 18f;
        private const float CellInnerPad = 10f;

        private const string UrlModRepo = "https://github.com/Bytechey/SkinSync";
        private const string UrlModVideo = "https://www.bilibili.com/video/BV1p1GJ64EAG";

        private readonly Dictionary<string, bool> _currentExpanded = new Dictionary<string, bool>();

        private static GUIStyle _skinNameStyle;
        private static GUIStyle SkinNameStyle =>
            _skinNameStyle ??= new GUIStyle(BlackWhiteSkin.HeaderStyle)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 28,
                fontStyle = FontStyle.Bold,
            };

        private static GUIStyle _starStyle;
        private static GUIStyle StarStyle =>
            _starStyle ??= new GUIStyle(BlackWhiteSkin.HeaderStyle)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 40,
                fontStyle = FontStyle.Bold,
            };

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
                SkinApplier.RefreshAll();
            }

            bool vanillaSelf = DrawSwitch(SkinSyncI18n.T("sw.vanilla_self"), _cfg.VanillaSkinForSelf.Value);
            if (vanillaSelf != _cfg.VanillaSkinForSelf.Value)
            {
                _cfg.VanillaSkinForSelf.Value = vanillaSelf;
                SkinApplier.RefreshAll();
            }
            bool vanillaOthers = DrawSwitch(SkinSyncI18n.T("sw.vanilla_others"), _cfg.VanillaSkinForOthers.Value);
            if (vanillaOthers != _cfg.VanillaSkinForOthers.Value)
            {
                _cfg.VanillaSkinForOthers.Value = vanillaOthers;
                SkinApplier.RefreshAll();
            }
            GUILayout.EndVertical();

            GUILayout.Space(12f);
            GUILayout.Label(SkinSyncI18n.T("sec.sync"), BlackWhiteSkin.HeaderStyle);
            GUILayout.BeginVertical(BlackWhiteSkin.CardStyle);
            bool mpAvailable = SkinSync.IsMultiplayerSession;
            if (!mpAvailable)
            {
                GUILayout.Label(SkinSyncI18n.T("sync.no_together_hint"));
                GUILayout.Space(4f);
            }
            else if (!SkinSyncMod.Interop.Consumers.SessionConsumer.IsRunning)
            {
                GUILayout.Label(SkinSyncI18n.T("sync.network_off_hint"));
                GUILayout.Space(4f);
            }
            bool prevEnabled = GUI.enabled;
            GUI.enabled = prevEnabled && mpAvailable;
            GUILayout.BeginHorizontal();
            GUILayout.Label(SkinSyncI18n.T("lbl.sync_mode"),
                GUILayout.MinWidth(LabelColW), GUILayout.ExpandWidth(false), GUILayout.MinHeight(RowMinHeight));
            GUILayout.Space(20f);
            string mode = _cfg.SyncMode.Value;
            bool isOnEnter = mode != "Passive";
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

            bool syncAcc = DrawSwitch(SkinSyncI18n.T("sw.sync_accessories"), _cfg.SyncAccessories.Value);
            if (syncAcc != _cfg.SyncAccessories.Value) _cfg.SyncAccessories.Value = syncAcc;
            bool syncTail = DrawSwitch(SkinSyncI18n.T("sw.sync_tail"), _cfg.SyncTailDeform.Value);
            if (syncTail != _cfg.SyncTailDeform.Value) _cfg.SyncTailDeform.Value = syncTail;
            GUI.enabled = prevEnabled;
            GUILayout.EndVertical();

            GUILayout.Space(12f);
            GUILayout.Label(SkinSyncI18n.T("sec.experimental"), BlackWhiteSkin.HeaderStyle);
            GUILayout.BeginVertical(BlackWhiteSkin.CardStyle);
            bool packSync = DrawSwitch(SkinSyncI18n.T("sw.skin_pack_sync"), _cfg.EnableSkinPackSync.Value);
            if (packSync != _cfg.EnableSkinPackSync.Value) _cfg.EnableSkinPackSync.Value = packSync;
            bool persistEnabled = _cfg.EnableSkinPackSync.Value;
            bool prevEnabledExp = GUI.enabled;
            GUI.enabled = prevEnabledExp && persistEnabled;
            bool persist = DrawSwitch(SkinSyncI18n.T("sw.skin_pack_persist"), _cfg.SkinPackPersistOnDisk.Value);
            if (persist != _cfg.SkinPackPersistOnDisk.Value) _cfg.SkinPackPersistOnDisk.Value = persist;
            bool allowPeers = DrawSwitch(SkinSyncI18n.T("sw.allow_peers_persist"), _cfg.AllowPeersPersistMyPack.Value);
            if (allowPeers != _cfg.AllowPeersPersistMyPack.Value) _cfg.AllowPeersPersistMyPack.Value = allowPeers;
            GUI.enabled = prevEnabledExp;
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
            DrawLanguageModeRow(SkinSyncI18n.T("lbl.language"), _cfg.PreferredLanguage);
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

        private void DrawSkinGrid(List<string> skins, string currentSkin)
        {
            float measured = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true)).width;
            if (Event.current.type == EventType.Repaint && measured > 200f)
            {
                _lastGridWidth = measured;
            }
            float available = _lastGridWidth > 200f ? _lastGridWidth : (WindowWidth - 68f);

            float cellW = Mathf.Floor((available - GridGap * (CellsPerRow - 1) - GridRightPad) / CellsPerRow);
            if (cellW < 80f) cellW = 80f;
            float side = cellW - CellInnerPad * 2f;
            float cellH = Mathf.Floor(CellInnerPad + side + CellChromeH);

            int row = 0;
            while (row * CellsPerRow < skins.Count)
            {
                GUILayout.BeginHorizontal();
                for (int c = 0; c < CellsPerRow; c++)
                {
                    int idx = row * CellsPerRow + c;
                    if (idx >= skins.Count)
                    {
                        GUILayoutUtility.GetRect(cellW, cellH, GUILayout.Width(cellW), GUILayout.Height(cellH));
                    }
                    else
                    {
                        DrawSkinCell(skins[idx], currentSkin, cellW, cellH);
                    }
                    if (c < CellsPerRow - 1) GUILayout.Space(GridGap);
                }
                GUILayout.Space(GridRightPad);
                GUILayout.EndHorizontal();
                GUILayout.Space(GridGap);
                row++;
            }
        }

        private void DrawSkinCell(string skin, string currentSkin, float cellW, float cellH)
        {
            bool isActive = !string.IsNullOrEmpty(currentSkin) && string.Equals(skin, currentSkin, StringComparison.Ordinal);

            Rect cellRect = GUILayoutUtility.GetRect(cellW, cellH,
                GUILayout.Width(cellW), GUILayout.Height(cellH),
                GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));

            GUI.DrawTexture(cellRect, BlackWhiteSkin.LineTex, ScaleMode.StretchToFill, false, 0f,
                new Color(0.06f, 0.06f, 0.06f, 0.42f), 0f, 0f);
            BlackWhiteSkin.DrawBorder(cellRect, 4f);

            float innerW = cellRect.width - CellInnerPad * 2f;
            float innerX = cellRect.x + CellInnerPad;

            const float btnH = 46f;
            const float nameH = 36f;
            const float gap = 4f;

            Rect btnRect = new Rect(innerX, cellRect.yMax - CellInnerPad - btnH, innerW, btnH);
            Rect titleRect = new Rect(innerX, btnRect.y - gap - nameH, innerW, nameH);

            float imgTop = cellRect.y + CellInnerPad;
            float imgBottom = titleRect.y - gap;
            float imgH = Mathf.Max(40f, imgBottom - imgTop);
            float side = Mathf.Min(innerW, imgH);
            Rect imgRect = new Rect(cellRect.x + (cellRect.width - side) * 0.5f, imgTop, side, side);

            GUI.DrawTexture(imgRect, BlackWhiteSkin.LineTex, ScaleMode.StretchToFill, false, 0f,
                new Color(0f, 0f, 0f, 0.28f), 0f, 0f);
            BlackWhiteSkin.DrawBorder(imgRect, 3f);

            Texture2D tex = null;
            try
            {
                string skinDir = SkinSyncMod.SkinPathResolver.GetSkinDir(skin);
                tex = SkinPreviewRenderer.GetOrBuild(skin, skinDir, _cfg);
            }
            catch (Exception ex)
            {
                SkinSyncMod.ModLog.Warning("preview build failed for " + skin + ": " + ex.Message);
            }
            if (tex != null)
            {
                GUI.DrawTexture(imgRect, tex, ScaleMode.ScaleToFit, true);
            }

            if (isActive)
            {
                GUI.Label(new Rect(imgRect.x + 2f, imgRect.y - 4f, 56f, 56f), "★", StarStyle);
            }

            GUI.Label(titleRect, skin, SkinNameStyle);

            bool prevEnabled = GUI.enabled;
            GUI.enabled = !isActive;
            if (GUI.Button(btnRect, isActive ? SkinSyncI18n.T("btn.skin_in_use") : SkinSyncI18n.T("btn.apply_skin")))
            {
                _onApplySkin?.Invoke(skin);
            }
            GUI.enabled = prevEnabled;
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
            DrawNameButton("huanxin886", "https://github.com/huanxin886");

            GUILayout.Space(16f);
            GUILayout.Label(SkinSyncI18n.T("about.testers"), CenterTitleStyle, GUILayout.ExpandWidth(true));
            GUILayout.Space(6f);
            DrawCreditLine(".....");

            GUILayout.Space(16f);
            GUILayout.Label(SkinSyncI18n.T("about.sec_deps"), CenterTitleStyle, GUILayout.ExpandWidth(true));
            GUILayout.Space(6f);
            DrawLinkButton("BepInEx", "https://github.com/BepInEx/BepInEx");

            GUILayout.Space(20f);
            GUILayout.EndScrollView();
        }

        private static void DrawLinkButton(string label, string url)
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

        private static void DrawCreditLine(string text)
        {
            GUILayout.Label(text, CenterLabelStyle, GUILayout.ExpandWidth(true), GUILayout.MinHeight(36f));
        }

        private static void DrawNameButton(string name, string url)
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

            int seg = Mathf.RoundToInt(DrawSliderRow(SkinSyncI18n.T("tail.segments"), TailDeformConfig.Segments, 4f, 40f, 1f));
            if (seg != TailDeformConfig.Segments) { TailDeformConfig.Segments = seg; changed = true; }
            int it = Mathf.RoundToInt(DrawSliderRow(SkinSyncI18n.T("tail.iters"), TailDeformConfig.ConstraintIters, 1f, 20f, 1f));
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
                _tailDirty = false;
                _onResetTail?.Invoke();
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            if (changed) _tailDirty = true;
            // 仅在松开鼠标后落盘与广播，避免拖动滑块期间逐帧写盘发包。
            if (_tailDirty && !Input.GetMouseButton(0))
            {
                FlushPendingTailSave();
            }
        }

        private void DrawAccessoryGroup(AccessoryRow row)
        {
            string key = "acc:" + row.Id;
            if (!_currentExpanded.ContainsKey(key)) _currentExpanded[key] = false;
            bool expanded = _currentExpanded[key];

            GUILayout.BeginHorizontal();
            string statusText = row.Enabled ? SkinSyncI18n.T("sw.on") : SkinSyncI18n.T("sw.off");
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
            GUILayout.Label(v.ToString("0.###", CultureInfo.InvariantCulture),
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
            GUILayout.Label(value.ToString(CultureInfo.InvariantCulture),
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
            GUILayout.Label(value.ToString("0.##", CultureInfo.InvariantCulture),
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

        private static void DrawLanguageModeRow(string label, ConfigEntry<string> entry)
        {
            string mode = NormalizeLanguageMode(entry.Value);
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.MinWidth(LabelColW), GUILayout.ExpandWidth(true),
                GUILayout.MinHeight(RowMinHeight));
            GUILayout.Space(20f);
            if (GUILayout.Button(SkinSyncI18n.T("opt.language_auto"),
                mode == "auto" ? BlackWhiteSkin.TabActiveStyle : BlackWhiteSkin.TabStyle,
                GUILayout.MinWidth(140f), GUILayout.ExpandWidth(false), GUILayout.MinHeight(RowMinHeight)))
            {
                entry.Value = "auto";
            }
            GUILayout.Space(8f);
            if (GUILayout.Button(SkinSyncI18n.T("opt.language_zh"),
                mode == "zh" ? BlackWhiteSkin.TabActiveStyle : BlackWhiteSkin.TabStyle,
                GUILayout.MinWidth(120f), GUILayout.ExpandWidth(false), GUILayout.MinHeight(RowMinHeight)))
            {
                entry.Value = "zh";
            }
            GUILayout.Space(8f);
            if (GUILayout.Button(SkinSyncI18n.T("opt.language_en"),
                mode == "en" ? BlackWhiteSkin.TabActiveStyle : BlackWhiteSkin.TabStyle,
                GUILayout.MinWidth(120f), GUILayout.ExpandWidth(false), GUILayout.MinHeight(RowMinHeight)))
            {
                entry.Value = "en";
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(6f);
        }

        private static string NormalizeLanguageMode(string mode)
        {
            if (string.IsNullOrWhiteSpace(mode)) return "auto";
            mode = mode.Trim().ToLowerInvariant();
            return mode == "zh" || mode == "en" ? mode : "auto";
        }
    }
}
