using System.Collections.Generic;
using SkinSyncMod.Interop.Consumers;
using SkinSyncMod.Network;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SkinSyncMod.Patches
{
    /// <summary>主机端维护各实体当前皮肤、配件覆盖与尾巴参数表，并在客户端加入或角色重生时单播补发。</summary>
    [DisallowMultipleComponent]
    public class SkinCacheBroadcaster : MonoBehaviour
    {
        private static readonly Dictionary<uint, string> _serverSkinTable = new Dictionary<uint, string>();
        private static readonly Dictionary<uint, Dictionary<string, AccessorySyncMessage>> _serverAccessoryTable
            = new Dictionary<uint, Dictionary<string, AccessorySyncMessage>>();
        private static readonly Dictionary<uint, TailSyncMessage> _serverTailTable = new Dictionary<uint, TailSyncMessage>();

        private readonly Dictionary<uint, uint> _sentToClient = new Dictionary<uint, uint>();
        private float _pruneTimer;

        private const float PruneInterval = 1f;

        private bool _localSeeded;
        private bool _subscribed;

        /// <summary>在主机皮肤表中记录实体 netId 与皮肤标识；若皮肤发生变化则清理该实体的旧配件与尾巴覆盖。</summary>
        public static void RecordSkin(uint netId, string skinId)
        {
            if (netId == 0 || string.IsNullOrEmpty(skinId)) return;
            if (_serverSkinTable.TryGetValue(netId, out string prev) && prev != skinId)
            {
                _serverAccessoryTable.Remove(netId);
                _serverTailTable.Remove(netId);
            }
            _serverSkinTable[netId] = skinId;
        }

        /// <summary>在主机配件表中缓存指定实体的单个配件覆盖状态。</summary>
        public static void RecordAccessory(AccessorySyncMessage msg)
        {
            if (msg.netId == 0 || string.IsNullOrEmpty(msg.skinID) || string.IsNullOrEmpty(msg.accId)) return;
            if (!_serverAccessoryTable.TryGetValue(msg.netId, out var byId))
            {
                byId = new Dictionary<string, AccessorySyncMessage>();
                _serverAccessoryTable[msg.netId] = byId;
            }
            byId[msg.accId] = msg;
        }

        /// <summary>在主机尾巴表中缓存指定实体的尾巴物理参数。</summary>
        public static void RecordTail(TailSyncMessage msg)
        {
            if (msg.netId == 0 || string.IsNullOrEmpty(msg.skinID)) return;
            _serverTailTable[msg.netId] = msg;
        }

        /// <summary>查询主机皮肤表中指定 netId 当前登记的皮肤名。</summary>
        public static bool TryGetRecordedSkin(uint netId, out string skinId)
        {
            skinId = null;
            return netId != 0 && _serverSkinTable.TryGetValue(netId, out skinId) && !string.IsNullOrEmpty(skinId);
        }

        /// <summary>从主机状态表中移除指定 netId 的全部记录。</summary>
        public static void RemoveByNetId(uint netId)
        {
            _serverSkinTable.Remove(netId);
            _serverAccessoryTable.Remove(netId);
            _serverTailTable.Remove(netId);
            SkinMessageHandlers.DropRemoteNetId(netId);
        }

        private void OnEnable()
        {
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
            _serverAccessoryTable.Clear();
            _serverTailTable.Clear();
            _pruneTimer = 0f;
            _localSeeded = false;
        }

        private void Update()
        {
            if (!SessionConsumer.IsRunning || !SessionConsumer.IsServer) return;
            if (SceneManager.GetActiveScene().name == "PreGen") return;

            if (!_localSeeded)
            {
                string localSkin = SkinSync.Settings != null ? SkinSync.Settings.CurrentSkin.Value : null;
                if (!string.IsNullOrEmpty(localSkin)
                    && RosterConsumer.TryGetLocalNetBody(out _, out uint localNetId, out _)
                    && localNetId != 0)
                {
                    RecordSkin(localNetId, localSkin);
                    SeedLocalHostOverrides(localNetId, localSkin);
                    _localSeeded = true;
                    SkinSyncMod.ModLog.Info($"主机：本地皮肤已写表 (netId {localNetId} -> {localSkin})");
                }
            }

            _pruneTimer += Time.deltaTime;
            if (_pruneTimer >= PruneInterval)
            {
                _pruneTimer = 0f;
                PruneDeadEntries();
            }

            bool hasLocalCid = RosterConsumer.TryGetLocalClientId(out uint localCid);
            foreach (var entry in RosterConsumer.EnumeratePlayersWithBody())
            {
                if (entry.Chara == null || entry.NetId == 0) continue;
                // 按 netId 对比以识别同一客户端死亡重生后的新实体。
                if (_sentToClient.TryGetValue(entry.ClientId, out uint lastNetId) && lastNetId == entry.NetId) continue;
                _sentToClient[entry.ClientId] = entry.NetId;
                if (hasLocalCid && entry.ClientId == localCid) continue;

                int sent = 0;
                foreach (var kv in _serverSkinTable)
                {
                    if (kv.Key == 0 || kv.Key == entry.NetId) continue;
                    MultiplayerSender.ServerSendSkinChangeToClient(entry.ClientId, kv.Key, kv.Value);
                    if (_serverAccessoryTable.TryGetValue(kv.Key, out var accMap))
                    {
                        foreach (var accKv in accMap)
                        {
                            if (accKv.Value.skinID == kv.Value)
                                MultiplayerSender.ServerSendAccessoryToClient(entry.ClientId, accKv.Value);
                        }
                    }
                    if (_serverTailTable.TryGetValue(kv.Key, out var tailMsg) && tailMsg.skinID == kv.Value)
                    {
                        MultiplayerSender.ServerSendTailToClient(entry.ClientId, tailMsg);
                    }
                    sent++;
                }
                SkinSyncMod.ModLog.Info($"主机：检测到 client {entry.ClientId} (netId {entry.NetId}) 加入或重生；表共 {_serverSkinTable.Count} 条，单播 {sent} 条给它");
            }
        }

        private static void SeedLocalHostOverrides(uint localNetId, string localSkin)
        {
            var settings = SkinSync.Settings;
            if (settings == null || settings.SyncMode.Value == "Passive") return;

            if (settings.SyncAccessories.Value && !_serverAccessoryTable.ContainsKey(localNetId))
            {
                var accOverrides = settings.GetAllAccessoryOverridesForSkin(localSkin);
                if (accOverrides != null && accOverrides.Count > 0)
                {
                    var entries = SkinSync.LoadAccessoryEntriesForSkin(localSkin);
                    foreach (var kv in accOverrides)
                    {
                        AccessoryConfigLoader.Entry baseEntry = null;
                        foreach (var e in entries) { if (e.Id == kv.Key) { baseEntry = e; break; } }
                        if (baseEntry == null) continue;
                        var ov = kv.Value;
                        RecordAccessory(new AccessorySyncMessage
                        {
                            netId = localNetId,
                            skinID = localSkin,
                            accId = kv.Key,
                            enabled = ov?.Enabled ?? baseEntry.Enabled,
                            offX = ov?.OffX ?? baseEntry.OffX,
                            offY = ov?.OffY ?? baseEntry.OffY,
                            rotation = ov?.Rotation ?? baseEntry.Rotation,
                            zOrder = ov?.ZOrder ?? baseEntry.ZOrder,
                        });
                    }
                }
            }

            if (settings.SyncTailDeform.Value
                && !_serverTailTable.ContainsKey(localNetId)
                && (settings.GetTailOverride(localSkin) != null || !settings.TailDeformEnabled.Value))
            {
                RecordTail(MultiplayerSender.BuildLocalTailMessage(localNetId, localSkin));
            }
        }

        private void PruneDeadEntries()
        {
            if (_serverSkinTable.Count == 0 && _serverAccessoryTable.Count == 0 && _serverTailTable.Count == 0) return;

            var live = new HashSet<uint>();
            foreach (var entry in RosterConsumer.EnumeratePlayersWithBody())
                if (entry.Chara != null && entry.NetId != 0) live.Add(entry.NetId);
            if (RosterConsumer.TryGetLocalNetBody(out _, out uint localNetId, out _) && localNetId != 0)
                live.Add(localNetId);

            List<uint> dead = null;
            foreach (var kv in _serverSkinTable)
                if (!live.Contains(kv.Key)) (dead ?? (dead = new List<uint>())).Add(kv.Key);
            foreach (var kv in _serverAccessoryTable)
                if (!live.Contains(kv.Key) && (dead == null || !dead.Contains(kv.Key))) (dead ?? (dead = new List<uint>())).Add(kv.Key);
            foreach (var kv in _serverTailTable)
                if (!live.Contains(kv.Key) && (dead == null || !dead.Contains(kv.Key))) (dead ?? (dead = new List<uint>())).Add(kv.Key);
            if (dead == null) return;

            foreach (var netId in dead) RemoveByNetId(netId);
            SkinSyncMod.ModLog.Info($"主机：清理 {dead.Count} 条失效皮肤记录（netId 已不存在）");
        }
    }
}
