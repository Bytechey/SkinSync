using System;
using SkinSyncMod.Interop;
using SkinSyncMod.Interop.Consumers;

namespace SkinSyncMod.Patches
{
    /// <summary>向联机模组注册服务端与客户端自定义数据包接收器并按子消息 ID 分发。</summary>
    public static class TogetherReceiverRegistrar
    {
        private static bool _registered;
        private static bool _serverRegistered;
        private static bool _clientRegistered;
        private static bool _unavailableLogged;

        /// <summary>注册服务端与客户端通道接收器，尚未就绪时返回 false 供后续重试。</summary>
        public static bool Register()
        {
            if (_registered) return true;
            if (!SessionConsumer.IsAvailable)
            {
                if (!_unavailableLogged)
                {
                    _unavailableLogged = true;
                    SkinSyncMod.ModLog.Info("TogetherReceiverRegistrar: Together 不可用，等待重试。");
                }
                DebugMessageTrace.RegisterAttempt(false, false, false, false);
                return false;
            }

            if (!_serverRegistered) _serverRegistered = ChannelConsumer.RegisterServer(HandleServerPacket);
            if (!_clientRegistered) _clientRegistered = ChannelConsumer.RegisterClient(HandleClientPacket);

            _registered = _serverRegistered && _clientRegistered;
            DebugMessageTrace.RegisterAttempt(true, _serverRegistered, _clientRegistered, ChannelConsumer.HasUShortPrimitive);
            if (!_registered) return false;

            SkinSyncMod.ModLog.Info("TogetherReceiverRegistrar: 皮肤同步包已挂载（Steam / 直连共用通道）。");
            return true;
        }

        private static void HandleServerPacket(InteropPlayerRef sender, IInteropReader reader)
        {
            try
            {
                ushort msgId = ChannelConsumer.ReadSubId(reader);
                uint callerId = sender.ClientId;
                DebugMessageTrace.Received(true, callerId, msgId, reader == null ? 0 : reader.RemainingBytes);
                switch (msgId)
                {
                    case SkinNetworkIDs.SkinChangeMessageId:
                        SkinMessageHandlers.OnServerSkinMessage(callerId, reader);
                        break;
                    case SkinNetworkIDs.AccessorySyncMessageId:
                        SkinMessageHandlers.OnServerAccessorySync(callerId, reader);
                        break;
                    case SkinNetworkIDs.TailSyncMessageId:
                        SkinMessageHandlers.OnServerTailSync(callerId, reader);
                        break;
                    case SkinNetworkIDs.SkinPackRequestMessageId:
                        SkinMessageHandlers.OnServerSkinPackRequest(callerId, reader);
                        break;
                    case SkinNetworkIDs.SkinPackDataMessageId:
                        SkinMessageHandlers.OnServerSkinPackData(callerId, reader);
                        break;
                    case SkinNetworkIDs.SkinPackChunkMessageId:
                        SkinMessageHandlers.OnServerSkinPackChunk(callerId, reader);
                        break;
                    default:
                        SkinSyncMod.ModLog.Warning($"服务端收到未知皮肤子消息 ID {msgId}");
                        SkinSyncMod.ModLog.Debug($"未知子 ID：服务端来源 clientId {callerId}，子 ID {msgId}，负载 {(reader == null ? 0 : reader.RemainingBytes)} 字节");
                        break;
                }
            }
            catch (Exception ex)
            {
                SkinSyncMod.ModLog.Warning("TogetherReceiverRegistrar: 服务端处理失败 " + ex);
            }
        }

        private static void HandleClientPacket(IInteropReader reader)
        {
            try
            {
                ushort msgId = ChannelConsumer.ReadSubId(reader);
                DebugMessageTrace.Received(false, 0u, msgId, reader == null ? 0 : reader.RemainingBytes);
                switch (msgId)
                {
                    case SkinNetworkIDs.SkinChangeMessageId:
                        SkinMessageHandlers.OnClientSkinMessage(0u, reader);
                        break;
                    case SkinNetworkIDs.AccessorySyncMessageId:
                        SkinMessageHandlers.OnClientAccessorySync(0u, reader);
                        break;
                    case SkinNetworkIDs.TailSyncMessageId:
                        SkinMessageHandlers.OnClientTailSync(0u, reader);
                        break;
                    case SkinNetworkIDs.SkinPackRequestMessageId:
                        SkinMessageHandlers.OnClientSkinPackRequest(0u, reader);
                        break;
                    case SkinNetworkIDs.SkinPackDataMessageId:
                        SkinMessageHandlers.OnClientSkinPackData(0u, reader);
                        break;
                    case SkinNetworkIDs.SkinPackChunkMessageId:
                        SkinMessageHandlers.OnClientSkinPackChunk(0u, reader);
                        break;
                    default:
                        SkinSyncMod.ModLog.Warning($"客户端收到未知皮肤子消息 ID {msgId}");
                        SkinSyncMod.ModLog.Debug($"未知子 ID：客户端来源，子 ID {msgId}，负载 {(reader == null ? 0 : reader.RemainingBytes)} 字节");
                        break;
                }
            }
            catch (Exception ex)
            {
                SkinSyncMod.ModLog.Warning("TogetherReceiverRegistrar: 客户端处理失败 " + ex);
            }
        }
    }
}
