// SkinSync Interop
// Copyright (c) 2026 Bytechey
// SPDX-License-Identifier: MIT

using System.Collections.Generic;

namespace SkinSyncMod.Interop.Consumers
{
    /// <summary>通道消费者：把契约具名通道收敛成 SkinSync 的单一包名收发入口。</summary>
    /// <remarks>
    /// 目标程序集：CasualtiesMP.dll，BepInPlugin GUID "CasualtiesMP"（Together 5.3.1）。
    /// 依赖的目标成员全名：Together.Multiplayer.CreateNamedWriter(string)、
    /// Together.Multiplayer.RegisterCustomServerReceiver(string, Action&lt;ScavPlayer, NetDataReader&gt;)、
    /// Together.Multiplayer.RegisterCustomClientReceiver(string, Action&lt;NetDataReader&gt;)、
    /// Together.Net.Client_Send(DeliveryMethod, NetDataWriter)、
    /// Together.Net.Server_SendTo(DeliveryMethod, NetDataWriter, knetid)、
    /// Together.Net.Server_SendToClients(DeliveryMethod, NetDataWriter, IEnumerable&lt;knetid&gt;)。
    /// 全部经契约 IInteropChannel 转发，本消费者不出现目标类型。
    /// 负载首字段为 16 位无符号子 ID（取值见 SkinNetworkIDs），其后是消息体；
    /// 子 ID 走契约的 16 位原语，与目标写入器 NetDataWriter.Put(ushort) / 读取器 NetDataReader.GetUShort() 一一对应。
    /// 登记前用一次性写入器探测该原语：缺失时拒绝登记，避免降级为 32 位写入导致整条消息错位。
    /// </remarks>
    internal static class ChannelConsumer
    {
        /// <summary>自定义包名；Together 用它推导 msgid 并区分收发器。</summary>
        internal const string PacketName = "skinsync";

        private const string UShortWarningKey = "ChannelConsumer.UShort";

        private static bool? _supportsUShort;

        /// <summary>目标写入器是否提供原生 16 位原语；登记失败时用来区分「原语缺失」与「登记被拒」。</summary>
        internal static bool HasUShortPrimitive
        {
            get { return SupportsUShortPrimitive(); }
        }

        /// <summary>是否已登记通道提供方（具名通道可用）。</summary>
        internal static bool IsAvailable
        {
            get { return InteropBootstrap.Initialize().Find(InteropCapability.Channel) != null; }
        }

        /// <summary>按子 ID 创建写入器：先写入 2 字节子 ID，再交给调用方写消息体。</summary>
        /// <remarks>通道不可用时返回丢弃写入的空实现，调用方的发送会静默失效。</remarks>
        internal static IInteropWriter CreateWriter(ushort subId)
        {
            IInteropWriter writer = InteropBootstrap.Initialize().Channel.CreateWriter(PacketName);
            writer.PutUShort(subId);
            return writer;
        }

        /// <summary>从接收器读取负载首字段的 2 字节子 ID。</summary>
        internal static ushort ReadSubId(IInteropReader reader)
        {
            return reader == null ? (ushort)0 : reader.GetUShort();
        }

        /// <summary>登记服务端侧接收器；目标缺少原生 16 位原语时拒绝登记。</summary>
        /// <remarks>两侧分开登记，便于调用方在部分失败时只重试失败的一侧，避免重复登记。</remarks>
        internal static bool RegisterServer(System.Action<InteropPlayerRef, IInteropReader> onServer)
        {
            if (!SupportsUShortPrimitive())
            {
                InteropDiagnostics.WarnOnce(UShortWarningKey, "目标插件缺少 16 位原语，皮肤同步不可用。");
                return false;
            }

            return InteropBootstrap.Initialize().Channel.Register(PacketName, onServer, null);
        }

        /// <summary>登记客户端侧接收器；目标缺少原生 16 位原语时拒绝登记。</summary>
        internal static bool RegisterClient(System.Action<IInteropReader> onClient)
        {
            if (!SupportsUShortPrimitive())
            {
                InteropDiagnostics.WarnOnce(UShortWarningKey, "目标插件缺少 16 位原语，皮肤同步不可用。");
                return false;
            }

            return InteropBootstrap.Initialize().Channel.Register(PacketName, null, onClient);
        }

        /// <summary>探测目标写入器是否提供原生 16 位写入原语。</summary>
        /// <remarks>
        /// 只创建一个一次性写入器并检查其底层对象的 Put(ushort) 重载，不发送消息、不做类型强转、不依赖类型名；
        /// 判定条件与 provider 解析该成员的条件一致。目标尚未解析（底层句柄为空）时不缓存结果，供下一轮重试重新探测。
        /// </remarks>
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

        /// <summary>本端 → 服务端；服务端自身调用时由目标插件本地派发。</summary>
        internal static void SendToServer(IInteropWriter writer)
        {
            InteropBootstrap.Initialize().Channel.SendToServer(writer, true);
        }

        /// <summary>服务端广播给名册中的全部客户端。</summary>
        internal static void Broadcast(IInteropWriter writer)
        {
            InteropBootstrap.Initialize().Channel.Broadcast(writer, true);
        }

        /// <summary>服务端单播给指定客户端；clientId 0 表示房主自己。</summary>
        internal static void SendToClient(ushort clientId, IInteropWriter writer)
        {
            InteropBootstrap.Initialize().Channel.SendToClient(clientId, writer, true);
        }

        /// <summary>服务端转发：向名册中除来源以外的每个客户端各单播一次同一写入器。</summary>
        /// <param name="excludeClientId">来源客户端，不参与转发。</param>
        /// <param name="writer">已写入子 ID 与消息体的写入器。</param>
        /// <param name="subId">子 ID，仅用于诊断日志。</param>
        /// <returns>实际转发的收件人数量。</returns>
        /// <remarks>
        /// 契约通道没有「广播但排除来源」的成员，这里按名册逐客户端单播实现；
        /// 写入器发送后不复位，重复发送同一写入器即重发同一缓冲。
        /// </remarks>
        internal static int ForwardExcept(ushort excludeClientId, IInteropWriter writer, ushort subId)
        {
            if (writer == null)
            {
                return 0;
            }

            IInteropRegistry registry = InteropBootstrap.Initialize();
            IInteropChannel channel = registry.Channel;
            IReadOnlyList<InteropPlayerRef> players = registry.Roster.GetPlayers();
            int recipients = 0;
            for (int i = 0; i < players.Count; i++)
            {
                InteropPlayerRef player = players[i];
                if (!player.IsValid || player.ClientId == excludeClientId)
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
