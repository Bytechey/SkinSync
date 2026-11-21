using System.Collections.Generic;
using System.Reflection;
using SkinSyncMod.Interop.Consumers;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SkinSyncMod.Patches
{
    /// <summary>缓存尚未完成 NetBody 注册的远端皮肤请求并定时重试应用。</summary>
    [DisallowMultipleComponent]
    public class PendingSkinApplier : MonoBehaviour
    {
        private struct PendingEntry
        {
            public string SkinId;
            public int CharType;
            public float EnqueuedAt;
        }

        private static readonly Dictionary<uint, PendingEntry> _pending = new Dictionary<uint, PendingEntry>();
        private static readonly FieldInfo InstantiatingWorldField = typeof(WorldGeneration).GetField(
            "instantiatingWorld", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo DoingRegenField = typeof(WorldGeneration).GetField(
            "doingRegen", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private const float TickIntervalSec = 0.25f;
        private const float PackRetryIntervalSec = 1f;
        private const float MaxWaitSec = 30f;
        private float _nextTick;
        private float _nextPackRetryAt;

        /// <summary>将暂未找到实体对象的皮肤同步请求加入重试队列。</summary>
        public static void Enqueue(uint netId, string skinId, string reason = null, int charType = 0)
        {
            if (netId == 0 || string.IsNullOrEmpty(skinId)) return;
            _pending[netId] = new PendingEntry
            {
                SkinId = skinId,
                CharType = charType,
                EnqueuedAt = Time.unscaledTime,
            };
            SkinSyncMod.ModLog.Debug($"延迟应用入队：netId {netId} -> {skinId}，队列 {_pending.Count} 条，原因：{(string.IsNullOrEmpty(reason) ? "NetBody 未注册" : reason)}");
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Additive) return;
            if (SkinSync.TogetherInstalled && SessionConsumer.IsRunning && scene.name != "PreGen")
            {
                // 活跃联机切图期间保留在途待应用条目并重置计时起点，避免场景加载冲掉提前到达的换肤消息。
                if (_pending.Count > 0)
                {
                    float now = Time.unscaledTime;
                    var keys = new List<uint>(_pending.Keys);
                    for (int i = 0; i < keys.Count; i++)
                    {
                        uint key = keys[i];
                        PendingEntry entry = _pending[key];
                        entry.EnqueuedAt = now;
                        _pending[key] = entry;
                    }
                }
                return;
            }
            _pending.Clear();
        }

        private void Update()
        {
            if (!SessionConsumer.IsRunning)
            {
                _pending.Clear();
                return;
            }

            float now = Time.unscaledTime;
            if (now >= _nextPackRetryAt)
            {
                _nextPackRetryAt = now + PackRetryIntervalSec;
                SkinApplier.RetryPendingPackRequests();
            }

            if (_pending.Count == 0) return;
            if (now < _nextTick) return;
            _nextTick = now + TickIntervalSec;

            bool worldGenerating = IsWorldGenerating();
            List<uint> done = null;
            List<uint> expired = null;
            List<uint> deferred = null;
            foreach (var kv in _pending)
            {
                if (RosterConsumer.TryGetNetBodyFromId(kv.Key, out _, out GameObject chara, out _) && chara != null)
                {
                    AccessoryAttacher.BindPendingOverrides(kv.Key, chara);
                    TailDeformConfig.BindPendingOverrides(kv.Key, chara);
                    SkinApplier.ApplySkinToPlayer(chara, kv.Value.SkinId, kv.Value.CharType);
                    SkinSyncMod.ModLog.Info($"延迟应用：netId {kv.Key} -> {kv.Value.SkinId} 成功");
                    (done ?? (done = new List<uint>())).Add(kv.Key);
                }
                else if (worldGenerating)
                {
                    // 世界生成与实例化耗时可能超过 30 秒，生成阶段顺延超时起点。
                    (deferred ?? (deferred = new List<uint>())).Add(kv.Key);
                }
                else if (now - kv.Value.EnqueuedAt > MaxWaitSec)
                {
                    AccessoryAttacher.DropPendingOverrides(kv.Key);
                    TailDeformConfig.DropPendingOverrides(kv.Key);
                    SkinSyncMod.ModLog.Info($"延迟应用：netId {kv.Key} -> {kv.Value.SkinId} 超过 {MaxWaitSec}s 未注册，放弃");
                    (expired ?? (expired = new List<uint>())).Add(kv.Key);
                }
            }
            if (deferred != null)
            {
                for (int i = 0; i < deferred.Count; i++)
                {
                    uint k = deferred[i];
                    PendingEntry entry = _pending[k];
                    entry.EnqueuedAt = now;
                    _pending[k] = entry;
                }
            }
            if (done != null) foreach (var k in done) _pending.Remove(k);
            if (expired != null) foreach (var k in expired) _pending.Remove(k);

            if (done == null && expired == null)
            {
                SkinSyncMod.ModLog.Debug($"延迟应用重试：队列 {_pending.Count} 条，等待注册：{DescribePending()}");
            }
            else
            {
                if (done != null)
                {
                    SkinSyncMod.ModLog.Debug($"延迟应用出队：netId {string.Join(", ", done)} 已应用，剩余 {_pending.Count} 条");
                }
                if (expired != null)
                {
                    SkinSyncMod.ModLog.Debug($"延迟应用出队：netId {string.Join(", ", expired)} 超时放弃，剩余 {_pending.Count} 条");
                }
            }
        }

        private static bool IsWorldGenerating()
        {
            // generatingWorld 为公开字段，instantiatingWorld 与 doingRegen 在游戏程序集中为私有字段。
            WorldGeneration world = WorldGeneration.world;
            if (world == null || world.generatingWorld) return true;
            if (InstantiatingWorldField != null && InstantiatingWorldField.GetValue(world) is bool inst && inst) return true;
            if (DoingRegenField != null && DoingRegenField.GetValue(world) is bool regen && regen) return true;
            return false;
        }

        private static string DescribePending()
        {
            var text = new System.Text.StringBuilder();
            foreach (var kv in _pending)
            {
                if (text.Length > 0) text.Append("；");
                text.Append("netId ").Append(kv.Key).Append(" -> ").Append(kv.Value.SkinId)
                    .Append("（已等待 ").Append((Time.unscaledTime - kv.Value.EnqueuedAt).ToString("0.0")).Append("s）");
            }
            return text.ToString();
        }
    }
}
