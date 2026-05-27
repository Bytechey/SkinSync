using SkinSyncMod.Network;
using System.Collections.Generic;
using UnityEngine;

namespace SkinSyncMod.Patches
{
    /// <summary>子端在 KrokMP worldgen 阶段可能先收到 SkinChange 但 NetBody 还没注册；本组件每帧重试待应用条目，注册成功后清掉。</summary>
    [DisallowMultipleComponent]
    public class PendingSkinApplier : MonoBehaviour
    {
        private static readonly Dictionary<uint, string> _pending = new Dictionary<uint, string>();
        private const float TickIntervalSec = 0.25f;
        private const float MaxWaitSec = 30f;
        private float _nextTick;
        private static readonly Dictionary<uint, float> _enqueuedAt = new Dictionary<uint, float>();

        public static void Enqueue(uint netId, string skinId)
        {
            if (string.IsNullOrEmpty(skinId)) return;
            _pending[netId] = skinId;
            _enqueuedAt[netId] = Time.unscaledTime;
        }

        private void Update()
        {
            if (_pending.Count == 0) return;
            if (Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + TickIntervalSec;

            List<uint> done = null;
            List<uint> expired = null;
            foreach (var kv in _pending)
            {
                if (KrokoshaBridge.TryGetNetBodyFromId(kv.Key, out _, out GameObject chara, out _) && chara != null)
                {
                    SkinApplier.ApplySkinToPlayer(chara, kv.Value);
                    SkinSyncMod.SkinSync.LogBoth($"[SkinSync] 延迟应用：netId {kv.Key} → {kv.Value} 成功");
                    (done ?? (done = new List<uint>())).Add(kv.Key);
                }
                else if (_enqueuedAt.TryGetValue(kv.Key, out float at) && Time.unscaledTime - at > MaxWaitSec)
                {
                    SkinSyncMod.SkinSync.LogBoth($"[SkinSync] 延迟应用：netId {kv.Key} → {kv.Value} 超过 {MaxWaitSec}s 未注册，放弃");
                    (expired ?? (expired = new List<uint>())).Add(kv.Key);
                }
            }
            if (done != null) foreach (var k in done) { _pending.Remove(k); _enqueuedAt.Remove(k); }
            if (expired != null) foreach (var k in expired) { _pending.Remove(k); _enqueuedAt.Remove(k); }
        }
    }
}
