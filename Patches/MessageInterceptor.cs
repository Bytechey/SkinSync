using HarmonyLib;
using KrokoshaCasualtiesMP;
using LiteNetLib.Utils;
using System.Reflection;
using UnityEngine;

namespace SkinSyncMod.Patches
{
    /// <summary>
    /// 拦截服务器端 Net.InvokeServerMessage，将自定义皮肤消息分发到 SkinMessageHandlers，避免与原管线冲突。
    /// </summary>
    [HarmonyPatch(typeof(Net), "InvokeServerMessage")]
    public static class ServerMessageInterceptor
    {
        private static FieldInfo _positionField = typeof(NetDataReader).GetField("_position", BindingFlags.NonPublic | BindingFlags.Instance);

        static bool Prefix(uint callerclientId, NetDataReader reader)
        {
            if (_positionField == null) return true;

            int originalPos = (int)_positionField.GetValue(reader);
            ushort msgId = reader.GetUShort();

            if (msgId == SkinNetworkIDs.SkinChangeMessageId)
            {
                Debug.Log($"[SkinSync] Server intercepted skin message ID {msgId}");
                SkinMessageHandlers.OnServerSkinMessage(callerclientId, ref reader);
                return false;
            }

            _positionField.SetValue(reader, originalPos);
            return true;
        }
    }

    /// <summary>
    /// 拦截客户端 Net.InvokeClientMessage，将自定义皮肤消息分发到 SkinMessageHandlers。
    /// </summary>
    [HarmonyPatch(typeof(Net), "InvokeClientMessage")]
    public static class ClientMessageInterceptor
    {
        private static FieldInfo _positionField = typeof(NetDataReader).GetField("_position", BindingFlags.NonPublic | BindingFlags.Instance);

        static bool Prefix(uint callerclientId, NetDataReader reader)
        {
            if (_positionField == null) return true;

            int originalPos = (int)_positionField.GetValue(reader);
            ushort msgId = reader.GetUShort();

            if (msgId == SkinNetworkIDs.SkinChangeMessageId)
            {
                Debug.Log($"[SkinSync] Client intercepted skin message ID {msgId}");
                SkinMessageHandlers.OnClientSkinMessage(callerclientId, ref reader);
                return false;
            }

            _positionField.SetValue(reader, originalPos);
            return true;
        }
    }
}
