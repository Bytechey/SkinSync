using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace SkinSyncMod
{
    internal sealed partial class SkinSyncWindow
    {
        private const float CellChromeH = 110f;
        private const float GridGap = 14f;
        private const float CellInnerPad = 10f;
        private const float ScrollContentRightPad = 8f;

        private const string UrlModRepo = "https://github.com/Bytechey/SkinSync";
        private const string UrlModVideo = "https://www.bilibili.com/video/BV1p1GJ64EAG";
        private const string UrlBepInEx = "https://github.com/BepInEx/BepInEx";

        private readonly Dictionary<string, bool> _currentExpanded = new Dictionary<string, bool>();
        private readonly HashSet<string> _previewTriedNull = new HashSet<string>(StringComparer.Ordinal);
        private int _lastPreviewStateVersion = -1;

        private void DrawSettingsTab()
        {
            _settingsScroll = BeginVerticalScroll(_settingsScroll);
            try
            {
                GUILayout.BeginHorizontal();
                GUILayout.BeginVertical(GUILayout.ExpandWidth(true));

                DrawSectionHeader(SkinSyncI18n.T("sec.visual"), first: true);
                GUILayout.BeginVertical(BlackWhiteSkin.CardStyle);
                bool tailDeform = DrawSwitch(SkinSyncI18n.T("sw.tail_deform"), _cfg.TailDeformEnabled.Value);
                if (tailDeform != _cfg.TailDeformEnabled.Value) _cfg.TailDeformEnabled.Value = tailDeform;
                GUILayout.Space(RowGap);
                bool hideWear = DrawSwitch(SkinSyncI18n.T("sw.hide_game_wearables"), _cfg.HideGameWearables.Value);
                if (hideWear != _cfg.HideGameWearables.Value) _cfg.HideGameWearables.Value = hideWear;
                GUILayout.Space(RowGap);
                bool requireEq = DrawSwitch(SkinSyncI18n.T("sw.require_equipment"), _cfg.RequireEquipmentForAccessories.Value);
                if (requireEq != _cfg.RequireEquipmentForAccessories.Value) _cfg.RequireEquipmentForAccessories.Value = requireEq;
                GUILayout.Space(RowGap);
                bool renderBlood = DrawSwitch(SkinSyncI18n.T("sw.render_custom_blood"), _cfg.RenderCustomBlood.Value);
                if (renderBlood != _cfg.RenderCustomBlood.Value) _cfg.RenderCustomBlood.Value = renderBlood;
                GUILayout.Space(RowGap);
                bool vanillaSelf = DrawSwitch(SkinSyncI18n.T("sw.vanilla_self"), _cfg.VanillaSkinForSelf.Value);
                if (vanillaSelf != _cfg.VanillaSkinForSelf.Value) _cfg.VanillaSkinForSelf.Value = vanillaSelf;
                GUILayout.Space(RowGap);
                bool vanillaOthers = DrawSwitch(SkinSyncI18n.T("sw.vanilla_others"), _cfg.VanillaSkinForOthers.Value);
                if (vanillaOthers != _cfg.VanillaSkinForOthers.Value) _cfg.VanillaSkinForOthers.Value = vanillaOthers;
                GUILayout.EndVertical();

                DrawSectionHeader(SkinSyncI18n.T("sec.sync"));
                GUILayout.BeginVertical(BlackWhiteSkin.CardStyle);
                bool mpAvailable = SkinSync.IsMultiplayerSession;
                if (!mpAvailable)
                {
                    GUILayout.Label(SkinSyncI18n.T("sync.no_together_hint"),
                        BlackWhiteSkin.BodyTextStyle, GUILayout.ExpandWidth(true), GUILayout.MinHeight(40f));
                    GUILayout.Space(RowGap);
                }
                else if (!SkinSyncMod.Interop.Consumers.SessionConsumer.IsRunning)
                {
                    GUILayout.Label(SkinSyncI18n.T("sync.network_off_hint"),
                        BlackWhiteSkin.BodyTextStyle, GUILayout.ExpandWidth(true), GUILayout.MinHeight(40f));
                    GUILayout.Space(RowGap);
                }

                bool prevEnabled = GUI.enabled;
                GUI.enabled = prevEnabled && mpAvailable;
                DrawSyncModeRow();
                GUILayout.Space(RowGap);

                bool syncAcc = DrawSwitch(SkinSyncI18n.T("sw.sync_accessories"), _cfg.SyncAccessories.Value);
                if (syncAcc != _cfg.SyncAccessories.Value) _cfg.SyncAccessories.Value = syncAcc;
                GUILayout.Space(RowGap);
                bool syncTail = DrawSwitch(SkinSyncI18n.T("sw.sync_tail"), _cfg.SyncTailDeform.Value);
                if (syncTail != _cfg.SyncTailDeform.Value) _cfg.SyncTailDeform.Value = syncTail;

                GUILayout.Space(RowGap);
                bool packSync = DrawSwitch(SkinSyncI18n.T("sw.skin_pack_sync"), _cfg.EnableSkinPackSync.Value);
                if (packSync != _cfg.EnableSkinPackSync.Value) _cfg.EnableSkinPackSync.Value = packSync;

                BeginSubBlock();
                bool prevEnabledPack = GUI.enabled;
                GUI.enabled = prevEnabledPack && _cfg.EnableSkinPackSync.Value;
                bool persist = DrawSwitch(SkinSyncI18n.T("sw.skin_pack_persist"), _cfg.SkinPackPersistOnDisk.Value);
                if (persist != _cfg.SkinPackPersistOnDisk.Value) _cfg.SkinPackPersistOnDisk.Value = persist;
                GUILayout.Space(RowGap);
                bool allowPeers = DrawSwitch(SkinSyncI18n.T("sw.allow_peers_persist"), _cfg.AllowPeersPersistMyPack.Value);
                if (allowPeers != _cfg.AllowPeersPersistMyPack.Value) _cfg.AllowPeersPersistMyPack.Value = allowPeers;
                GUI.enabled = prevEnabled;
                EndSubBlock();
                GUILayout.EndVertical();

                DrawSectionHeader(SkinSyncI18n.T("sec.hotkeys"));
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

                DrawSectionHeader(SkinSyncI18n.T("sec.misc"));
                GUILayout.BeginVertical(BlackWhiteSkin.CardStyle);
                bool showLog = DrawSwitch(SkinSyncI18n.T("sw.show_log_in_console"), _cfg.ShowLogInConsole.Value);
                if (showLog != _cfg.ShowLogInConsole.Value) _cfg.ShowLogInConsole.Value = showLog;
                GUILayout.Space(RowGap);
                bool acceptUpdate = DrawSwitch(SkinSyncI18n.T("sw.accept_update_notice"), _cfg.AcceptUpdateNotice.Value);
                if (acceptUpdate != _cfg.AcceptUpdateNotice.Value) _cfg.AcceptUpdateNotice.Value = acceptUpdate;
                GUILayout.Space(RowGap);
                DrawLanguageModeRow(SkinSyncI18n.T("lbl.language"), _cfg.PreferredLanguage);
                GUILayout.EndVertical();

                GUILayout.Space(16f);
                GUILayout.EndVertical();
                GUILayout.Space(ScrollContentRightPad);
                GUILayout.EndHorizontal();
            }
            finally
            {
                GUILayout.EndScrollView();
            }
        }

        private void DrawSyncModeRow()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(SkinSyncI18n.T("lbl.sync_mode"), BlackWhiteSkin.RowLabelStyle,
                GUILayout.ExpandWidth(true), GUILayout.MinHeight(RowMinHeight));
            GUILayout.Space(12f);

            bool isOnEnter = !_cfg.IsPassiveSyncMode;
            string enterLabel = SkinSyncI18n.T("sync.mode_on_enter");
            string passiveLabel = SkinSyncI18n.T("sync.mode_passive");
            GUIStyle enterStyle = isOnEnter ? BlackWhiteSkin.ControlBtnActiveStyle : BlackWhiteSkin.ControlBtnStyle;
            GUIStyle passiveStyle = !isOnEnter ? BlackWhiteSkin.ControlBtnActiveStyle : BlackWhiteSkin.ControlBtnStyle;

            float enterW = CalcButtonWidth(enterStyle, enterLabel, 176f, 16f);
            float passiveW = CalcButtonWidth(passiveStyle, passiveLabel, 176f, 16f);
            Rect slot = ReserveVerticalCenterSlot(enterW + 8f + passiveW, ControlHeight);

            if (GUI.Button(new Rect(slot.x, slot.y, enterW, slot.height), enterLabel, enterStyle))
            {
                ClearTextFocus();
                if (!isOnEnter) _cfg.SyncMode.Value = "OnEnter";
            }
            if (GUI.Button(new Rect(slot.x + enterW + 8f, slot.y, passiveW, slot.height), passiveLabel, passiveStyle))
            {
                ClearTextFocus();
                if (isOnEnter) _cfg.SyncMode.Value = "Passive";
            }
            GUILayout.EndHorizontal();
        }

        private static void DrawLanguageModeRow(string label, ConfigEntry<string> entry)
        {
            string mode = SkinSyncI18n.NormalizeLanguageMode(entry.Value);
            string autoText = SkinSyncI18n.T("opt.language_auto");
            string zhText = SkinSyncI18n.T("opt.language_zh");
            string enText = SkinSyncI18n.T("opt.language_en");

            float autoW = CalcButtonWidth(BlackWhiteSkin.ControlBtnStyle, autoText, 136f, 16f);
            float zhW = CalcButtonWidth(BlackWhiteSkin.ControlBtnStyle, zhText, 96f, 16f);
            float enW = CalcButtonWidth(BlackWhiteSkin.ControlBtnStyle, enText, 96f, 16f);
            const float gap = 6f;
            float totalW = autoW + zhW + enW + gap * 2f;

            GUILayout.BeginHorizontal();
            GUILayout.Label(label, BlackWhiteSkin.RowLabelStyle,
                GUILayout.ExpandWidth(true), GUILayout.MinHeight(RowMinHeight));
            GUILayout.Space(12f);

            Rect slot = ReserveVerticalCenterSlot(totalW, ControlHeight);
            if (GUI.Button(new Rect(slot.x, slot.y, autoW, slot.height), autoText,
                mode == "auto" ? BlackWhiteSkin.ControlBtnActiveStyle : BlackWhiteSkin.ControlBtnStyle))
            {
                ClearTextFocus();
                entry.Value = "auto";
            }
            if (GUI.Button(new Rect(slot.x + autoW + gap, slot.y, zhW, slot.height), zhText,
                mode == "zh" ? BlackWhiteSkin.ControlBtnActiveStyle : BlackWhiteSkin.ControlBtnStyle))
            {
                ClearTextFocus();
                entry.Value = "zh";
            }
            if (GUI.Button(new Rect(slot.x + autoW + gap + zhW + gap, slot.y, enW, slot.height), enText,
                mode == "en" ? BlackWhiteSkin.ControlBtnActiveStyle : BlackWhiteSkin.ControlBtnStyle))
            {
                ClearTextFocus();
                entry.Value = "en";
            }
            GUILayout.EndHorizontal();
        }

        private void DrawSkinsTab()
        {
            var skins = _getSkins?.Invoke() ?? Array.Empty<string>();
            string currentSkin = _getCurrentSkin?.Invoke() ?? "";
            if (_lastPreviewStateVersion != SkinApplier.SkinStateVersion)
            {
                _lastPreviewStateVersion = SkinApplier.SkinStateVersion;
                _previewTriedNull.Clear();
            }

            GUILayout.BeginHorizontal();
            string countText = SkinSyncI18n.F("fmt.skins_count", skins.Count);
            float badgeW = CalcButtonWidth(BlackWhiteSkin.BadgeStyle, countText, 124f, 16f);
            Rect badgeSlot = GUILayoutUtility.GetRect(badgeW, ButtonHeight, GUILayout.Width(badgeW), GUILayout.Height(ButtonHeight));
            GUI.Label(new Rect(badgeSlot.x, badgeSlot.y + (ButtonHeight - BadgeHeight) * 0.5f, badgeW, BadgeHeight),
                countText, BlackWhiteSkin.BadgeStyle);
            GUILayout.FlexibleSpace();

            string rescanText = SkinSyncI18n.T("btn.rescan");
            float rescanW = CalcButtonWidth(BlackWhiteSkin.ButtonStyle, rescanText, 164f, 20f);
            if (GUILayout.Button(rescanText, BlackWhiteSkin.ButtonStyle, GUILayout.Width(rescanW), GUILayout.Height(ButtonHeight)))
            {
                ClearTextFocus();
                _previewTriedNull.Clear();
                _onRescan?.Invoke();
                skins = _getSkins?.Invoke() ?? Array.Empty<string>();
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(8f);

            // 必须在 BeginScrollView 外部测量宿主真实宽度，否则内部固定宽度元素会反向撑宽虚拟内容区形成死锁
            Rect outerProbe = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true));
            if (Event.current != null && Event.current.type == EventType.Repaint && outerProbe.width > 200f)
            {
                if (_drawingEmbedded) _measuredEmbeddedOuterW = outerProbe.width;
                else _measuredStandaloneOuterW = outerProbe.width;
            }

            _skinsScroll = BeginVerticalScroll(_skinsScroll);
            try
            {
                if (skins.Count == 0)
                {
                    GUILayout.Space(8f);
                    GUILayout.BeginHorizontal();
                    GUILayout.BeginVertical(BlackWhiteSkin.CardStyle, GUILayout.ExpandWidth(true));
                    GUILayout.Label(SkinSyncI18n.T("msg.no_skins"),
                        BlackWhiteSkin.BodyTextStyle, GUILayout.ExpandWidth(true), GUILayout.MinHeight(40f));
                    GUILayout.EndVertical();
                    GUILayout.Space(ScrollContentRightPad);
                    GUILayout.EndHorizontal();
                }
                else
                {
                    DrawSkinGrid(skins, currentSkin);
                }
                GUILayout.Space(16f);
            }
            finally
            {
                GUILayout.EndScrollView();
            }

            if (Event.current != null && Event.current.type == EventType.Repaint)
            {
                float vh = GUILayoutUtility.GetLastRect().height;
                if (vh > 100f) _measuredSkinsViewHeight = vh;
            }
        }

        private void DrawSkinGrid(IReadOnlyList<string> skins, string currentSkin)
        {
            bool isRepaint = Event.current != null && Event.current.type == EventType.Repaint;
            float available = _layoutGridWidth;
            int cols = available >= 860f ? 3 : (available >= 540f ? 2 : 1);

            float cellW = Mathf.Floor((available - GridGap * (cols - 1)) / cols);
            if (cellW < 180f) cellW = 180f;
            float side = cellW - CellInnerPad * 2f;
            float cellH = Mathf.Floor(CellInnerPad + side + CellChromeH);
            float rowStride = cellH + GridGap;
            float viewH = _layoutSkinsViewHeight > 100f ? _layoutSkinsViewHeight : _winH;
            float visibleTop = _skinsScroll.y - rowStride;
            float visibleBottom = _skinsScroll.y + viewH + rowStride;

            bool builtOneThisRepaint = false;
            int row = 0;
            while (row * cols < skins.Count)
            {
                float rowTop = row * rowStride;
                bool nearVisible = (rowTop + cellH) >= visibleTop && rowTop <= visibleBottom;

                GUILayout.BeginHorizontal();
                for (int c = 0; c < cols; c++)
                {
                    int idx = row * cols + c;
                    if (idx >= skins.Count)
                    {
                        GUILayoutUtility.GetRect(cellW, cellH, GUILayout.Width(cellW), GUILayout.Height(cellH));
                    }
                    else
                    {
                        DrawSkinCell(skins[idx], currentSkin, cellW, cellH,
                            isRepaint && nearVisible, ref builtOneThisRepaint);
                    }
                    if (c < cols - 1) GUILayout.Space(GridGap);
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(GridGap);
                row++;
            }
        }

        private void DrawSkinCell(string skin, string currentSkin, float cellW, float cellH,
            bool canBuildIfUncached, ref bool builtOneThisRepaint)
        {
            bool isActive = !string.IsNullOrEmpty(currentSkin) && string.Equals(skin, currentSkin, StringComparison.Ordinal);

            Rect cellRect = GUILayoutUtility.GetRect(cellW, cellH,
                GUILayout.Width(cellW), GUILayout.Height(cellH),
                GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));

            GUI.Box(cellRect, GUIContent.none, isActive ? BlackWhiteSkin.CardCurrentStyle : BlackWhiteSkin.SkinCellStyle);

            float innerW = cellRect.width - CellInnerPad * 2f;
            float innerX = cellRect.x + CellInnerPad;

            const float btnH = 44f;
            const float nameH = 42f;
            const float titleGap = 6f;
            const float imgGap = 8f;

            Rect btnRect = new Rect(innerX, cellRect.yMax - CellInnerPad - btnH, innerW, btnH);
            Rect titleRect = new Rect(innerX, btnRect.y - titleGap - nameH, innerW, nameH);

            float imgTop = cellRect.y + CellInnerPad;
            float imgBottom = titleRect.y - imgGap;
            float imgH = Mathf.Max(40f, imgBottom - imgTop);
            float side = Mathf.Min(innerW, imgH);
            Rect imgRect = new Rect(cellRect.x + (cellRect.width - side) * 0.5f, imgTop, side, side);

            GUI.Box(imgRect, GUIContent.none, BlackWhiteSkin.PreviewBoxStyle);

            Texture2D tex = null;
            try
            {
                tex = SkinPreviewRenderer.GetOrBuildIncremental(skin, null, _cfg, allowBuild: false);
                if (tex == null && canBuildIfUncached && !builtOneThisRepaint && !_previewTriedNull.Contains(skin))
                {
                    builtOneThisRepaint = true;
                    string skinDir = SkinPathResolver.GetSkinDir(skin);
                    tex = SkinPreviewRenderer.GetOrBuildIncremental(skin, skinDir, _cfg, allowBuild: true);
                    if (tex == null) _previewTriedNull.Add(skin);
                }
            }
            catch (Exception ex)
            {
                _previewTriedNull.Add(skin);
                ModLog.Warning("preview build failed for " + skin + ": " + ex.Message);
            }
            if (tex != null)
            {
                GUI.DrawTexture(imgRect, tex, ScaleMode.ScaleToFit, true);
            }

            if (isActive)
            {
                string activeTag = SkinSyncI18n.T("btn.skin_in_use");
                float tagW = CalcButtonWidth(BlackWhiteSkin.BadgeActiveStyle, activeTag, 82f, 14f);
                GUI.Label(new Rect(imgRect.x + 6f, imgRect.y + 6f, tagW, BadgeHeight), activeTag, BlackWhiteSkin.BadgeActiveStyle);
            }

            GUI.Label(titleRect, skin, BlackWhiteSkin.SkinNameStyle);

            if (isActive)
            {
                bool prevEnabled = GUI.enabled;
                GUI.enabled = false;
                GUI.Button(btnRect, SkinSyncI18n.T("btn.skin_in_use"), BlackWhiteSkin.ControlBtnActiveStyle);
                GUI.enabled = prevEnabled;
            }
            else if (GUI.Button(btnRect, SkinSyncI18n.T("btn.apply_skin"), BlackWhiteSkin.ButtonStyle))
            {
                ClearTextFocus();
                _onApplySkin?.Invoke(skin);
            }
        }

        private void DrawAboutTab()
        {
            _aboutScroll = BeginVerticalScroll(_aboutScroll);
            try
            {
                GUILayout.BeginHorizontal();
                GUILayout.BeginVertical(GUILayout.ExpandWidth(true));

                GUILayout.Space(4f);
                GUILayout.BeginVertical(BlackWhiteSkin.CardStyle);
                GUILayout.Label(SkinSyncI18n.T("about.title"), BlackWhiteSkin.AboutTitleStyle,
                    GUILayout.ExpandWidth(true), GUILayout.MinHeight(54f));
                GUILayout.Space(4f);
                GUILayout.Label(SkinSyncI18n.T("about.desc"), BlackWhiteSkin.AboutBodyStyle,
                    GUILayout.ExpandWidth(true), GUILayout.MinHeight(40f));
                GUILayout.Space(6f);
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                string verText = SkinSyncI18n.F("about.version", SkinSync.Version);
                float verW = CalcButtonWidth(BlackWhiteSkin.BadgeStyle, verText, 136f, 16f);
                GUILayout.Label(verText, BlackWhiteSkin.BadgeStyle, GUILayout.Width(verW), GUILayout.Height(BadgeHeight));
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();

                DrawSectionHeader(SkinSyncI18n.T("about.sec_links"));
                GUILayout.BeginVertical(BlackWhiteSkin.CardStyle);
                DrawDualUrlRow(
                    SkinSyncI18n.T("about.link_mod_repo"), UrlModRepo,
                    SkinSyncI18n.T("about.link_mod_video"), UrlModVideo);
                GUILayout.EndVertical();

                DrawSectionHeader(SkinSyncI18n.T("about.sec_credits"));
                GUILayout.BeginVertical(BlackWhiteSkin.CardStyle);
                DrawDualUrlRow("Bytechey", "https://github.com/Bytechey", "huanxin886", "https://github.com/huanxin886");
                GUILayout.EndVertical();

                DrawSectionHeader(SkinSyncI18n.T("about.testers"));
                GUILayout.BeginVertical(BlackWhiteSkin.CardStyle);
                GUILayout.Label("Cross, Meteor, Rdby", BlackWhiteSkin.SingleLineLabelStyle,
                    GUILayout.ExpandWidth(true), GUILayout.MinHeight(44f));
                GUILayout.EndVertical();

                DrawSectionHeader(SkinSyncI18n.T("about.sec_deps"));
                GUILayout.BeginVertical(BlackWhiteSkin.CardStyle);
                DrawDualUrlRow("BepInEx", UrlBepInEx);
                GUILayout.EndVertical();

                GUILayout.Space(16f);
                GUILayout.EndVertical();
                GUILayout.Space(ScrollContentRightPad);
                GUILayout.EndHorizontal();
            }
            finally
            {
                GUILayout.EndScrollView();
            }
        }

        private static void DrawDualUrlRow(string leftLabel, string leftUrl, string rightLabel = null, string rightUrl = null)
        {
            const float colGap = 16f;
            const float btnH = 44f;
            Rect rowRect = GUILayoutUtility.GetRect(1f, btnH, GUILayout.ExpandWidth(true));
            if (rightLabel == null)
            {
                if (GUI.Button(rowRect, leftLabel, BlackWhiteSkin.ButtonStyle))
                {
                    ClearTextFocus();
                    OpenUrlSafely(leftUrl);
                }
                return;
            }

            float colW = Mathf.Floor((rowRect.width - colGap) * 0.5f);
            if (GUI.Button(new Rect(rowRect.x, rowRect.y, colW, btnH), leftLabel, BlackWhiteSkin.ButtonStyle))
            {
                ClearTextFocus();
                OpenUrlSafely(leftUrl);
            }
            if (GUI.Button(new Rect(rowRect.x + rowRect.width - colW, rowRect.y, colW, btnH), rightLabel, BlackWhiteSkin.ButtonStyle))
            {
                ClearTextFocus();
                OpenUrlSafely(rightUrl);
            }
        }

        private static void OpenUrlSafely(string url)
        {
            try { Application.OpenURL(url); } catch { }
        }

        private void DrawCurrentTab()
        {
            string currentSkin = _getCurrentSkin?.Invoke() ?? "";
            DrawSectionHeader(string.IsNullOrEmpty(currentSkin)
                ? SkinSyncI18n.T("status.no_skin")
                : SkinSyncI18n.F("status.current_skin", currentSkin), first: true);

            _accessoriesScroll = BeginVerticalScroll(_accessoriesScroll);
            try
            {
                GUILayout.BeginHorizontal();
                GUILayout.BeginVertical(GUILayout.ExpandWidth(true));

                if (string.IsNullOrEmpty(currentSkin))
                {
                    GUILayout.BeginVertical(BlackWhiteSkin.CardStyle);
                    GUILayout.Label(SkinSyncI18n.T("msg.no_skin_yet"),
                        BlackWhiteSkin.BodyTextStyle, GUILayout.ExpandWidth(true), GUILayout.MinHeight(40f));
                    GUILayout.EndVertical();
                    GUILayout.EndVertical();
                    GUILayout.Space(ScrollContentRightPad);
                    GUILayout.EndHorizontal();
                    return;
                }

                DrawTailGroup();
                DrawSectionHeader(SkinSyncI18n.T("sec.accessories"));
                var rows = _getAccessories?.Invoke() ?? Array.Empty<AccessoryRow>();
                if (rows.Count == 0)
                {
                    GUILayout.BeginVertical(BlackWhiteSkin.CardStyle);
                    GUILayout.Label(SkinSyncI18n.T("msg.no_accessories"),
                        BlackWhiteSkin.BodyTextStyle, GUILayout.ExpandWidth(true), GUILayout.MinHeight(40f));
                    GUILayout.EndVertical();
                }
                else
                {
                    for (int i = 0; i < rows.Count; i++)
                    {
                        DrawAccessoryGroup(rows[i]);
                        GUILayout.Space(RowGap);
                    }
                }

                GUILayout.Space(16f);
                GUILayout.EndVertical();
                GUILayout.Space(ScrollContentRightPad);
                GUILayout.EndHorizontal();
            }
            finally
            {
                GUILayout.EndScrollView();
            }
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
                GUILayout.ExpandWidth(true), GUILayout.Height(50f)))
            {
                ClearTextFocus();
                _currentExpanded[key] = !expanded;
                _tailResetConfirmUntil = 0f;
                _hasLayoutSnapshot = false;
                GUIUtility.ExitGUI();
            }
            if (!expanded) return;

            GUILayout.BeginVertical(BlackWhiteSkin.CardStyle);
            bool changed = false;

            bool fg = DrawSwitch(SkinSyncI18n.T("tail.front_guard"), TailDeformConfig.FrontGuard);
            if (fg != TailDeformConfig.FrontGuard) { TailDeformConfig.FrontGuard = fg; changed = true; }
            GUILayout.Space(RowGap);

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
            DrawTailResetRow();
            GUILayout.EndVertical();

            if (changed) _tailDirty = true;
            // 仅在松开鼠标后落盘与广播，避免拖动滑块期间逐帧写盘发包
            if (_tailDirty && !Input.GetMouseButton(0))
            {
                FlushPendingTailSave();
            }
        }

        private void DrawTailResetRow()
        {
            GUILayout.BeginHorizontal();
            if (!_tailResetConfirmActive)
            {
                string resetText = SkinSyncI18n.T("tail.reset");
                float resetW = CalcButtonWidth(BlackWhiteSkin.ButtonStyle, resetText, 176f, 20f);
                if (GUILayout.Button(resetText, BlackWhiteSkin.ButtonStyle, GUILayout.Width(resetW), GUILayout.Height(ButtonHeight)))
                {
                    ClearTextFocus();
                    _tailResetConfirmUntil = Time.unscaledTime + 4f;
                    _hasLayoutSnapshot = false;
                    GUILayout.EndHorizontal();
                    GUIUtility.ExitGUI();
                }
            }
            else
            {
                string confirmText = SkinSyncI18n.T("tail.confirm_reset");
                string cancelText = SkinSyncI18n.T("btn.cancel");
                float confirmW = CalcButtonWidth(BlackWhiteSkin.ControlBtnActiveStyle, confirmText, 156f, 20f);
                float cancelW = CalcButtonWidth(BlackWhiteSkin.ControlBtnStyle, cancelText, 116f, 18f);

                if (GUILayout.Button(confirmText, BlackWhiteSkin.ControlBtnActiveStyle, GUILayout.Width(confirmW), GUILayout.Height(ButtonHeight)))
                {
                    ClearTextFocus();
                    _tailDirty = false;
                    _tailResetConfirmUntil = 0f;
                    _hasLayoutSnapshot = false;
                    _onResetTail?.Invoke();
                    GUILayout.EndHorizontal();
                    GUIUtility.ExitGUI();
                }
                GUILayout.Space(10f);
                if (GUILayout.Button(cancelText, BlackWhiteSkin.ControlBtnStyle, GUILayout.Width(cancelW), GUILayout.Height(ButtonHeight)))
                {
                    ClearTextFocus();
                    _tailResetConfirmUntil = 0f;
                    _hasLayoutSnapshot = false;
                    GUILayout.EndHorizontal();
                    GUIUtility.ExitGUI();
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawAccessoryGroup(AccessoryRow row)
        {
            string key = "acc:" + row.Id;
            if (!_currentExpanded.ContainsKey(key)) _currentExpanded[key] = false;
            bool expanded = _currentExpanded[key];

            string statusText = row.Enabled ? SkinSyncI18n.T("lbl.tail_on") : SkinSyncI18n.T("lbl.tail_off");
            string displayName;
            try { displayName = Locale.GetItem(row.Id); }
            catch { displayName = row.Id; }
            if (string.IsNullOrEmpty(displayName)) displayName = row.Id;
            string label = (expanded ? "▼  " : "▶  ") + displayName + "    " + statusText;

            if (GUILayout.Button(label, BlackWhiteSkin.HeaderButtonStyle,
                GUILayout.ExpandWidth(true), GUILayout.Height(50f)))
            {
                ClearTextFocus();
                _currentExpanded[key] = !expanded;
                GUIUtility.ExitGUI();
            }
            if (!expanded) return;

            GUILayout.BeginVertical(BlackWhiteSkin.CardStyle);

            if (!string.IsNullOrEmpty(row.RequireSlot))
            {
                GUILayout.Label(
                    SkinSyncI18n.F("acc.require_slot", SkinSyncI18n.LocalizeWearSlot(row.RequireSlot)),
                    BlackWhiteSkin.MutedLabelStyle, GUILayout.MinHeight(38f));
                GUILayout.Space(4f);
            }

            bool nextEnabled = DrawSwitch(SkinSyncI18n.T("acc.enabled"), row.Enabled);
            if (nextEnabled != row.Enabled)
            {
                _onToggleAccessory?.Invoke(row.Id, nextEnabled);
            }
            GUILayout.Space(RowGap);

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
    }
}
