using SkinSyncMod.Interop.Consumers;
using SkinSyncMod.Network;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SkinSyncMod.Patches
{
    /// <summary>主机维护 netId → skinId 表；新客户端 NetBody 出现时把现有玩家皮肤单播给它。</summary>
    [DisallowMultipleComponent]
    public class SkinCacheBroadcaster : MonoBehaviour
    {
        private static readonly Dictionary<uint, string> _serverSkinTable = new Dictionary<uint, string>();

        /// <summary>clientId → 上一次给它单播时对端的 netId。用来识别「同一玩家死亡重生换了新 netId」。/// </summary>
        private readonly Dictionary<uint, uint> _sentToClient = new Dictionary<uint, uint>();
        private float _pruneTimer;

        /// <summary>失效记录清理间隔（秒）。玩家数很少，1 秒一次开销可忽。</summary>
        private const float PruneInterval = 1f;

        private bool _localSeeded;
        private bool _subscribed;

        /// <summary>主机端：客户端上报 / 切皮肤后写入；其他模块也用此入口同步表。</summary>
        public static void RecordSkin(uint netId, string skinId)
        {
            if (string.IsNullOrEmpty(skinId)) return;
            _serverSkinTable[netId] = skinId;
        }

        /// <summary>
        /// 主机端：玩家断线时调用。
        /// 这里只能清按 clientId 记账的发送记录：Together 在触发 OnPlayerLeft 之前就把该玩家
        /// 从 ClientIdToPlayerDict 移除了（Player.cs:1300-1307），拿不到 netId 去反查。
        /// 表里的 netId → skinId 由 Update 里的存活性校验兜底清除。
        /// </summary>
        public static void RemoveByNetId(uint netId)
        {
            _serverSkinTable.Remove(netId);
        }

        private void OnEnable()
        {
            // 事件订阅为进程级且无退订 API（契约 §6.4.4）：只订阅一次，
            // 否则组件被禁用再启用会重复挂上同一个回调，OnPlayerLeft 会执行两遍。
            if (!_subscribed) _subscribed = RosterConsumer.SubscribePlayerLeft(OnPlayerLeft);
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnPlayerLeft(uint clientId)
        {
            _sentToClient.Remove(clientId);
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _sentToClient.Clear();
            _serverSkinTable.Clear();
            _pruneTimer = 0f;
            _localSeeded = false;
        }

        private void Update()
        {
            if (!SessionConsumer.IsServer) return;
            if (SceneManager.GetActiveScene().name == "PreGen") return;

            if (!_localSeeded)
            {
                string localSkin = SkinSync.Settings != null ? SkinSync.Settings.CurrentSkin.Value : null;
                // netId 为 0 表示实体还没被分配到网络 ID（进图瞬间）。此时写表会在表里留下一个
                // netId 0 的假条目，之后每个新加入的客机都会收到它，那边只能永远排队等 netId 0 注册。
                if (!string.IsNullOrEmpty(localSkin)
                    && RosterConsumer.TryGetLocalNetBody(out _, out uint localNetId, out _)
                    && localNetId != 0)
                {
                    _serverSkinTable[localNetId] = localSkin;
                    _localSeeded = true;
                    SkinSyncMod.ModLog.Info($"主机：本地皮肤已写表 (netId {localNetId} → {localSkin})");
                }
            }

            _pruneTimer += Time.deltaTime;
            if (_pruneTimer >= PruneInterval)
            {
                _pruneTimer = 0f;
                PruneDeadEntries();
            }

            foreach (var entry in RosterConsumer.EnumeratePlayersWithBody())
            {
                if (entry.Chara == null) continue;
                // 按 netId 而不只按 clientId 记账：同一玩家死亡重生会拿到新 netId，
                // 只按 clientId 会认为「已发过」而不再补发，导致该玩家变回原皮。
                if (_sentToClient.TryGetValue(entry.ClientId, out uint lastNetId) && lastNetId == entry.NetId) continue;
                _sentToClient[entry.ClientId] = entry.NetId;

                int sent = 0;
                foreach (var kv in _serverSkinTable)
                {
                    // netId 0 是无效条目（历史残留），单播出去对面无从应用。
                    if (kv.Key == 0 || kv.Key == entry.NetId) continue;
                    MultiplayerSender.ServerSendSkinChangeToClient(entry.ClientId, kv.Key, kv.Value);
                    sent++;
                }
                SkinSyncMod.ModLog.Info($"主机：检测到 client {entry.ClientId} (netId {entry.NetId}) 加入或重生；表共 {_serverSkinTable.Count} 条，单播 {sent} 条给它");
            }
        }

        /// <summary>
        /// 清掉表里已经失效的 netId。
        /// Together 的实体系统会复用 id（ScavLiteEntitySystem.cs:481-484），旧 netId 不清的话，
        /// 新玩家拿到同一个 netId 时会收到上一个人的皮肤。
        /// </summary>
        private void PruneDeadEntries()
        {
            if (_serverSkinTable.Count == 0) return;

            var live = new HashSet<uint>();
            foreach (var entry in RosterConsumer.EnumeratePlayersWithBody())
                if (entry.Chara != null) live.Add(entry.NetId);
            if (RosterConsumer.TryGetLocalNetBody(out _, out uint localNetId, out _)) live.Add(localNetId);

            List<uint> dead = null;
            foreach (var kv in _serverSkinTable)
                if (!live.Contains(kv.Key)) (dead ?? (dead = new List<uint>())).Add(kv.Key);
            if (dead == null) return;

            foreach (var netId in dead) _serverSkinTable.Remove(netId);
            SkinSyncMod.ModLog.Info($"主机：清理 {dead.Count} 条失效皮肤记录（netId 已不存在）");
        }
    }
}
