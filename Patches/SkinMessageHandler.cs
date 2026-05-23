using KrokoshaCasualtiesMP;
using LiteNetLib;
using LiteNetLib.Utils;
using System.Collections.Generic;
using UnityEngine;

namespace SkinSyncMod
{
    public static class SkinMessageHandlers
    {
        // 客户端收到皮肤消息时调用（通常是服务器广播来的）
        public static void OnClientSkinMessage(uint callerId, ref NetDataReader reader)
        {
            SkinChangeMessage msg = new SkinChangeMessage();
            msg.Deserialize(reader);

            Debug.Log($"[SkinSync] Client received skin: {msg.skinID} for netId {msg.netId}");

            ApplySkinToPlayer(msg.netId, msg.skinID);
        }

        // 服务器收到客户端的皮肤切换请求时调用
        public static void OnServerSkinMessage(uint callerId, ref NetDataReader reader)
        {
            SkinChangeMessage msg = new SkinChangeMessage();
            msg.Deserialize(reader);

            Debug.Log($"[SkinSync] Server received skin change from client {callerId}: {msg.skinID} for netId {msg.netId}");

            // 可选：验证权限（如检查 callerId 是否等于 msg.netId，防止作弊）
            if (callerId != msg.netId)
            {
                Debug.LogWarning($"[SkinSync] Cheat attempt: client {callerId} tried to change skin for {msg.netId}");
                return;
            }

            // 服务器自己也应用一次（如果有对应的 NetBody）
            ApplySkinToPlayer(msg.netId, msg.skinID);

            // 广播给所有客户端（包括发送者自己，不过发送者自己已通过本地预览更新，无所谓）
            NetDataWriter writer = new NetDataWriter();
            writer.Put(SkinNetworkIDs.SkinChangeMessageId);
            msg.Serialize(writer);
            var allClients = ServerMain.AllClientIds; // 假设有这个属性
            Net.Server_SendToClients(DeliveryMethod.ReliableOrdered, writer, allClients);
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