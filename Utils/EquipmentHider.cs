using System.Collections.Generic;
using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>定时隐藏或恢复角色身上已穿戴装备的 SpriteRenderer，不影响装备数值与逻辑。</summary>
    [DisallowMultipleComponent]
    public class EquipmentHider : MonoBehaviour
    {
        public static bool Active;

        private const float TickIntervalSec = 0.25f;
        private float _nextTick;

        private readonly HashSet<SpriteRenderer> _hidden = new HashSet<SpriteRenderer>();
        private readonly List<SpriteRenderer> _toRemove = new List<SpriteRenderer>();

        private void Update()
        {
            if (Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + TickIntervalSec;

            if (Active) ApplyHide();
            else RestoreAll();
        }

        private void ApplyHide()
        {
            PruneDetachedRenderers();

            var bodies = FindObjectsOfType<Body>();
            foreach (var body in bodies)
            {
                if (body == null || body.limbs == null) continue;
                for (int i = 0; i < body.limbs.Length; i++)
                {
                    var limb = body.limbs[i];
                    if (limb == null) continue;
                    var limbTf = limb.transform;
                    for (int c = 0; c < limbTf.childCount; c++)
                    {
                        var child = limbTf.GetChild(c);
                        if (child == null) continue;
                        var sr = child.GetComponent<SpriteRenderer>();
                        if (sr == null || !sr.enabled) continue;

                        var item = child.GetComponent<Item>();
                        bool isWearable = item != null && item.Stats != null && item.Stats.wearable;
                        bool isExt = child.GetComponent<WearableExtension>() != null;
                        if (!isWearable && !isExt) continue;

                        sr.enabled = false;
                        _hidden.Add(sr);
                    }
                }
            }
        }

        private void PruneDetachedRenderers()
        {
            if (_hidden.Count == 0) return;
            _toRemove.Clear();
            foreach (var sr in _hidden)
            {
                if (sr == null)
                {
                    _toRemove.Add(sr);
                    continue;
                }
                Transform parent = sr.transform.parent;
                if (parent == null)
                {
                    sr.enabled = true;
                    _toRemove.Add(sr);
                }
                else if (parent.GetComponent<Limb>() == null)
                {
                    // 移入背包等 Container 节点的物品由容器自身控制隐藏，仅解除本组件跟踪而不篡改 sr.enabled。
                    _toRemove.Add(sr);
                }
            }
            for (int i = 0; i < _toRemove.Count; i++)
            {
                _hidden.Remove(_toRemove[i]);
            }
            _toRemove.Clear();
        }

        private void RestoreAll()
        {
            if (_hidden.Count == 0) return;
            foreach (var sr in _hidden)
            {
                if (sr == null) continue;
                Transform parent = sr.transform.parent;
                if (parent == null || parent.GetComponent<Limb>() != null)
                {
                    sr.enabled = true;
                }
            }
            _hidden.Clear();
        }

        private void OnDisable()
        {
            RestoreAll();
        }
    }
}
