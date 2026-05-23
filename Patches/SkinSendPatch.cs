using HarmonyLib;
using KrokoshaCasualtiesMP;  // 引入 Net 类所在的命名空间
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;
using UnityEngine.Windows;

namespace SkinSyncMod.Patches
{
    [HarmonyPatch(typeof(NetBody), "Update")]
    public static class SkinSendPatch
    {
        private static void Postfix(NetBody __instance)
        {
            // 只处理本地玩家
            if (!__instance.is_local) return;

            if (UnityEngine.Input.GetKeyDown(KeyCode.F5))
            {
                // 获取当前皮肤ID（你可以根据实际逻辑决定，这里简单写死 "st1"）
                string newSkinID = "st1";

                // 构造消息
                SkinChangeMessage msg = new SkinChangeMessage
                {
                    skinID = newSkinID,
                    netId = __instance.netId
                };

                // 写入 NetDataWriter
                NetDataWriter writer = new NetDataWriter();
                msg.WriteTo(writer);

                // 发送给服务器（或本地主机）
                Net.Client_Send(DeliveryMethod.ReliableOrdered, writer);

                Debug.Log($"[SkinSync] Sent skin change: {newSkinID} for netId {msg.netId}");

                // 本地立即应用皮肤（避免等待服务器回包）
                if (NetBody.TryGetNetBodyFromId(__instance.netId, out NetBody localNb))
                {
                    SkinApplier.ApplySkinToPlayer(localNb.chara, newSkinID);
                }
            }
        }
    }
}