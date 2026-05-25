using KrokoshaCasualtiesMP;
using LiteNetLib;
using LiteNetLib.Utils;
using System.Collections.Generic;
using UnityEngine;

namespace SkinSyncMod.Patches
{
    /// <summary>
    /// 主机端读取 SkinCacheStore，在每个客户端 body 就绪后向全员广播其上次使用的皮肤。
    /// </summary>
    [DisallowMultipleComponent]
    public class SkinCacheBroadcaster : MonoBehaviour
    {
        private readonly HashSet<uint> _restored = new HashSet<uint>();

        private void OnEnable()
        {
            NetPlayer.OnPlayerLeft += OnPlayerLeft;
        }

        private void OnDisable()
        {
            NetPlayer.OnPlayerLeft -= OnPlayerLeft;
        }

        private void OnPlayerLeft(NetPlayer plr)
        {
            if (plr != null) _restored.Remove(plr.clientId);
        }

        private void Update()
        {
            if (!KrokoshaScavMultiplayer.is_server) return;
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
