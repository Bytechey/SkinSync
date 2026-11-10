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
        private readonly HashSet<uint> _seenClients = new HashSet<uint>();
        private bool _localSeeded;
        private bool _subscribed;

        /// <summary>主机端：客户端上报 / 切皮肤后写入；其他模块也用此入口同步表。</summary>
        public static void RecordSkin(uint netId, string skinId)
        {
            if (string.IsNullOrEmpty(skinId)) return;
            _serverSkinTable[netId] = skinId;
        }

        /// <summary>主机端：玩家断线时调用清理。</summary>
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
            _seenClients.Remove(clientId);
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _seenClients.Clear();
            _serverSkinTable.Clear();
            _localSeeded = false;
        }

        private void Update()
        {
            if (!SessionConsumer.IsServer) return;
            if (SceneManager.GetActiveScene().name == "PreGen") return;

            if (!_localSeeded)
            {
                string localSkin = SkinSync.Settings != null ? SkinSync.Settings.CurrentSkin.Value : null;
                if (!string.IsNullOrEmpty(localSkin)
                    && RosterConsumer.TryGetLocalNetBody(out _, out uint localNetId, out _))
                {
                    _serverSkinTable[localNetId] = localSkin;
                    _localSeeded = true;
                    SkinSyncMod.ModLog.Info($"主机：本地皮肤已写表 (netId {localNetId} → {localSkin})");
                }
            }

            foreach (var entry in RosterConsumer.EnumeratePlayersWithBody())
            {
                if (entry.Chara == null) continue;
                if (_seenClients.Contains(entry.ClientId)) continue;
                _seenClients.Add(entry.ClientId);

                int sent = 0;
                foreach (var kv in _serverSkinTable)
                {
                    if (kv.Key == entry.NetId) continue;
                    MultiplayerSender.ServerSendSkinChangeToClient(entry.ClientId, kv.Key, kv.Value);
                    sent++;
                }
                SkinSyncMod.ModLog.Info($"主机：检测到 client {entry.ClientId} (netId {entry.NetId}) 加入；表共 {_serverSkinTable.Count} 条，单播 {sent} 条给它");
            }
        }
    }
}
