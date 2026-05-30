using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BepInEx.Configuration;
using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>
    /// 集中维护持久化配置：改键、当前皮肤、配件覆盖、尾巴形变 per-skin 覆盖。
    /// 写入 BepInEx ConfigFile（plugins/.../config 目录），重启游戏后自动恢复。
    /// </summary>
    internal sealed class SkinSyncSettings
    {
        private static readonly KeyboardShortcut Unbound = new KeyboardShortcut(KeyCode.None);

        internal ConfigEntry<KeyboardShortcut> TogglePanelHotkey { get; }
        internal ConfigEntry<KeyboardShortcut> NextSkinHotkey { get; }
        internal ConfigEntry<KeyboardShortcut> PrevSkinHotkey { get; }
        internal ConfigEntry<KeyboardShortcut> RescanSkinsHotkey { get; }

        internal ConfigEntry<string> CurrentSkin { get; }

        internal ConfigEntry<bool> HideGameWearables { get; }
        internal ConfigEntry<bool> RequireEquipmentForAccessories { get; }

        // —— 杂项 —— //
        internal ConfigEntry<bool> ShowLogInConsole { get; }
        internal ConfigEntry<bool> AcceptUpdateNotice { get; }

        // —— 多人同步 —— //
        /// <summary>"OnEnter"=自己进游戏一键同步全部；"Passive"=仅响应改动事件不主动批量同步。</summary>
        internal ConfigEntry<string> SyncMode { get; }
        internal ConfigEntry<bool> SyncAccessories { get; }
        internal ConfigEntry<bool> SyncTailDeform { get; }

        /// <summary>配件覆盖（按 skin + accId）。enabled 必有；offX/offY/rot/z 缺省时表示沿用 accessories.json 默认值。</summary>
        internal sealed class AccessoryOverride
        {
            public bool? Enabled;
            public int? OffX;
            public int? OffY;
            public float? Rotation;
            public int? ZOrder;
        }

        /// <summary>尾巴形变 per-skin 覆盖。任意字段为 null 时用 TailDeformDefaults 默认。</summary>
        internal sealed class TailDeformOverride
        {
            public bool? Enabled;
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

        private readonly ConfigEntry<string> _accessoryOverrides;
        private readonly ConfigEntry<string> _tailDeformOverrides;

        // skin -> accId -> AccessoryOverride
        private readonly Dictionary<string, Dictionary<string, AccessoryOverride>> _accOverrides
            = new Dictionary<string, Dictionary<string, AccessoryOverride>>();

        // skin -> TailDeformOverride
        private readonly Dictionary<string, TailDeformOverride> _tailOverrides
            = new Dictionary<string, TailDeformOverride>();

        internal SkinSyncSettings(ConfigFile config)
        {
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

            RequireEquipmentForAccessories = config.Bind("Visual", "RequireEquipmentForAccessories", true,
                "配件 accessories.json 中配了 requireWornSlot 时，玩家未穿戴对应装备 slot 则隐藏该配件。关闭此开关让配件无视依赖永远显示。");

            ShowLogInConsole = config.Bind("Misc", "ShowInConsole", false,
                "是否把模组日志同步打印到游戏内控制台（` 键打开）。关闭时仅写入 BepInEx 日志。");
            AcceptUpdateNotice = config.Bind("Misc", "AcceptUpdateNotice", true,
                "是否在启动时检查 GitHub 新版本并在游戏内提示。关闭则不检测不提示。");

            SyncMode = config.Bind("Sync", "Mode", "OnEnter",
                "多人同步策略。OnEnter=自己进入游戏时一键同步皮肤+配件+尾巴；Passive=仅在改动配件/尾巴时被动广播。");
            SyncAccessories = config.Bind("Sync", "Accessories", true,
                "本机改动配件覆盖（toggle / 偏移 / 旋转 / Z 排序）时广播给所有玩家。");
            SyncTailDeform = config.Bind("Sync", "TailDeform", true,
                "本机改动尾巴形变参数时广播给所有玩家（尾巴形变是全局参数，会覆盖到所有客户端）。");

            _accessoryOverrides = config.Bind("Session", "AccessoryOverrides", "",
                "配件覆盖。格式：每条 \"skin|accId=k1=v1,k2=v2,...\" 用分号分隔；可用 key：on/off, offX, offY, rot, z。");
            ParseAccessoryOverrides(_accessoryOverrides.Value);

            _tailDeformOverrides = config.Bind("Session", "TailDeformOverrides", "",
                "尾巴形变 per-skin 覆盖。格式：每条 \"skin|field=value,field=value\" 用分号分隔。缺字段沿用代码默认。");
            ParseTailOverrides(_tailDeformOverrides.Value);
        }

        internal static bool IsBound(KeyboardShortcut sc) => sc.MainKey != KeyCode.None;

        internal static bool TriggeredThisFrame(ConfigEntry<KeyboardShortcut> entry)
        {
            var sc = entry.Value;
            if (!IsBound(sc)) return false;
            return sc.IsDown();
        }

        // —— 配件覆盖 —— //

        /// <summary>查询配件覆盖；返回 null 表示完全没记录，按 entry 默认走。</summary>
        internal AccessoryOverride GetAccessoryOverride(string skinId, string accId)
        {
            if (string.IsNullOrEmpty(skinId) || string.IsNullOrEmpty(accId)) return null;
            if (_accOverrides.TryGetValue(skinId, out var inner)
                && inner.TryGetValue(accId, out var ov)) return ov;
            return null;
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
                string s = seg.Trim();
                if (s.Length == 0) continue;
                int eq = s.IndexOf('=');
                if (eq <= 0) continue;
                string left = s.Substring(0, eq);
                string right = s.Substring(eq + 1).Trim();
                int bar = left.IndexOf('|');
                if (bar <= 0 || bar >= left.Length - 1) continue;
                string skin = left.Substring(0, bar).Trim();
                string acc = left.Substring(bar + 1).Trim();
                var ov = EnsureAcc(skin, acc);
                // 兼容旧格式：右侧只是 on/off
                if (right.Equals("on", System.StringComparison.OrdinalIgnoreCase)) { ov.Enabled = true; continue; }
                if (right.Equals("off", System.StringComparison.OrdinalIgnoreCase)) { ov.Enabled = false; continue; }
                // 新格式：逗号分隔 key=value 列表
                foreach (string kv in right.Split(','))
                {
                    string p = kv.Trim();
                    if (p.Length == 0) continue;
                    if (p.Equals("on", System.StringComparison.OrdinalIgnoreCase)) { ov.Enabled = true; continue; }
                    if (p.Equals("off", System.StringComparison.OrdinalIgnoreCase)) { ov.Enabled = false; continue; }
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
                    sb.Append(skinKv.Key).Append('|').Append(accKv.Key).Append('=');
                    sb.Append(string.Join(",", parts.ToArray()));
                }
            }
            return sb.ToString();
        }

        // —— 尾巴覆盖 —— //

        /// <summary>查询尾巴覆盖；返回 null 表示沿用代码默认。</summary>
        internal TailDeformOverride GetTailOverride(string skinId)
        {
            if (string.IsNullOrEmpty(skinId)) return null;
            return _tailOverrides.TryGetValue(skinId, out var ov) ? ov : null;
        }

        /// <summary>覆盖整体写入并落盘（用于"重置默认"也走这个清空路径）。</summary>
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
                string s = seg.Trim();
                if (s.Length == 0) continue;
                int eq = s.IndexOf('=');
                if (eq <= 0) continue;
                string left = s.Substring(0, eq);
                string right = s.Substring(eq + 1).Trim();
                int bar = left.IndexOf('|');
                string skin = bar > 0 ? left.Substring(0, bar).Trim() : left.Trim();
                if (string.IsNullOrEmpty(skin)) continue;
                var ov = new TailDeformOverride();
                // right 已经是逗号分隔的 k=v 列表（"|" 后部分可能为空，向上取 left 的"="后内容）
                // 实际"skin|=k=v,..."拼接时第一个 = 把 left 与 value 列表分隔，所以 right 直接用即可
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
                case "Enabled": if (bool.TryParse(val, out bool en)) ov.Enabled = en; break;
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
                if (ov.Enabled.HasValue) parts.Add("Enabled=" + ov.Enabled.Value);
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
                sb.Append(kv.Key).Append('|').Append('=');
                sb.Append(string.Join(",", parts.ToArray()));
            }
            return sb.ToString();
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
