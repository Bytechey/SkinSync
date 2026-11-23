using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BepInEx.Configuration;
using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>管理模组持久化配置项、快捷键绑定以及按皮肤存储的配件与尾巴参数覆盖。</summary>
    internal sealed class SkinSyncSettings
    {
        internal ConfigEntry<KeyboardShortcut> TogglePanelHotkey { get; }
        internal ConfigEntry<KeyboardShortcut> NextSkinHotkey { get; }
        internal ConfigEntry<KeyboardShortcut> PrevSkinHotkey { get; }
        internal ConfigEntry<KeyboardShortcut> RescanSkinsHotkey { get; }

        internal ConfigEntry<string> CurrentSkin { get; }

        internal ConfigEntry<bool> HideGameWearables { get; }
        internal ConfigEntry<bool> RequireEquipmentForAccessories { get; }
        internal ConfigEntry<bool> RenderCustomBlood { get; }
        internal ConfigEntry<bool> TailDeformEnabled { get; }
        internal ConfigEntry<bool> VanillaSkinForSelf { get; }
        internal ConfigEntry<bool> VanillaSkinForOthers { get; }

        internal ConfigEntry<bool> ShowLogInConsole { get; }
        internal ConfigEntry<bool> AcceptUpdateNotice { get; }
        internal ConfigEntry<string> PreferredLanguage { get; }
        internal ConfigEntry<int> UiScalePercent { get; }

        internal ConfigFile RawConfig => _config;
        internal ConfigEntry<bool> PauseDialAutoAvoid { get; }
        internal ConfigEntry<float> PauseDialAngleDeg { get; }
        internal ConfigEntry<float> PauseDialRadiusOffset { get; }
        internal ConfigEntry<int> PauseDialScalePercent { get; }

        internal ConfigEntry<string> SyncMode { get; }
        internal bool IsPassiveSyncMode => string.Equals(SyncMode.Value?.Trim(), "Passive", StringComparison.OrdinalIgnoreCase);
        internal ConfigEntry<bool> SyncAccessories { get; }
        internal ConfigEntry<bool> SyncTailDeform { get; }

        internal ConfigEntry<bool> EnableSkinPackSync { get; }
        internal ConfigEntry<bool> SkinPackPersistOnDisk { get; }
        internal ConfigEntry<bool> AllowPeersPersistMyPack { get; }

        internal sealed class AccessoryOverride
        {
            public bool? Enabled;
            public int? OffX;
            public int? OffY;
            public float? Rotation;
            public int? ZOrder;
        }

        internal sealed class TailDeformOverride
        {
            public bool? FrontGuard;
            public int? Segments;
            public int? ConstraintIters;
            public float? Damping;
            public float? SpeedDamping;
            public float? Stiffness;
            public float? MaxBendDeg;
            public float? AnchorFollow;
            public float? Smoothness;
            public float? MaxStep;
            public float? MaxFixedDt;
            public float? FrontGuardMargin;
            public float? GravityX;
            public float? GravityY;
            public float? WindFreq;
            public float? WindAmp;
            public float? SpeedDisturb;
        }

        private readonly ConfigFile _config;
        private readonly ConfigEntry<string> _accessoryOverrides;
        private readonly ConfigEntry<string> _tailDeformOverrides;

        private readonly Dictionary<string, Dictionary<string, AccessoryOverride>> _accOverrides
            = new Dictionary<string, Dictionary<string, AccessoryOverride>>();

        private readonly Dictionary<string, TailDeformOverride> _tailOverrides
            = new Dictionary<string, TailDeformOverride>();

        internal SkinSyncSettings(ConfigFile config)
        {
            _config = config;
            TogglePanelHotkey = config.Bind("Hotkeys", "TogglePanel",
                new KeyboardShortcut(KeyCode.F10),
                "打开 / 关闭 SkinSync 配置面板。");
            NextSkinHotkey = config.Bind("Hotkeys", "NextSkin",
                new KeyboardShortcut(KeyCode.F6),
                "切换到下一个皮肤。");
            PrevSkinHotkey = config.Bind("Hotkeys", "PrevSkin",
                new KeyboardShortcut(KeyCode.F7),
                "切换到上一个皮肤。");
            RescanSkinsHotkey = config.Bind("Hotkeys", "RescanSkins",
                new KeyboardShortcut(KeyCode.F8),
                "重新扫描皮肤目录。");

            CurrentSkin = config.Bind("Session", "CurrentSkin", "",
                "上次使用的皮肤 ID，进入游戏后自动应用。");

            HideGameWearables = config.Bind("Visual", "HideGameWearables", false,
                "隐藏游戏中玩家穿戴的装备 sprite（保留装备逻辑：装甲值 / 保暖等仍正常生效），避免装备遮挡 SkinSync 配件。");

            RequireEquipmentForAccessories = config.Bind("Visual", "RequireEquipmentForAccessories", false,
                "配件 accessories.json 中配了 requireWornSlot 时，玩家未穿戴对应装备 slot 则隐藏该配件。关闭此开关让配件无视依赖永远显示。");

            RenderCustomBlood = config.Bind("Visual", "RenderCustomBlood", false,
                "是否按皮肤 blood.json 渲染自定义血液（受伤血色 / 流血 / 落地血迹 / 爆血 / 呕吐）。关闭时全部回退游戏默认并停止每帧扫描。");

            TailDeformEnabled = config.Bind("Visual", "TailDeformEnabled", true,
                "是否启用尾巴物理形变。关闭时尾巴显示原始 sprite 不做摆动。");

            VanillaSkinForSelf = config.Bind("Visual", "VanillaSkinForSelf", false,
                "只自己使用游戏原版皮肤：本地玩家恢复成游戏原生外观，其他玩家不受影响。");

            VanillaSkinForOthers = config.Bind("Visual", "VanillaSkinForOthers", false,
                "除自己外都用原版皮肤：远端玩家恢复成游戏原生外观，自己的皮肤不受影响。");

            ShowLogInConsole = config.Bind("Misc", "ShowInConsole", false,
                "是否把模组日志同步打印到游戏内控制台（` 键打开）。关闭时仅写入 BepInEx 日志。");
            AcceptUpdateNotice = config.Bind("Misc", "AcceptUpdateNotice", true,
                "是否在启动时检查 GitHub 新版本并在游戏内提示。");
            PreferredLanguage = config.Bind("I18n", "PreferredLanguage", "auto",
                "界面语言：auto=跟随游戏；zh=强制中文；en=强制英文。");
            UiScalePercent = config.Bind("UI", "UiScalePercent", 100,
                new ConfigDescription(
                    "界面缩放百分比（75-150）。检测到 CuSaveManager 时自动跟随主面板统一缩放。",
                    new AcceptableValueRange<int>(75, 150)));

            PauseDialAutoAvoid = config.Bind("PauseDial", "AutoAvoidOverlap", true,
                "暂停表盘按钮是否自动避让原版按钮与其他模组按钮/面板，并在空间拥挤时自适应缩小。");
            PauseDialAngleDeg = config.Bind("PauseDial", "AngleDegrees", PauseDialLayoutSolver.DefaultAngleDeg,
                new ConfigDescription("暂停表盘按钮环绕角度（度，0=正右，90=正上，-90=正下，±180=正左）。",
                    new AcceptableValueRange<float>(-180f, 180f)));
            PauseDialRadiusOffset = config.Bind("PauseDial", "RadiusOffset", 0f,
                new ConfigDescription("暂停表盘按钮相对原版按钮轨道的半径偏移（像素）。",
                    new AcceptableValueRange<float>(PauseDialLayoutSolver.MinRadiusOffset, PauseDialLayoutSolver.MaxRadiusOffset)));
            PauseDialScalePercent = config.Bind("PauseDial", "ScalePercent", 100,
                new ConfigDescription("暂停表盘按钮大小百分比（60% ~ 160%）。",
                    new AcceptableValueRange<int>(60, 160)));

            SyncMode = config.Bind("Sync", "Mode", "OnEnter",
                "多人同步策略。OnEnter=进入游戏时同步皮肤与已保存的配件/尾巴覆盖；Passive=入局只报皮肤名，改动参数时再广播。");
            string rawSyncMode = SyncMode.Value?.Trim();
            if (!string.Equals(rawSyncMode, "OnEnter", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(rawSyncMode, "Passive", StringComparison.OrdinalIgnoreCase))
            {
                ModLog.Warning($"未知的 Sync.Mode 配置值 \"{SyncMode.Value}\"，将按 OnEnter 处理（有效值：OnEnter / Passive）。");
            }
            SyncAccessories = config.Bind("Sync", "Accessories", true,
                "本机改动配件覆盖时广播给其他玩家。");
            SyncTailDeform = config.Bind("Sync", "TailDeform", true,
                "本机改动尾巴形变参数时广播给其他玩家。");

            EnableSkinPackSync = config.Bind("Experimental", "EnableSkinPackSync", true,
                "缺少其他玩家使用的皮肤时，自动从持有方拉取皮肤包到临时会话缓存。");
            SkinPackPersistOnDisk = config.Bind("Experimental", "SkinPackPersistOnDisk", false,
                "接收到的皮肤包永久写入 plugins/CustomSprites/；关闭时仅写入 BepInEx/cache/SkinSync_SyncCache/ 临时会话缓存并在下次启动清理。");
            AllowPeersPersistMyPack = config.Bind("Experimental", "AllowPeersPersistMyPack", true,
                "允许接收到本机皮肤包的玩家将其永久落盘到 CustomSprites。");

            _accessoryOverrides = config.Bind("Session", "AccessoryOverrides", "",
                "配件覆盖。格式：每条 \"skin|accId=k1=v1,k2=v2,...\" 用分号分隔。");
            ParseAccessoryOverrides(_accessoryOverrides.Value);

            _tailDeformOverrides = config.Bind("Session", "TailDeformOverrides", "",
                "尾巴形变覆盖。格式：每条 \"skin|=field=value,field=value\" 用分号分隔。");
            ParseTailOverrides(_tailDeformOverrides.Value);
        }

        internal void SetUiScalePercent(int percent)
        {
            int clamped = Mathf.Clamp(percent, 75, 150);
            if (UiScalePercent != null)
            {
                UiScalePercent.Value = clamped;
            }
            _config?.Save();
        }

        internal void ResetPauseDialLayout()
        {
            PauseDialAutoAvoid.Value = true;
            PauseDialAngleDeg.Value = PauseDialLayoutSolver.DefaultAngleDeg;
            PauseDialRadiusOffset.Value = 0f;
            PauseDialScalePercent.Value = 100;
        }

        internal static bool IsBound(KeyboardShortcut sc) => sc.MainKey != KeyCode.None;

        /// <summary>检测快捷键是否在本帧触发，仅核对 Shift/Ctrl/Alt 三组标准修饰键以避免移动键 WASD 吞掉热键。</summary>
        internal static bool TriggeredThisFrame(ConfigEntry<KeyboardShortcut> entry)
        {
            if (entry == null) return false;
            var sc = entry.Value;
            if (sc.MainKey == KeyCode.None || !Input.GetKeyDown(sc.MainKey)) return false;

            bool wantShift = false, wantCtrl = false, wantAlt = false;
            if (sc.Modifiers != null)
            {
                foreach (KeyCode mod in sc.Modifiers)
                {
                    if (mod == KeyCode.LeftShift || mod == KeyCode.RightShift) wantShift = true;
                    else if (mod == KeyCode.LeftControl || mod == KeyCode.RightControl) wantCtrl = true;
                    else if (mod == KeyCode.LeftAlt || mod == KeyCode.RightAlt) wantAlt = true;
                }
            }

            if (sc.MainKey != KeyCode.LeftShift && sc.MainKey != KeyCode.RightShift)
            {
                bool hasShift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                if (wantShift != hasShift) return false;
            }

            if (sc.MainKey != KeyCode.LeftControl && sc.MainKey != KeyCode.RightControl)
            {
                bool hasCtrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                if (wantCtrl != hasCtrl) return false;
            }

            if (sc.MainKey != KeyCode.LeftAlt && sc.MainKey != KeyCode.RightAlt)
            {
                bool hasAlt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
                if (wantAlt != hasAlt) return false;
            }

            return true;
        }

        internal AccessoryOverride GetAccessoryOverride(string skinId, string accId)
        {
            if (string.IsNullOrEmpty(skinId) || string.IsNullOrEmpty(accId)) return null;
            if (_accOverrides.TryGetValue(skinId, out var inner)
                && inner.TryGetValue(accId, out var ov)) return ov;
            return null;
        }

        internal IReadOnlyDictionary<string, AccessoryOverride> GetAllAccessoryOverridesForSkin(string skinId)
        {
            if (string.IsNullOrEmpty(skinId)) return null;
            return _accOverrides.TryGetValue(skinId, out var inner) ? inner : null;
        }

        internal void SetAccessoryEnabled(string skinId, string accId, bool enabled)
        {
            if (string.IsNullOrEmpty(skinId) || string.IsNullOrEmpty(accId)) return;
            EnsureAcc(skinId, accId).Enabled = enabled;
            _accessoryOverrides.Value = SerializeAccessoryOverrides();
        }

        internal void SetAccessoryTransform(string skinId, string accId, int offX, int offY, float rot, int z)
        {
            if (string.IsNullOrEmpty(skinId) || string.IsNullOrEmpty(accId)) return;
            var ov = EnsureAcc(skinId, accId);
            ov.OffX = offX;
            ov.OffY = offY;
            ov.Rotation = rot;
            ov.ZOrder = z;
            _accessoryOverrides.Value = SerializeAccessoryOverrides();
        }

        private AccessoryOverride EnsureAcc(string skin, string acc)
        {
            if (!_accOverrides.TryGetValue(skin, out var inner))
            {
                inner = new Dictionary<string, AccessoryOverride>();
                _accOverrides[skin] = inner;
            }
            if (!inner.TryGetValue(acc, out var ov))
            {
                ov = new AccessoryOverride();
                inner[acc] = ov;
            }
            return ov;
        }

        private void ParseAccessoryOverrides(string raw)
        {
            _accOverrides.Clear();
            if (string.IsNullOrEmpty(raw)) return;
            foreach (string seg in raw.Split(';'))
            {
                if (string.IsNullOrWhiteSpace(seg)) continue;
                int eq = seg.IndexOf('=');
                if (eq <= 0) continue;
                string left = seg.Substring(0, eq);
                string right = seg.Substring(eq + 1).Trim();
                int bar = left.IndexOf('|');
                if (bar <= 0 || bar >= left.Length - 1) continue;
                string skin = UnescapeKey(left.Substring(0, bar));
                string acc = UnescapeKey(left.Substring(bar + 1));
                if (string.IsNullOrEmpty(skin) || string.IsNullOrEmpty(acc)) continue;

                var ov = EnsureAcc(skin, acc);
                if (right.Equals("on", StringComparison.OrdinalIgnoreCase)) { ov.Enabled = true; continue; }
                if (right.Equals("off", StringComparison.OrdinalIgnoreCase)) { ov.Enabled = false; continue; }

                foreach (string kv in right.Split(','))
                {
                    string p = kv.Trim();
                    if (p.Length == 0) continue;
                    if (p.Equals("on", StringComparison.OrdinalIgnoreCase)) { ov.Enabled = true; continue; }
                    if (p.Equals("off", StringComparison.OrdinalIgnoreCase)) { ov.Enabled = false; continue; }
                    int e2 = p.IndexOf('=');
                    if (e2 <= 0 || e2 >= p.Length - 1) continue;
                    string k = p.Substring(0, e2).Trim();
                    string v = p.Substring(e2 + 1).Trim();
                    AssignAccField(ov, k, v);
                }
            }
        }

        private static void AssignAccField(AccessoryOverride ov, string key, string val)
        {
            switch (key)
            {
                case "offX":
                    if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ox)) ov.OffX = ox;
                    break;
                case "offY":
                    if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int oy)) ov.OffY = oy;
                    break;
                case "rot":
                    if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float rt)) ov.Rotation = rt;
                    break;
                case "z":
                    if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int z)) ov.ZOrder = z;
                    break;
            }
        }

        private string SerializeAccessoryOverrides()
        {
            var sb = new StringBuilder();
            bool first = true;
            foreach (var skinKv in _accOverrides)
            {
                foreach (var accKv in skinKv.Value)
                {
                    var ov = accKv.Value;
                    if (ov == null) continue;
                    var parts = new List<string>();
                    if (ov.Enabled.HasValue) parts.Add(ov.Enabled.Value ? "on" : "off");
                    if (ov.OffX.HasValue) parts.Add("offX=" + ov.OffX.Value.ToString(CultureInfo.InvariantCulture));
                    if (ov.OffY.HasValue) parts.Add("offY=" + ov.OffY.Value.ToString(CultureInfo.InvariantCulture));
                    if (ov.Rotation.HasValue) parts.Add("rot=" + ov.Rotation.Value.ToString("0.###", CultureInfo.InvariantCulture));
                    if (ov.ZOrder.HasValue) parts.Add("z=" + ov.ZOrder.Value.ToString(CultureInfo.InvariantCulture));
                    if (parts.Count == 0) continue;
                    if (!first) sb.Append(';');
                    first = false;
                    sb.Append(EscapeKey(skinKv.Key)).Append('|').Append(EscapeKey(accKv.Key)).Append('=');
                    sb.Append(string.Join(",", parts.ToArray()));
                }
            }
            return sb.ToString();
        }

        internal TailDeformOverride GetTailOverride(string skinId)
        {
            if (string.IsNullOrEmpty(skinId)) return null;
            return _tailOverrides.TryGetValue(skinId, out var ov) ? ov : null;
        }

        /// <summary>写入或清除（传 null）指定皮肤的尾巴形变覆盖并落盘。</summary>
        internal void SetTailOverride(string skinId, TailDeformOverride ov)
        {
            if (string.IsNullOrEmpty(skinId)) return;
            if (ov == null) _tailOverrides.Remove(skinId);
            else _tailOverrides[skinId] = ov;
            _tailDeformOverrides.Value = SerializeTailOverrides();
        }

        private void ParseTailOverrides(string raw)
        {
            _tailOverrides.Clear();
            if (string.IsNullOrEmpty(raw)) return;
            foreach (string seg in raw.Split(';'))
            {
                if (string.IsNullOrWhiteSpace(seg)) continue;
                int eq = seg.IndexOf('=');
                if (eq <= 0) continue;
                string left = seg.Substring(0, eq);
                string right = seg.Substring(eq + 1).Trim();
                int bar = left.IndexOf('|');
                string rawSkin = bar > 0 ? left.Substring(0, bar) : left;
                string skin = UnescapeKey(rawSkin);
                if (string.IsNullOrEmpty(skin)) continue;

                var ov = new TailDeformOverride();
                foreach (string kv in right.Split(','))
                {
                    string p = kv.Trim();
                    if (p.Length == 0) continue;
                    int e2 = p.IndexOf('=');
                    if (e2 <= 0 || e2 >= p.Length - 1) continue;
                    string k = p.Substring(0, e2).Trim();
                    string v = p.Substring(e2 + 1).Trim();
                    AssignTailField(ov, k, v);
                }
                _tailOverrides[skin] = ov;
            }
        }

        private static void AssignTailField(TailDeformOverride ov, string key, string val)
        {
            switch (key)
            {
                case "FrontGuard": if (bool.TryParse(val, out bool fg)) ov.FrontGuard = fg; break;
                case "Segments": if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sg)) ov.Segments = sg; break;
                case "ConstraintIters": if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ci)) ov.ConstraintIters = ci; break;
                case "Damping": if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float dp)) ov.Damping = dp; break;
                case "SpeedDamping": if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float sdp)) ov.SpeedDamping = sdp; break;
                case "Stiffness": if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float st)) ov.Stiffness = st; break;
                case "MaxBendDeg": if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float mb)) ov.MaxBendDeg = mb; break;
                case "AnchorFollow": if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float af)) ov.AnchorFollow = af; break;
                case "Smoothness": if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float sm)) ov.Smoothness = sm; break;
                case "MaxStep": if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float ms)) ov.MaxStep = ms; break;
                case "MaxFixedDt": if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float mf)) ov.MaxFixedDt = mf; break;
                case "FrontGuardMargin": if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float fm)) ov.FrontGuardMargin = fm; break;
                case "GravityX": if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float gx)) ov.GravityX = gx; break;
                case "GravityY": if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float gy)) ov.GravityY = gy; break;
                case "WindFreq": if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float wf)) ov.WindFreq = wf; break;
                case "WindAmp": if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float wa)) ov.WindAmp = wa; break;
                case "SpeedDisturb": if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float sd)) ov.SpeedDisturb = sd; break;
            }
        }

        private string SerializeTailOverrides()
        {
            var sb = new StringBuilder();
            bool first = true;
            foreach (var kv in _tailOverrides)
            {
                var ov = kv.Value;
                if (ov == null) continue;
                var parts = new List<string>();
                if (ov.FrontGuard.HasValue) parts.Add("FrontGuard=" + ov.FrontGuard.Value);
                AddIntPart(parts, "Segments", ov.Segments);
                AddIntPart(parts, "ConstraintIters", ov.ConstraintIters);
                AddFloatPart(parts, "Damping", ov.Damping);
                AddFloatPart(parts, "SpeedDamping", ov.SpeedDamping);
                AddFloatPart(parts, "Stiffness", ov.Stiffness);
                AddFloatPart(parts, "MaxBendDeg", ov.MaxBendDeg);
                AddFloatPart(parts, "AnchorFollow", ov.AnchorFollow);
                AddFloatPart(parts, "Smoothness", ov.Smoothness);
                AddFloatPart(parts, "MaxStep", ov.MaxStep);
                AddFloatPart(parts, "MaxFixedDt", ov.MaxFixedDt);
                AddFloatPart(parts, "FrontGuardMargin", ov.FrontGuardMargin);
                AddFloatPart(parts, "GravityX", ov.GravityX);
                AddFloatPart(parts, "GravityY", ov.GravityY);
                AddFloatPart(parts, "WindFreq", ov.WindFreq);
                AddFloatPart(parts, "WindAmp", ov.WindAmp);
                AddFloatPart(parts, "SpeedDisturb", ov.SpeedDisturb);
                if (parts.Count == 0) continue;
                if (!first) sb.Append(';');
                first = false;
                sb.Append(EscapeKey(kv.Key)).Append('|').Append('=');
                sb.Append(string.Join(",", parts.ToArray()));
            }
            return sb.ToString();
        }

        private static string EscapeKey(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                bool edgeSpace = (i == 0 || i == s.Length - 1) && char.IsWhiteSpace(c);
                if (c == '%' || c == ';' || c == '=' || c == '|' || c == ',' || edgeSpace)
                {
                    foreach (byte b in Encoding.UTF8.GetBytes(new[] { c }))
                        sb.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static string UnescapeKey(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            string trimmed = raw.Trim();
            if (trimmed.IndexOf('%') < 0) return trimmed;
            try { return Uri.UnescapeDataString(trimmed); }
            catch { return trimmed; }
        }

        private static void AddIntPart(List<string> parts, string key, int? v)
        {
            if (v.HasValue) parts.Add(key + "=" + v.Value.ToString(CultureInfo.InvariantCulture));
        }

        private static void AddFloatPart(List<string> parts, string key, float? v)
        {
            if (v.HasValue) parts.Add(key + "=" + v.Value.ToString("0.######", CultureInfo.InvariantCulture));
        }
    }
}
