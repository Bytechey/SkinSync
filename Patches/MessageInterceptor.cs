using HarmonyLib;
using LiteNetLib.Utils;
using SkinSyncMod.Network;
using System.Reflection;
using UnityEngine;

namespace SkinSyncMod.Patches
{
    /// <summary>
    /// 拦截服务器端 Net.InvokeServerMessage，将自定义皮肤消息分发到 SkinMessageHandlers，避免与原管线冲突。
    /// </summary>
    [HarmonyPatch]
    public static class ServerMessageInterceptor
    {
        private static FieldInfo _positionField = typeof(NetDataReader).GetField("_position", BindingFlags.NonPublic | BindingFlags.Instance);

        static MethodBase TargetMethod() => KrokoshaBridge.InvokeServerMessageMethod;

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
            if (msgId == SkinNetworkIDs.AccessorySyncMessageId)
            {
                SkinMessageHandlers.OnServerAccessorySync(callerclientId, ref reader);
                return false;
            }
            if (msgId == SkinNetworkIDs.TailSyncMessageId)
            {
                SkinMessageHandlers.OnServerTailSync(callerclientId, ref reader);
                return false;
            }

            _positionField.SetValue(reader, originalPos);
            return true;
        }
    }

    /// <summary>
    /// 拦截客户端 Net.InvokeClientMessage，将自定义皮肤消息分发到 SkinMessageHandlers。
    /// </summary>
    [HarmonyPatch]
    public static class ClientMessageInterceptor
    {
        private static FieldInfo _positionField = typeof(NetDataReader).GetField("_position", BindingFlags.NonPublic | BindingFlags.Instance);

        static MethodBase TargetMethod() => KrokoshaBridge.InvokeClientMessageMethod;

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
            if (msgId == SkinNetworkIDs.AccessorySyncMessageId)
            {
                SkinMessageHandlers.OnClientAccessorySync(callerclientId, ref reader);
                return false;
            }
            if (msgId == SkinNetworkIDs.TailSyncMessageId)
            {
                SkinMessageHandlers.OnClientTailSync(callerclientId, ref reader);
                return false;
            }

            _positionField.SetValue(reader, originalPos);
            return true;
        }
    }
}
