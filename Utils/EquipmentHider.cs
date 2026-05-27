using System.Collections.Generic;
using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>
    /// 隐藏所有玩家身上装备的可见 SpriteRenderer，但保留装备 GameObject 与游戏装备逻辑（armor / isolation / GetWearableBySlotID）。
    /// 识别：装备主体 = Limb 子节点中含 Item 组件且 Stats.wearable=true 的物体；副件 sprite = 含 WearableExtension 组件的物体。
    /// 配件 GO（HwAccessories_*）不含这两种组件，不会被误隐藏。
    /// </summary>
    [DisallowMultipleComponent]
    public class EquipmentHider : MonoBehaviour
    {
        /// <summary>设置开关：true=每帧 enforce 隐藏，false=恢复显示。</summary>
        public static bool Active;

        private const float TickIntervalSec = 0.25f;
        private float _nextTick;

        // 记录被本组件强制设过 enabled=false 的 SpriteRenderer，关闭功能时统一恢复 enabled=true。
        private readonly HashSet<SpriteRenderer> _hidden = new HashSet<SpriteRenderer>();

        private void Update()
        {
            if (Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + TickIntervalSec;

            if (Active) ApplyHide();
            else RestoreAll();
        }

        private void ApplyHide()
        {
            var bodies = FindObjectsOfType<Body>();
            foreach (var body in bodies)
            {
                if (body == null) continue;
                foreach (var tf in body.GetComponentsInChildren<Transform>(true))
                {
                    if (tf == null || tf.gameObject == null) continue;
                    var sr = tf.GetComponent<SpriteRenderer>();
                    if (sr == null || !sr.enabled) continue;
                    bool isItem = tf.GetComponent<Item>() != null;
                    bool isExt = tf.GetComponent<WearableExtension>() != null;
                    if (!isItem && !isExt) continue;
                    sr.enabled = false;
                    _hidden.Add(sr);
                }
            }
        }

        private void RestoreAll()
        {
            if (_hidden.Count == 0) return;
            foreach (var sr in _hidden)
            {
                if (sr != null) sr.enabled = true;
            }
            _hidden.Clear();
        }

        private void OnDisable()
        {
            RestoreAll();
        }
    }
}
