using System;
using SkinSyncMod.Interop;
using SkinSyncMod.Interop.Consumers;

namespace SkinSyncMod.Patches
{
    /// <summary>
    /// 把 SkinSync 的自定义包注册到「Casualties: Together」的收发管线。
    /// </summary>
    /// <remarks>
    /// 目标插件提供正式扩展点 Together.Multiplayer.RegisterCustomServerReceiver / RegisterCustomClientReceiver：
    /// 包名固定为 <see cref="ChannelConsumer.PacketName"/>，msgid 由 Together 用稳定哈希推导，
    /// 收到包时才回调我们，不用 patch 私有分发方法，也不会和其他 mod 的 id 撞车。
    ///
    /// 包内格式：Together 的 msgid 前缀之后，紧跟 16 位子 ID（取值见 <see cref="SkinNetworkIDs"/>），其后是消息体。
    /// 子 ID 与消息体都由契约读取器/写入器承载，本类只做子 ID 分发。
    /// </remarks>
    public static class TogetherReceiverRegistrar
    {
        private static bool _registered;
        private static bool _serverRegistered;
        private static bool _clientRegistered;
        private static bool _unavailableLogged;

        /// <summary>尝试登记服务端与客户端接收器；已登记返回 true，目标插件不可用或登记失败返回 false 供调用方重试。</summary>
        /// <remarks>两侧分别去重，部分成功后的重试只补未成功的一侧，不会把已登记的接收器再登记一次。</remarks>
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

        // 服务端侧：callerId 取自契约玩家身份的 ClientId（对应 Together.ScavPlayer.playerId）。
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

        // 客户端侧：契约的客户端接收器不携带发送者，传 0（调用方只用于日志）。
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
