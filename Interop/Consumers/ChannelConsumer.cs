// SkinSync Interop
// Copyright (c) 2026 Bytechey
// SPDX-License-Identifier: MIT

using System.Collections.Generic;

namespace SkinSyncMod.Interop.Consumers
{
    /// <summary>封装 SkinSync 具名网络通道的消息收发与接收器注册。</summary>
    internal static class ChannelConsumer
    {
        internal const string PacketName = "skinsync";

        private const string UShortWarningKey = "ChannelConsumer.UShort";

        private static bool? _supportsUShort;

        internal static bool HasUShortPrimitive => SupportsUShortPrimitive();

        internal static bool IsAvailable => InteropBootstrap.Initialize().Find(InteropCapability.Channel) != null;

        /// <summary>创建携带 16 位子消息 ID 头的通道写入器。</summary>
        internal static IInteropWriter CreateWriter(ushort subId)
        {
            IInteropWriter writer = InteropBootstrap.Initialize().Channel.CreateWriter(PacketName);
            writer.PutUShort(subId);
            return writer;
        }

        /// <summary>从读取器首部读取 16 位子消息 ID。</summary>
        internal static ushort ReadSubId(IInteropReader reader)
        {
            return reader == null ? (ushort)0 : reader.GetUShort();
        }

        /// <summary>注册服务端自定义包接收回调。</summary>
        internal static bool RegisterServer(System.Action<InteropPlayerRef, IInteropReader> onServer)
        {
            if (!SupportsUShortPrimitive())
            {
                InteropDiagnostics.WarnOnce(UShortWarningKey, "目标插件缺少 16 位原语，皮肤同步不可用。");
                return false;
            }

            return InteropBootstrap.Initialize().Channel.Register(PacketName, onServer, null);
        }

        /// <summary>注册客户端自定义包接收回调。</summary>
        internal static bool RegisterClient(System.Action<IInteropReader> onClient)
        {
            if (!SupportsUShortPrimitive())
            {
                InteropDiagnostics.WarnOnce(UShortWarningKey, "目标插件缺少 16 位原语，皮肤同步不可用。");
                return false;
            }

            return InteropBootstrap.Initialize().Channel.Register(PacketName, null, onClient);
        }

        private static bool SupportsUShortPrimitive()
        {
            if (_supportsUShort.HasValue) return _supportsUShort.Value;

            IInteropWriter writer = InteropBootstrap.Initialize().Channel.CreateWriter(PacketName);
            object native = writer == null ? null : writer.NativeHandle;
            if (native == null) return false;

            bool supported = native.GetType().GetMethod(
                "Put",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                null,
                new[] { typeof(ushort) },
                null) != null;
            _supportsUShort = supported;
            return supported;
        }

        internal static void SendToServer(IInteropWriter writer)
        {
            InteropBootstrap.Initialize().Channel.SendToServer(writer, true);
        }

        internal static void Broadcast(IInteropWriter writer)
        {
            InteropBootstrap.Initialize().Channel.Broadcast(writer, true);
        }

        internal static void SendToClient(ushort clientId, IInteropWriter writer)
        {
            InteropBootstrap.Initialize().Channel.SendToClient(clientId, writer, true);
        }

        /// <summary>服务端向除发送来源与房主本地回环外的所有客户端转发消息。</summary>
        internal static int ForwardExcept(ushort excludeClientId, IInteropWriter writer, ushort subId)
        {
            if (writer == null)
            {
                return 0;
            }

            IInteropRegistry registry = InteropBootstrap.Initialize();
            IInteropChannel channel = registry.Channel;
            IReadOnlyList<InteropPlayerRef> players = registry.Roster.GetPlayers();
            bool isHostServer = SessionConsumer.IsServer;
            int recipients = 0;
            for (int i = 0; i < players.Count; i++)
            {
                InteropPlayerRef player = players[i];
                if (!player.IsValid || player.ClientId == excludeClientId)
                {
                    continue;
                }

                // 房主已在服务端接收分支处理完毕，跳过本地回环避免重复执行。
                if (isHostServer && player.IsLocal)
                {
                    continue;
                }

                channel.SendToClient(player.ClientId, writer, true);
                recipients++;
            }

            DebugMessageTrace.Forwarded(subId, writer, recipients, excludeClientId);
            return recipients;
        }
    }
}
