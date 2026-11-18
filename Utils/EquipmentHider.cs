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
