using KrokoshaCasualtiesMP;
using LiteNetLib;
using LiteNetLib.Utils;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SkinSyncMod.Patches
{
    /// <summary>
    /// 主机端读取 SkinCacheStore，在每个客户端 body 就绪后向全员广播其上次使用的皮肤。
    /// 主菜单（PreGen）场景跳过——主菜单阶段 NetBody 状态未稳定，且玩家在主菜单也不需要可见同步。
    /// </summary>
    [DisallowMultipleComponent]
    public class SkinCacheBroadcaster : MonoBehaviour
    {
        private readonly HashSet<uint> _restored = new HashSet<uint>();

        private void OnEnable()
        {
            NetPlayer.OnPlayerLeft += OnPlayerLeft;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            NetPlayer.OnPlayerLeft -= OnPlayerLeft;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnPlayerLeft(NetPlayer plr)
        {
            if (plr != null) _restored.Remove(plr.clientId);
        }

        /// <summary>场景切换（含进入 / 退出主菜单）时清空"已还原"集合，让下次进游戏重新广播每个玩家的皮肤。</summary>
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _restored.Clear();
        }

        private void Update()
        {
            if (!KrokoshaScavMultiplayer.is_server) return;
            // 主菜单场景：不广播也不还原。
            if (SceneManager.GetActiveScene().name == "PreGen") return;
            if (NetPlayer.ClientIdToPlayerDict == null) return;

            foreach (var kv in NetPlayer.ClientIdToPlayerDict)
            {
                NetPlayer plr = kv.Value;
                if (plr == null) continue;
                if (_restored.Contains(plr.clientId)) continue;
                if (plr.steam_id == 0UL) { _restored.Add(plr.clientId); continue; }
                if (!plr.TryGetNetBody(out NetBody nb) || nb == null || nb.chara == null) continue;

                string skinId = SkinCacheStore.Get(plr.steam_id);
                _restored.Add(plr.clientId);
                if (string.IsNullOrEmpty(skinId)) continue;

                var msg = new SkinChangeMessage { skinID = skinId, netId = nb.netId };
                NetDataWriter writer = new NetDataWriter();
                msg.WriteTo(writer);
                var allClients = ServerMain.AllClientIds;
                Net.Server_SendToClients(DeliveryMethod.ReliableOrdered, writer, allClients);
                SkinApplier.ApplySkinToPlayer(nb.chara, skinId);
                Debug.Log($"[SkinSync] Restored cached skin {skinId} for steamId {plr.steam_id} (clientId {plr.clientId})");
            }
        }
    }
}
