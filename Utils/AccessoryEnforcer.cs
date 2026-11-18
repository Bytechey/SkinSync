using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>定时根据角色装备穿戴槽位状态更新带有装备依赖的自定义配件显隐。</summary>
    [DisallowMultipleComponent]
    public class AccessoryEnforcer : MonoBehaviour
    {
        public static bool Active = true;

        private const float TickIntervalSec = 0.25f;
        private float _nextTick;

        private void Update()
        {
            if (Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + TickIntervalSec;
            AccessoryAttacher.EnforceWearableSlots(Active);
        }
    }
}
