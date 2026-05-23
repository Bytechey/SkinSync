using HarmonyLib;
using KrokoshaCasualtiesMP;
using LiteNetLib.Utils;
using System.Reflection;
using UnityEngine;

namespace SkinSyncMod.Patches
{
    // 拦截服务器端消息
    [HarmonyPatch(typeof(Net), "InvokeServerMessage")]
    public static class ServerMessageInterceptor
    {
        private static FieldInfo _positionField = typeof(NetDataReader).GetField("_position", BindingFlags.NonPublic | BindingFlags.Instance);

        static bool Prefix(uint callerclientId, NetDataReader reader)
        {
            if (_positionField == null) return true;

            // 保存原始位置
            int originalPos = (int)_positionField.GetValue(reader);
            // 读取消息 ID（这会移动内部位置）
            ushort msgId = reader.GetUShort();

            if (msgId == SkinNetworkIDs.SkinChangeMessageId)
            {
                Debug.Log($"[SkinSync] Server intercepted skin message ID {msgId}");
                // 注意：此时 reader 已经指向消息内容，直接传递给处理函数
                SkinMessageHandlers.OnServerSkinMessage(callerclientId, ref reader);
                // 阻止原方法执行（因为我们已经处理了）
                return false;
            }
            else
            {
                // 不是我们的消息：恢复位置，让原方法继续处理
                _positionField.SetValue(reader, originalPos);
                return true;
            }
        }
    }

    // 拦截客户端消息
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
            else
            {
                _positionField.SetValue(reader, originalPos);
                return true;
            }
        }
    }
}