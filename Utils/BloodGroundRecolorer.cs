using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>
    /// 给场景中刚生成的 GroundBlood / wallblood / blockblood / wallvomit / blockvomit 实例按本地玩家
    /// 当前皮肤的 blood.json 染色。地面 / 墙面血迹由 BleedParticle.Update 在粒子末帧实例化预制 prefab，
    /// sprite 本身就是血迹图——通过覆盖 SpriteRenderer.color 保留 alpha 抖动同时染上自定义色。
    /// 全局单例 watcher（挂在 Plugin GameObject 上），与 chara 解耦——地面血迹是 world-级别。
    /// </summary>
    [DisallowMultipleComponent]
    public class BloodGroundRecolorer : MonoBehaviour
    {
        private const float TickIntervalSec = 0.25f;
        private float _nextTick;
        // 缓存本地皮肤名 + 解析后的 BloodConfig，避免每次都读 JSON。
        private string _cachedSkin;
        private Color _cachedDark = Color.white;
        private Color _cachedLight = Color.white;
        private bool _cachedAvailable;
        // GroundBlood 是组件，wallblood 等只是 GO + SpriteRenderer——用 instanceID 防重复处理。
        private readonly HashSet<int> _processed = new HashSet<int>();

        private void Update()
        {
            if (Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + TickIntervalSec;

            string skin = SkinSync.Settings != null ? SkinSync.Settings.CurrentSkin.Value : null;
            if (string.IsNullOrEmpty(skin)) return;
            if (skin != _cachedSkin) RefreshConfigCache(skin);
            if (!_cachedAvailable) return;

            // GroundBlood 组件——FindObjectsOfType 返回当前激活组件。120s 后游戏自销毁。
            foreach (var gb in FindObjectsOfType<GroundBlood>())
            {
                if (gb == null || gb.gameObject == null) continue;
                int id = gb.gameObject.GetInstanceID();
                if (_processed.Contains(id)) continue;
                ApplyColor(gb.gameObject);
                _processed.Add(id);
            }

            // wallblood / blockblood / wallvomit / blockvomit 没有专用组件，用 SpriteRenderer.sprite.name 识别。
            // 注意 FindObjectsOfType<SpriteRenderer> 量很大（场景内所有 sprite），按 sprite name 前缀过滤即可。
            foreach (var sr in FindObjectsOfType<SpriteRenderer>())
            {
                if (sr == null || sr.sprite == null) continue;
                int id = sr.gameObject.GetInstanceID();
                if (_processed.Contains(id)) continue;
                string n = sr.sprite.name;
                if (string.IsNullOrEmpty(n)) continue;
                if (!IsBloodSpriteName(n)) continue;
                ApplyColor(sr.gameObject, sr);
                _processed.Add(id);
            }
        }

        private static bool IsBloodSpriteName(string n)
        {
            return n.IndexOf("wallblood", System.StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("blockblood", System.StringComparison.OrdinalIgnoreCase) >= 0;
            // wallvomit / blockvomit 是呕吐残留，本系列也是黄色血——按需可加；先只染血色。
        }

        private void RefreshConfigCache(string skin)
        {
            _cachedSkin = skin;
            _cachedAvailable = false;
            string skinDir = Path.Combine(Paths.PluginPath, "CustomSprites", skin);
            var cfg = BloodConfigLoader.Load(skinDir);
            if (cfg == null) return;
            // 优先用 ParticleStartColor 当地面血色；缺则用 BloodLight 兜底；都没有则不染。
            Color32? main = cfg.ParticleStartColor ?? cfg.BloodLight;
            Color32? dark = cfg.ParticleEndColor ?? cfg.BloodDark ?? main;
            if (!main.HasValue) return;
            _cachedLight = (Color)main.Value;
            _cachedDark = dark.HasValue ? (Color)dark.Value : _cachedLight;
            _cachedAvailable = true;
        }

        private void ApplyColor(GameObject go, SpriteRenderer providedSr = null)
        {
            var sr = providedSr ?? go.GetComponent<SpriteRenderer>();
            if (sr == null) return;
            // 保留游戏给的 alpha 抖动（Random.Range(0.2f, 0.8f) 等），仅替换 RGB——sprite 自身灰阶图样保留。
            float a = sr.color.a;
            // 地面血迹偏深、墙面偏浅；这里都用 light 色——简单可预期；用户想区分时可在 blood.json 加 groundColor 字段（待加）。
            sr.color = new Color(_cachedLight.r, _cachedLight.g, _cachedLight.b, a);
            _ = _cachedDark;
        }
    }
}
