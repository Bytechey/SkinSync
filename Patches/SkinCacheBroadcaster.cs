using SkinSyncMod.Network;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SkinSyncMod.Patches
{
    /// <summary>主机端读取 SkinCacheStore，在每个客户端 body 就绪后向全员广播其上次使用的皮肤。</summary>
    [DisallowMultipleComponent]
    public class SkinCacheBroadcaster : MonoBehaviour
    {
        private readonly HashSet<uint> _restored = new HashSet<uint>();
        private IDisposable _onPlayerLeftSub;

        private void OnEnable()
        {
            _onPlayerLeftSub = KrokoshaBridge.SubscribePlayerLeft(OnPlayerLeft);
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            try { _onPlayerLeftSub?.Dispose(); } catch { }
            _onPlayerLeftSub = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnPlayerLeft(uint clientId)
        {
            _restored.Remove(clientId);
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _restored.Clear();
        }

        private void Update()
        {
            if (!KrokoshaBridge.IsServer()) return;
            if (SceneManager.GetActiveScene().name == "PreGen") return;

            foreach (var entry in KrokoshaBridge.EnumeratePlayersWithBody())
            {
                if (_restored.Contains(entry.ClientId)) continue;
                if (entry.SteamId == 0UL) { _restored.Add(entry.ClientId); continue; }
                if (entry.Chara == null) continue;

                string skinId = SkinCacheStore.Get(entry.SteamId);
                _restored.Add(entry.ClientId);
                if (string.IsNullOrEmpty(skinId)) continue;

                MultiplayerSender.ServerBroadcastSkinChange(entry.NetId, skinId);
                SkinApplier.ApplySkinToPlayer(entry.Chara, skinId);
                Debug.Log($"[SkinSync] Restored cached skin {skinId} for steamId {entry.SteamId} (clientId {entry.ClientId})");
            }
        }
    }
}
