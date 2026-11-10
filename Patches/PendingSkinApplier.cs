using SkinSyncMod.Interop.Consumers;
using SkinSyncMod.Network;
using System.Collections.Generic;
using UnityEngine;

namespace SkinSyncMod.Patches
{
    /// <summary>子端可能先收到 SkinChange，此时对应玩家的 NetBody 还没注册；本组件每 0.25 秒重试待应用条目，对应 Together.NetBody.TryGetNetBodyFromId 成功即应用并清掉。</summary>
    [DisallowMultipleComponent]
    public class PendingSkinApplier : MonoBehaviour
    {
        private static readonly Dictionary<uint, string> _pending = new Dictionary<uint, string>();
        private const float TickIntervalSec = 0.25f;
        private const float MaxWaitSec = 30f;
        private float _nextTick;
        private static readonly Dictionary<uint, float> _enqueuedAt = new Dictionary<uint, float>();

        public static void Enqueue(uint netId, string skinId, string reason = null)
        {
            if (string.IsNullOrEmpty(skinId)) return;
            _pending[netId] = skinId;
            _enqueuedAt[netId] = Time.unscaledTime;
            SkinSyncMod.ModLog.Debug($"延迟应用入队：netId {netId} → {skinId}，队列 {_pending.Count} 条，等待原因：{(string.IsNullOrEmpty(reason) ? "NetBody 未注册" : reason)}");
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
                if (RosterConsumer.TryGetNetBodyFromId(kv.Key, out _, out GameObject chara, out _) && chara != null)
                {
                    SkinApplier.ApplySkinToPlayer(chara, kv.Value);
                    SkinSyncMod.ModLog.Info($"延迟应用：netId {kv.Key} → {kv.Value} 成功");
                    (done ?? (done = new List<uint>())).Add(kv.Key);
                }
                else if (_enqueuedAt.TryGetValue(kv.Key, out float at) && Time.unscaledTime - at > MaxWaitSec)
                {
                    SkinSyncMod.ModLog.Info($"延迟应用：netId {kv.Key} → {kv.Value} 超过 {MaxWaitSec}s 未注册，放弃");
                    (expired ?? (expired = new List<uint>())).Add(kv.Key);
                }
            }
            if (done != null) foreach (var k in done) { _pending.Remove(k); _enqueuedAt.Remove(k); }
            if (expired != null) foreach (var k in expired) { _pending.Remove(k); _enqueuedAt.Remove(k); }

            if (done == null && expired == null)
            {
                SkinSyncMod.ModLog.Debug($"延迟应用重试：队列 {_pending.Count} 条，仍等待 NetBody 注册：{DescribePending()}");
            }
            else
            {
                if (done != null)
                {
                    SkinSyncMod.ModLog.Debug($"延迟应用出队：netId {string.Join(", ", done)} 已应用，队列剩余 {_pending.Count} 条");
                }

                if (expired != null)
                {
                    SkinSyncMod.ModLog.Debug($"延迟应用出队：netId {string.Join(", ", expired)} 超时放弃，队列剩余 {_pending.Count} 条");
                }
            }
        }

        /// <summary>Debug 构建用：列出队列里仍在等待的 netId、皮肤与已等待时长。</summary>
        private static string DescribePending()
        {
            var text = new System.Text.StringBuilder();
            foreach (var kv in _pending)
            {
                if (text.Length > 0) text.Append("；");
                text.Append("netId ").Append(kv.Key).Append(" → ").Append(kv.Value);
                if (_enqueuedAt.TryGetValue(kv.Key, out float at))
                {
                    text.Append("（已等待 ").Append((Time.unscaledTime - at).ToString("0.0")).Append("s）");
                }
            }

            return text.ToString();
        }
    }
}
