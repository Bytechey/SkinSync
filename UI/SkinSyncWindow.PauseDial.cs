using System;
using System.Globalization;
using BepInEx.Configuration;
using UnityEngine;

namespace SkinSyncMod
{
    internal sealed partial class SkinSyncWindow
    {
        internal bool IsEmbedded { get; set; }

        private void DrawPauseDialSection()
        {
            // 嵌入 CuSaveManager 侧栏时不挂载独立暂停表盘按钮，同步隐藏该配置卡片。
            if (_drawingEmbedded || IsEmbedded) return;

            DrawSectionHeader(SkinSyncI18n.T("sec.pause_dial"));
            GUILayout.BeginVertical(BlackWhiteSkin.CardStyle);

            bool autoAvoid = DrawSwitch(SkinSyncI18n.T("sw.pause_dial_auto_avoid"), _cfg.PauseDialAutoAvoid.Value);
            if (autoAvoid != _cfg.PauseDialAutoAvoid.Value)
            {
                _cfg.PauseDialAutoAvoid.Value = autoAvoid;
            }

            GUILayout.Space(10f);

            DrawPauseDialFloatSlider(
                SkinSyncI18n.F("fmt.pause_dial_angle", _cfg.PauseDialAngleDeg.Value),
                _cfg.PauseDialAngleDeg,
                -180f,
                180f,
                snap: 0.5f,
                fallbackBadge: _cfg.PauseDialAngleDeg.Value.ToString("0.#", CultureInfo.InvariantCulture) + "°");

            DrawPauseDialFloatSlider(
                SkinSyncI18n.F("fmt.pause_dial_radius", _cfg.PauseDialRadiusOffset.Value),
                _cfg.PauseDialRadiusOffset,
                PauseDialLayoutSolver.MinRadiusOffset,
                PauseDialLayoutSolver.MaxRadiusOffset,
                snap: 1f,
                fallbackBadge: _cfg.PauseDialRadiusOffset.Value.ToString("+0.#;-0.#;0", CultureInfo.InvariantCulture) + " px");

            DrawPauseDialIntSlider(
                SkinSyncI18n.F("fmt.pause_dial_scale", _cfg.PauseDialScalePercent.Value),
                _cfg.PauseDialScalePercent,
                60,
                160);

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            string resetText = SkinSyncI18n.T("btn.pause_dial_reset");
            float resetW = CalcButtonWidth(BlackWhiteSkin.ButtonStyle, resetText, 220f, 20f);
            if (GUILayout.Button(resetText, BlackWhiteSkin.ButtonStyle, GUILayout.Width(resetW), GUILayout.Height(ButtonHeight)))
            {
                _cfg.ResetPauseDialLayout();
                ClearTextFocus();
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.Label(
                SkinSyncI18n.T("hint.pause_dial_drag"),
                BlackWhiteSkin.MutedLabelStyle,
                GUILayout.ExpandWidth(true),
                GUILayout.MinHeight(34f));

            GUILayout.EndVertical();
        }

        private static void DrawPauseDialFloatSlider(
            string formattedLabel,
            ConfigEntry<float> entry,
            float min,
            float max,
            float snap,
            string fallbackBadge)
        {
            SplitDialLabelAndBadge(formattedLabel, fallbackBadge, out string title, out string badge);

            bool prevChanged = GUI.changed;
            GUI.changed = false;

            float badgeW = CalcButtonWidth(BlackWhiteSkin.ValueBoxStyle, badge, 92f, 12f);
            GUILayout.BeginHorizontal();
            GUILayout.Label(title, BlackWhiteSkin.RowLabelStyle, GUILayout.Width(LabelColW), GUILayout.MinHeight(RowMinHeight));
            GUILayout.Space(12f);
            float raw = GUILayout.HorizontalSlider(
                entry.Value,
                min,
                max,
                BlackWhiteSkin.SliderTrackStyle,
                BlackWhiteSkin.SliderThumbStyle,
                GUILayout.MinWidth(140f),
                GUILayout.ExpandWidth(true));
            bool moved = GUI.changed;
            GUILayout.Space(12f);
            Rect valSlot = ReserveVerticalCenterSlot(badgeW, ControlHeight);
            GUI.Label(valSlot, badge, BlackWhiteSkin.ValueBoxStyle);
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);

            if (moved)
            {
                ClearTextFocus();
                float next = snap > 0f ? Mathf.Round(raw / snap) * snap : raw;
                next = (float)Math.Round(Mathf.Clamp(next, min, max), 2);
                if (Mathf.Abs(next - entry.Value) > 0.0005f)
                {
                    entry.Value = next;
                    prevChanged = true;
                }
            }

            GUI.changed = prevChanged;
        }

        private static void DrawPauseDialIntSlider(string formattedLabel, ConfigEntry<int> entry, int min, int max)
        {
            string fallbackBadge = entry.Value.ToString(CultureInfo.InvariantCulture) + "%";
            SplitDialLabelAndBadge(formattedLabel, fallbackBadge, out string title, out string badge);

            bool prevChanged = GUI.changed;
            GUI.changed = false;

            float badgeW = CalcButtonWidth(BlackWhiteSkin.ValueBoxStyle, badge, 92f, 12f);
            GUILayout.BeginHorizontal();
            GUILayout.Label(title, BlackWhiteSkin.RowLabelStyle, GUILayout.Width(LabelColW), GUILayout.MinHeight(RowMinHeight));
            GUILayout.Space(12f);
            float raw = GUILayout.HorizontalSlider(
                entry.Value,
                min,
                max,
                BlackWhiteSkin.SliderTrackStyle,
                BlackWhiteSkin.SliderThumbStyle,
                GUILayout.MinWidth(140f),
                GUILayout.ExpandWidth(true));
            bool moved = GUI.changed;
            GUILayout.Space(12f);
            Rect valSlot = ReserveVerticalCenterSlot(badgeW, ControlHeight);
            GUI.Label(valSlot, badge, BlackWhiteSkin.ValueBoxStyle);
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);

            if (moved)
            {
                ClearTextFocus();
                int next = Mathf.Clamp(Mathf.RoundToInt(raw), min, max);
                if (next != entry.Value)
                {
                    entry.Value = next;
                    prevChanged = true;
                }
            }

            GUI.changed = prevChanged;
        }

        private static void SplitDialLabelAndBadge(string formattedLabel, string fallbackBadge, out string title, out string badge)
        {
            if (!string.IsNullOrEmpty(formattedLabel))
            {
                int sep = formattedLabel.IndexOf('：');
                if (sep < 0) sep = formattedLabel.IndexOf(':');
                if (sep > 0 && sep < formattedLabel.Length - 1)
                {
                    title = formattedLabel.Substring(0, sep).Trim();
                    badge = formattedLabel.Substring(sep + 1).Trim();
                    return;
                }
            }

            title = formattedLabel ?? string.Empty;
            badge = fallbackBadge ?? string.Empty;
        }
    }
}
