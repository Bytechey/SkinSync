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

        // __args[0] = callerclientId。用 __args（Harmony 提供的装箱参数数组）取首参，
        // 再经 ToUInt 统一成 uint，与首参的具体类型无关。
        static bool Prefix(object[] __args, NetDataReader reader)
        {
            if (_positionField == null) return true;

            uint callerclientId = (__args != null && __args.Length > 0) ? KrokoshaBridge.ToUInt(__args[0]) : 0u;
            int originalPos = (int)_positionField.GetValue(reader);
            ushort msgId = reader.GetUShort();

            if (msgId == SkinNetworkIDs.SkinChangeMessageId)
            {
                SkinSyncMod.ModLog.Info($"服务端拦截皮肤消息 ID {msgId}");
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
            if (msgId == SkinNetworkIDs.SkinPackRequestMessageId)
            {
                SkinMessageHandlers.OnServerSkinPackRequest(callerclientId, ref reader);
                return false;
            }
            if (msgId == SkinNetworkIDs.SkinPackDataMessageId)
            {
                SkinMessageHandlers.OnServerSkinPackData(callerclientId, ref reader);
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

        // __args[0] = callerclientId。用 __args（Harmony 提供的装箱参数数组）取首参，
        // 再经 ToUInt 统一成 uint，与首参的具体类型无关。
        static bool Prefix(object[] __args, NetDataReader reader)
        {
            if (_positionField == null) return true;

            uint callerclientId = (__args != null && __args.Length > 0) ? KrokoshaBridge.ToUInt(__args[0]) : 0u;
            int originalPos = (int)_positionField.GetValue(reader);
            ushort msgId = reader.GetUShort();

            if (msgId == SkinNetworkIDs.SkinChangeMessageId)
            {
                SkinSyncMod.ModLog.Info($"客户端拦截皮肤消息 ID {msgId}");
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
            if (msgId == SkinNetworkIDs.SkinPackRequestMessageId)
            {
                SkinMessageHandlers.OnClientSkinPackRequest(callerclientId, ref reader);
                return false;
            }
            if (msgId == SkinNetworkIDs.SkinPackDataMessageId)
            {
                SkinMessageHandlers.OnClientSkinPackData(callerclientId, ref reader);
                return false;
            }

            _positionField.SetValue(reader, originalPos);
            return true;
        }
    }
}
