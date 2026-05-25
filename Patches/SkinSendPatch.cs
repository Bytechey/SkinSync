using HarmonyLib;
using KrokoshaCasualtiesMP;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace SkinSyncMod.Patches
{
    /// <summary>
    /// 监听本地玩家 F5 按键，发送皮肤切换请求到服务器并立即在本地应用。
    /// </summary>
    [HarmonyPatch(typeof(NetBody), "Update")]
    public static class SkinSendPatch
    {
        private static void Postfix(NetBody __instance)
        {
            if (!__instance.is_local) return;
            if (!UnityEngine.Input.GetKeyDown(KeyCode.F5)) return;

            string newSkinID = "st1";
            SkinChangeMessage msg = new SkinChangeMessage
            {
                skinID = newSkinID,
                netId = __instance.netId,
            };

            NetDataWriter writer = new NetDataWriter();
            msg.WriteTo(writer);
            Net.Client_Send(DeliveryMethod.ReliableOrdered, writer);
            Debug.Log($"[SkinSync] Sent skin change: {newSkinID} for netId {msg.netId}");

            if (NetBody.TryGetNetBodyFromId(__instance.netId, out NetBody localNb))
            {
                SkinApplier.ApplySkinToPlayer(localNb.chara, newSkinID);
            }
        }
    }
}
