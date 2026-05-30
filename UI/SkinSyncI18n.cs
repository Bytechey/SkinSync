using System.Collections.Generic;
using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>
    /// 极简 i18n：zh-CN / EN 两套字典，按 Locale.currentLangName 自动选择。
    /// 与 SaveManager 风格一致；独立命名空间避免与 SaveManager.I18n 冲突。
    /// </summary>
    internal static class SkinSyncI18n
    {
        private static readonly Dictionary<string, string> _zh = new Dictionary<string, string>
        {
            ["app.name"] = "皮肤同步",
            ["app.menu_button"] = "皮肤同步",
            ["tab.settings"] = "设置",
            ["tab.skins"] = "角色",
            ["tab.current"] = "当前角色设置",
            ["tab.about"] = "关于",
            ["status.current_skin"] = "当前皮肤：{0}",
            ["status.no_skin"] = "尚未选择皮肤",

            ["sec.hotkeys"] = "快捷键",
            ["sec.logging"] = "日志",
            ["sw.show_log_in_console"] = "在游戏控制台显示模组日志",
            ["sec.visual"] = "视觉",
            ["sw.hide_game_wearables"] = "隐藏游戏装备 sprite（保留装甲 / 保暖等逻辑）",
            ["sw.require_equipment"] = "配件配了 requireWornSlot 时，仅穿戴对应装备 slot 才显示",

            ["sec.sync"] = "多人同步",
            ["sync.no_krokmp_hint"] = "未找到 KrokMP 多人联机模组，多人同步选项不可用",
            ["sync.network_off_hint"] = "尚未进入多人会话，以下设置将在主机 / 客户端连接后生效",
            ["lbl.sync_mode"] = "同步策略：",
            ["sync.mode_on_enter"] = "进入游戏一键同步",
            ["sync.mode_passive"] = "仅被动响应改动",
            ["sw.sync_accessories"] = "同步配件配置（toggle / 偏移 / Z）",
            ["sw.sync_tail"] = "同步尾巴形变参数（全局生效）",
            ["lbl.hotkey_toggle_panel"] = "打开 / 关闭面板：",
            ["lbl.hotkey_next_skin"] = "切下一个皮肤：",
            ["lbl.hotkey_prev_skin"] = "切上一个皮肤：",
            ["lbl.hotkey_rescan"] = "重新扫描皮肤：",
            ["btn.unbound"] = "未绑定",
            ["btn.press_a_key"] = "请按下新按键…",
            ["btn.clear"] = "清除",

            ["sec.skins"] = "可用皮肤",
            ["btn.skin_in_use"] = "正在使用",
            ["btn.rescan"] = "重新扫描",
            ["fmt.skins_count"] = "共 {0} 个皮肤",
            ["msg.no_skins"] = "  在 plugins/CustomSprites/ 下未找到 stN 皮肤目录。",
            ["btn.apply_skin"] = "切换到此皮肤",
            ["lbl.skin_active"] = "【当前】",

            ["sec.accessories"] = "配件",
            ["msg.no_accessories"] = "  当前皮肤没有配件配置（accessories.json 缺失或为空）。",
            ["msg.no_skin_yet"] = "  尚未选择皮肤。请先在角色 tab 切换到一个皮肤。",
            ["sw.on"] = "◀ 开",
            ["sw.off"] = "关 ▶",

            ["sec.tail"] = "尾巴形变",
            ["lbl.tail_on"] = "[已启用]",
            ["lbl.tail_off"] = "[未启用]",
            ["tail.enabled"] = "启用形变（关闭显示原 sprite）",
            ["tail.front_guard"] = "前侧约束（防止飘到正面）",
            ["tail.segments"] = "分段数",
            ["tail.iters"] = "约束迭代次数",
            ["tail.damping"] = "阻尼",
            ["tail.speed_damping"] = "速度阻尼（移动越快阻尼越大）",
            ["tail.stiffness"] = "距离约束强度",
            ["tail.max_bend"] = "最大弯角（度）",
            ["tail.anchor_follow"] = "首节软跟随",
            ["tail.smoothness"] = "平滑度",
            ["tail.max_step"] = "单步最大位移",
            ["tail.max_fixed_dt"] = "子步上限",
            ["tail.front_guard_margin"] = "前侧裕度",
            ["tail.gravity_x"] = "重力 X",
            ["tail.gravity_y"] = "重力 Y",
            ["tail.wind_freq"] = "风频率",
            ["tail.wind_amp"] = "风幅度",
            ["tail.speed_disturb"] = "速度扰动",
            ["tail.reset"] = "重置默认",

            ["acc.enabled"] = "启用配件",
            ["acc.off_x"] = "X 偏移（像素）",
            ["acc.off_y"] = "Y 偏移（像素）",
            ["acc.rotation"] = "旋转（度）",
            ["acc.z_order"] = "Z 排序",
            ["acc.require_slot"] = "依赖装备：{0}",

            ["slot.arms"] = "臂套",
            ["slot.back"] = "背部",
            ["slot.balaclava"] = "头套",
            ["slot.bandolier"] = "弹链",
            ["slot.belt"] = "腰带",
            ["slot.blindfold"] = "眼罩",
            ["slot.eyes"] = "眼镜",
            ["slot.feet"] = "鞋子",
            ["slot.hands"] = "手套",
            ["slot.hat"] = "帽子",
            ["slot.knees"] = "护膝",
            ["slot.mouth"] = "口罩",
            ["slot.neck"] = "项圈",
            ["slot.outertorso"] = "外衣",
            ["slot.thigh"] = "护腿（前）",
            ["slot.thighback"] = "护腿（后）",
            ["slot.torso"] = "上衣",
            ["slot.torsofront"] = "胸甲",
            ["slot.wraps"] = "绷带",

            ["about.title"] = "皮肤同步",
            ["about.desc"] = "为《Casualties: Unknown》提供自定义皮肤、配件、血液与多人同步。",
            ["about.version"] = "版本：{0}",
            ["about.sec_links"] = "链接",
            ["about.link_mod_repo"] = "皮肤 Mod 仓库",
            ["about.link_mod_video"] = "皮肤 Mod 演示视频",
            ["about.sec_credits"] = "开发人员",
            ["about.testers"] = "测试人员",
            ["about.sec_deps"] = "依赖与致谢",
        };

        private static readonly Dictionary<string, string> _en = new Dictionary<string, string>
        {
            ["app.name"] = "Skin Sync",
            ["app.menu_button"] = "Skin Sync",
            ["tab.settings"] = "Settings",
            ["tab.skins"] = "Skins",
            ["tab.current"] = "Current Skin",
            ["tab.about"] = "About",
            ["status.current_skin"] = "Current skin: {0}",
            ["status.no_skin"] = "No skin selected",

            ["sec.hotkeys"] = "Hotkeys",
            ["sec.logging"] = "Logging",
            ["sw.show_log_in_console"] = "Show mod logs in game console",
            ["sec.visual"] = "Visual",
            ["sw.hide_game_wearables"] = "Hide game wearable sprites (keep armor / isolation logic)",
            ["sw.require_equipment"] = "Show requireWornSlot accessories only when wearing the slot equipment",

            ["sec.sync"] = "Multiplayer sync",
            ["sync.no_krokmp_hint"] = "KrokMP multiplayer mod not found — multiplayer sync options are disabled",
            ["sync.network_off_hint"] = "Not in a multiplayer session — settings below will apply once host / client is connected",
            ["lbl.sync_mode"] = "Sync strategy:",
            ["sync.mode_on_enter"] = "On enter (auto sync)",
            ["sync.mode_passive"] = "Passive (on change)",
            ["sw.sync_accessories"] = "Sync accessory overrides (toggle / offsets / Z)",
            ["sw.sync_tail"] = "Sync tail deform (global parameters)",
            ["lbl.hotkey_toggle_panel"] = "Open / close panel:",
            ["lbl.hotkey_next_skin"] = "Next skin:",
            ["lbl.hotkey_prev_skin"] = "Previous skin:",
            ["lbl.hotkey_rescan"] = "Rescan skins:",
            ["btn.unbound"] = "Unbound",
            ["btn.press_a_key"] = "Press a new key...",
            ["btn.clear"] = "Clear",

            ["sec.skins"] = "Available skins",
            ["btn.skin_in_use"] = "In use",
            ["btn.rescan"] = "Rescan",
            ["fmt.skins_count"] = "{0} skins",
            ["msg.no_skins"] = "  No stN skin folders found under plugins/CustomSprites/.",
            ["btn.apply_skin"] = "Apply this skin",
            ["lbl.skin_active"] = "[Active]",

            ["sec.accessories"] = "Accessories",
            ["msg.no_accessories"] = "  No accessories.json for current skin (or empty).",
            ["msg.no_skin_yet"] = "  No skin selected yet. Pick one in the Skins tab.",
            ["sw.on"] = "◀ ON",
            ["sw.off"] = "OFF ▶",

            ["sec.tail"] = "Tail Deform",
            ["lbl.tail_on"] = "[Enabled]",
            ["lbl.tail_off"] = "[Disabled]",
            ["tail.enabled"] = "Enable deform (off = show plain sprite)",
            ["tail.front_guard"] = "Front guard (prevent flipping to front)",
            ["tail.segments"] = "Segments",
            ["tail.iters"] = "Constraint iterations",
            ["tail.damping"] = "Damping",
            ["tail.speed_damping"] = "Speed damping (higher player speed → more damping)",
            ["tail.stiffness"] = "Stiffness",
            ["tail.max_bend"] = "Max bend (deg)",
            ["tail.anchor_follow"] = "Anchor follow (1st joint)",
            ["tail.smoothness"] = "Smoothness",
            ["tail.max_step"] = "Max step",
            ["tail.max_fixed_dt"] = "Sub-step limit",
            ["tail.front_guard_margin"] = "Front guard margin",
            ["tail.gravity_x"] = "Gravity X",
            ["tail.gravity_y"] = "Gravity Y",
            ["tail.wind_freq"] = "Wind freq",
            ["tail.wind_amp"] = "Wind amp",
            ["tail.speed_disturb"] = "Speed disturb",
            ["tail.reset"] = "Reset to defaults",

            ["acc.enabled"] = "Enable accessory",
            ["acc.off_x"] = "Offset X (px)",
            ["acc.off_y"] = "Offset Y (px)",
            ["acc.rotation"] = "Rotation (deg)",
            ["acc.z_order"] = "Z order",
            ["acc.require_slot"] = "Requires equipment: {0}",

            ["slot.arms"] = "Arms",
            ["slot.back"] = "Back",
            ["slot.balaclava"] = "Balaclava",
            ["slot.bandolier"] = "Bandolier",
            ["slot.belt"] = "Belt",
            ["slot.blindfold"] = "Blindfold",
            ["slot.eyes"] = "Eyewear",
            ["slot.feet"] = "Feet",
            ["slot.hands"] = "Hands",
            ["slot.hat"] = "Hat",
            ["slot.knees"] = "Knees",
            ["slot.mouth"] = "Mouth",
            ["slot.neck"] = "Neck",
            ["slot.outertorso"] = "Outer torso",
            ["slot.thigh"] = "Thigh (front)",
            ["slot.thighback"] = "Thigh (back)",
            ["slot.torso"] = "Torso",
            ["slot.torsofront"] = "Torso front",
            ["slot.wraps"] = "Wraps",

            ["about.title"] = "Skin Sync",
            ["about.desc"] = "Custom skins, accessories, blood and multiplayer sync for Casualties: Unknown.",
            ["about.version"] = "Version: {0}",
            ["about.sec_links"] = "Links",
            ["about.link_mod_repo"] = "Skin Mod repository",
            ["about.link_mod_video"] = "Skin Mod demo video",
            ["about.sec_credits"] = "Developers",
            ["about.testers"] = "Playtesters",
            ["about.sec_deps"] = "Dependencies & thanks",
        };

        private static Dictionary<string, string> Current
        {
            get
            {
                string name = null;
                try { name = Locale.currentLangName; } catch { }
                if (string.IsNullOrEmpty(name))
                {
                    try { name = PlayerPrefs.GetString("locale"); } catch { }
                }
                if (!string.IsNullOrEmpty(name)
                    && (name.StartsWith("zh", System.StringComparison.OrdinalIgnoreCase)
                        || name.StartsWith("WC", System.StringComparison.OrdinalIgnoreCase)))
                {
                    return _zh;
                }
                return _en;
            }
        }

        internal static string T(string key)
        {
            if (Current.TryGetValue(key, out var v)) return v;
            return key;
        }

        internal static string F(string key, params object[] args)
        {
            string fmt = T(key);
            try { return string.Format(fmt, args); }
            catch { return fmt; }
        }

        /// <summary>装备 slot id 的本地化展示名。未知 slot 原样返回。</summary>
        internal static string LocalizeWearSlot(string slot)
        {
            if (string.IsNullOrEmpty(slot)) return "";
            return T("slot." + slot);
        }
    }
}
