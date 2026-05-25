using KrokoshaCasualtiesMP;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>
    /// 处理皮肤切换网络消息：客户端收到广播后应用、服务器收到请求后校验并广播。
    /// </summary>
    public static class SkinMessageHandlers
    {
        public static void OnClientSkinMessage(uint callerId, ref NetDataReader reader)
        {
            SkinChangeMessage msg = new SkinChangeMessage();
            msg.Deserialize(reader);
            Debug.Log($"[SkinSync] Client received skin: {msg.skinID} for netId {msg.netId}");
            ApplySkinToPlayer(msg.netId, msg.skinID);
        }

        public static void OnServerSkinMessage(uint callerId, ref NetDataReader reader)
        {
            SkinChangeMessage msg = new SkinChangeMessage();
            msg.Deserialize(reader);
            Debug.Log($"[SkinSync] Server received skin change from client {callerId}: {msg.skinID} for netId {msg.netId}");

            if (callerId != msg.netId)
            {
                Debug.LogWarning($"[SkinSync] Cheat attempt: client {callerId} tried to change skin for {msg.netId}");
                return;
            }

            ApplySkinToPlayer(msg.netId, msg.skinID);

            NetDataWriter writer = new NetDataWriter();
            writer.Put(SkinNetworkIDs.SkinChangeMessageId);
            msg.Serialize(writer);
            var allClients = ServerMain.AllClientIds;
            Net.Server_SendToClients(DeliveryMethod.ReliableOrdered, writer, allClients);

            if (NetPlayer.TryGetPlayerFromClientId(callerId, out NetPlayer plr) && plr.steam_id != 0UL)
                SkinCacheStore.Set(plr.steam_id, msg.skinID);
        }

        private static void ApplySkinToPlayer(uint netId, string characterName)
        {
            if (!NetBody.TryGetNetBodyFromId(netId, out NetBody nb))
            {
                Debug.LogWarning($"[SkinSync] NetBody with netId {netId} not found");
                return;
            }
            GameObject playerObj = nb.chara;
            if (playerObj == null)
            {
                Debug.LogWarning($"[SkinSync] Player GameObject for netId {netId} is null");
                return;
            }
            SkinApplier.ApplySkinToPlayer(playerObj, characterName);
        }
    }
}
