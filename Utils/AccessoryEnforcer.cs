using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>
    /// 配件依赖装备的运行时执行器：
    /// 对每个 mounted 配件，按 Meta.RequireSlot 调 Body.GetWearableBySlotID 判断玩家是否穿戴该 slot 装备；
    /// 仅当 (DesiredEnabled && (RequireSlot 为空 || RequireOption 关 || 该 slot 已穿戴)) 时 active=true。
    /// </summary>
    [DisallowMultipleComponent]
    public class AccessoryEnforcer : MonoBehaviour
    {
        /// <summary>全局开关：true=启用 RequireWornSlot 依赖判定，false=忽略依赖永远显示（仍尊重 DesiredEnabled）。</summary>
        public static bool Active = true;

        private const float TickIntervalSec = 0.25f;
        private float _nextTick;

        private void Update()
        {
            if (Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + TickIntervalSec;

            foreach (var kv in AccessoryAttacher.Meta)
            {
                var meta = kv.Value;
                if (meta == null) continue;
                if (!AccessoryAttacher.Mounted.TryGetValue(kv.Key, out var go) || go == null) continue;

                bool want = meta.DesiredEnabled;
                if (want && Active && !string.IsNullOrEmpty(meta.RequireSlot))
                {
                    want = meta.Body != null && meta.Body.GetWearableBySlotID(meta.RequireSlot) != null;
                }
                if (go.activeSelf != want) go.SetActive(want);
            }
        }
    }
}
