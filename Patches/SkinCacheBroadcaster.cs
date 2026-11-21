using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using SkinSyncMod.Interop;
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
        private static SkinCacheBroadcaster _activeInstance;
        private static readonly HashSet<uint> _worldReadyClients = new HashSet<uint>();
        private static readonly Dictionary<uint, string> _serverSkinTable = new Dictionary<uint, string>();
        private static readonly Dictionary<uint, Dictionary<string, AccessorySyncMessage>> _serverAccessoryTable
            = new Dictionary<uint, Dictionary<string, AccessorySyncMessage>>();
        private static readonly Dictionary<uint, TailSyncMessage> _serverTailTable = new Dictionary<uint, TailSyncMessage>();

        private static bool _playerStateReflected;
        private static bool _playerStateReflectionValid;
        private static FieldInfo _clientIdToPlayerDictField;
        private static ConstructorInfo _knetidUshortCtor;
        private static FieldInfo _serverPlayerStateField;
        private static FieldInfo _finishedWorldgenField;
        private static FieldInfo _isLoadedInField;

        private readonly Dictionary<uint, uint> _sentToClient = new Dictionary<uint, uint>();
        private float _nextScanTime;
        private float _nextPruneTime;

        private const int MaxAccessoriesPerEntity = 64;
        private const float ScanInterval = 0.5f;
        private const float PruneInterval = 1f;

        private bool _isPreGenScene;
        private bool _seededAsServer;
        private uint _lastHostNetId;
        private string _lastHostSkin;
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

        /// <summary>在主机配件表中缓存指定实体的单个配件覆盖状态，单实体最多保留 64 条覆盖。</summary>
        public static void RecordAccessory(AccessorySyncMessage msg)
        {
            if (msg.netId == 0
                || string.IsNullOrEmpty(msg.skinID)
                || string.IsNullOrEmpty(msg.accId)
                || msg.accId.Length > MaxAccessoriesPerEntity)
            {
                return;
            }
            if (!_serverAccessoryTable.TryGetValue(msg.netId, out var byId))
            {
                byId = new Dictionary<string, AccessorySyncMessage>(System.StringComparer.Ordinal);
                _serverAccessoryTable[msg.netId] = byId;
            }
            if (!byId.ContainsKey(msg.accId) && byId.Count >= MaxAccessoriesPerEntity)
            {
                return;
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

        /// <summary>标记客户端已完成世界场景加载；replayImmediately 为 true 时立即向该客户端单播当前主机缓存表。</summary>
        internal static void NotifyClientWorldReady(uint clientId, bool replayImmediately)
        {
            if (clientId == 0) return;
            _worldReadyClients.Add(clientId);

            if (!replayImmediately
                || _activeInstance == null
                || !SessionConsumer.IsRunning
                || !SessionConsumer.IsServer
                || _activeInstance._isPreGenScene)
            {
                return;
            }

            _activeInstance.SyncLocalHostState();

            uint playerNetId = 0u;
            foreach (var entry in RosterConsumer.EnumeratePlayersWithBody())
            {
                if (entry.ClientId == clientId && entry.Chara != null && entry.NetId != 0)
                {
                    playerNetId = entry.NetId;
                    break;
                }
            }

            int sent = SendAllCachedToClient(clientId, playerNetId);
            if (playerNetId != 0)
            {
                _activeInstance._sentToClient[clientId] = playerNetId;
            }
            SkinSyncMod.ModLog.Info($"主机：收到 client {clientId} 世界就绪通知 (netId {playerNetId})；表共 {_serverSkinTable.Count} 条，即时单播 {sent} 条");
        }

        private void OnEnable()
        {
            _activeInstance = this;
            if (!_subscribed) _subscribed = RosterConsumer.SubscribePlayerLeft(OnPlayerLeft);
            _isPreGenScene = SceneManager.GetActiveScene().name == "PreGen";
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            if (_activeInstance == this) _activeInstance = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnPlayerLeft(uint clientId)
        {
            _sentToClient.Remove(clientId);
            _worldReadyClients.Remove(clientId);
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Additive) return;
            _isPreGenScene = scene.name == "PreGen";
            if (_isPreGenScene || !SessionConsumer.IsRunning)
            {
                _worldReadyClients.Clear();
            }
            _sentToClient.Clear();
            _serverSkinTable.Clear();
            _serverAccessoryTable.Clear();
            _serverTailTable.Clear();
            _nextScanTime = 0f;
            _nextPruneTime = 0f;
            _seededAsServer = false;
            _lastHostNetId = 0;
            _lastHostSkin = null;
        }

        private void Update()
        {
            if (!SessionConsumer.IsRunning)
            {
                if (_worldReadyClients.Count > 0) _worldReadyClients.Clear();
                return;
            }
            if (!SessionConsumer.IsServer || _isPreGenScene) return;

            float now = Time.unscaledTime;
            if (now < _nextScanTime) return;
            _nextScanTime = now + ScanInterval;

            SyncLocalHostState();

            if (now >= _nextPruneTime)
            {
                _nextPruneTime = now + PruneInterval;
                PruneDeadEntries();
            }

            bool hasLocalCid = RosterConsumer.TryGetLocalClientId(out uint localCid);
            foreach (var entry in RosterConsumer.EnumeratePlayersWithBody())
            {
                if (entry.Chara == null || entry.NetId == 0) continue;
                if (hasLocalCid && entry.ClientId == localCid) continue;
                // 延迟到客户端完成世界加载后再单播，避免客机切图或慢加载期间包被冲掉或等待超时。
                if (!_worldReadyClients.Contains(entry.ClientId) && !IsClientLoadedInWorld(entry.ClientId)) continue;
                // 按 netId 对比以识别同一客户端死亡重生后的新实体。
                if (_sentToClient.TryGetValue(entry.ClientId, out uint lastNetId) && lastNetId == entry.NetId) continue;
                _sentToClient[entry.ClientId] = entry.NetId;

                int sent = SendAllCachedToClient(entry.ClientId, entry.NetId);
                SkinSyncMod.ModLog.Info($"主机：检测到 client {entry.ClientId} (netId {entry.NetId}) 加入或重生；表共 {_serverSkinTable.Count} 条，单播 {sent} 条给它");
            }
        }

        private void SyncLocalHostState()
        {
            if (!_seededAsServer)
            {
                _seededAsServer = true;
                if (_serverSkinTable.Count == 0)
                {
                    SkinMessageHandlers.CopyRecordedRemoteSkinsTo(_serverSkinTable);
                }
            }

            if (!RosterConsumer.TryGetLocalNetBody(out _, out uint localNetId, out _) || localNetId == 0)
            {
                return;
            }

            string effectiveLocalSkin = SkinSync.ResolveLocalEffectiveSkinForServer();
            if (string.IsNullOrEmpty(effectiveLocalSkin))
            {
                if (_lastHostNetId != 0 && _serverSkinTable.ContainsKey(_lastHostNetId))
                {
                    _serverSkinTable.Remove(_lastHostNetId);
                    _serverAccessoryTable.Remove(_lastHostNetId);
                    _serverTailTable.Remove(_lastHostNetId);
                }
                _lastHostNetId = localNetId;
                _lastHostSkin = null;
                return;
            }

            if (_lastHostNetId != localNetId
                || _lastHostSkin != effectiveLocalSkin
                || !_serverSkinTable.ContainsKey(localNetId))
            {
                if (_lastHostNetId != 0 && _lastHostNetId != localNetId)
                {
                    _serverSkinTable.Remove(_lastHostNetId);
                    _serverAccessoryTable.Remove(_lastHostNetId);
                    _serverTailTable.Remove(_lastHostNetId);
                }
                RecordSkin(localNetId, effectiveLocalSkin);
                SeedLocalHostOverrides(localNetId, effectiveLocalSkin);
                _lastHostNetId = localNetId;
                _lastHostSkin = effectiveLocalSkin;
                SkinSyncMod.ModLog.Info($"主机：本地皮肤已写表 (netId {localNetId} -> {effectiveLocalSkin})");
            }
        }

        private static int SendAllCachedToClient(uint clientId, uint excludeNetId)
        {
            int sent = 0;
            foreach (var kv in _serverSkinTable)
            {
                if (kv.Key == 0 || (excludeNetId != 0 && kv.Key == excludeNetId)) continue;
                MultiplayerSender.ServerSendSkinChangeToClient(clientId, kv.Key, kv.Value);
                if (_serverAccessoryTable.TryGetValue(kv.Key, out var accMap))
                {
                    foreach (var accKv in accMap)
                    {
                        if (accKv.Value.skinID == kv.Value)
                            MultiplayerSender.ServerSendAccessoryToClient(clientId, accKv.Value);
                    }
                }
                if (_serverTailTable.TryGetValue(kv.Key, out var tailMsg) && tailMsg.skinID == kv.Value)
                {
                    MultiplayerSender.ServerSendTailToClient(clientId, tailMsg);
                }
                sent++;
            }
            return sent;
        }

        private static bool IsClientLoadedInWorld(uint clientId)
        {
            if (!EnsurePlayerStateReflection()) return true;
            try
            {
                IDictionary dict = _clientIdToPlayerDictField.GetValue(null) as IDictionary;
                if (dict == null) return true;

                object playerObj = LookupPlayerByClientId(dict, clientId);
                if (playerObj == null) return false;

                object stateObj = _serverPlayerStateField.GetValue(playerObj);
                if (stateObj == null) return false;

                bool finishedWorldgen = _finishedWorldgenField != null
                    && _finishedWorldgenField.GetValue(stateObj) is bool fw && fw;
                bool isLoadedIn = _isLoadedInField != null
                    && _isLoadedInField.GetValue(stateObj) is bool li && li;
                return finishedWorldgen || isLoadedIn;
            }
            catch
            {
                return true;
            }
        }

        private static object LookupPlayerByClientId(IDictionary dict, uint clientId)
        {
            // Together.ScavPlayer.ClientIdToPlayerDict 键为 Together.knetid(ushort)，兼容探测 ushort/uint 变体。
            if (clientId <= ushort.MaxValue)
            {
                if (_knetidUshortCtor != null)
                {
                    object knetKey = _knetidUshortCtor.Invoke(new object[] { (ushort)clientId });
                    if (knetKey != null && dict.Contains(knetKey)) return dict[knetKey];
                }
                object ushortKey = (ushort)clientId;
                if (dict.Contains(ushortKey)) return dict[ushortKey];
            }
            if (dict.Contains(clientId)) return dict[clientId];
            return null;
        }

        private static bool EnsurePlayerStateReflection()
        {
            if (_playerStateReflected) return _playerStateReflectionValid;
            if (!SessionConsumer.IsAvailable) return false;

            _playerStateReflected = true;
            Type scavPlayerType = ResolveTogetherType("Together.ScavPlayer");
            Type playerStateType = ResolveTogetherType("Together.Server_PlayerState");
            if (scavPlayerType == null || playerStateType == null)
            {
                _playerStateReflectionValid = false;
                return false;
            }

            _clientIdToPlayerDictField = InteropReflection.FindField(scavPlayerType, "ClientIdToPlayerDict");
            _serverPlayerStateField = InteropReflection.FindField(scavPlayerType, "ServerPlayerState");
            _finishedWorldgenField = InteropReflection.FindField(playerStateType, "finished_worldgen");
            _isLoadedInField = InteropReflection.FindField(playerStateType, "is_loaded_in");

            Type knetidType = ResolveTogetherType("Together.knetid");
            if (knetidType != null)
            {
                _knetidUshortCtor = InteropReflection.FindConstructor(knetidType, typeof(ushort));
            }

            _playerStateReflectionValid = _clientIdToPlayerDictField != null
                && _serverPlayerStateField != null
                && (_finishedWorldgenField != null || _isLoadedInField != null);
            return _playerStateReflectionValid;
        }

        private static Type ResolveTogetherType(string fullName)
        {
            var types = InteropBootstrap.Initialize().Types;
            if (types != null && types.TryResolveType(fullName, out Type resolved) && resolved != null)
            {
                return resolved;
            }
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm == null) continue;
                Type t = asm.GetType(fullName, false);
                if (t != null) return t;
            }
            return null;
        }

        private static void SeedLocalHostOverrides(uint localNetId, string localSkin)
        {
            var settings = SkinSync.Settings;
            if (settings == null || settings.IsPassiveSyncMode) return;

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
