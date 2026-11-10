using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using SkinSyncMod.Interop;

namespace SkinSyncMod
{
    /// <summary>Debug 构建专用的收发诊断：0.5 秒窗口节流，避免皮肤包高频收发刷屏。</summary>
    /// <remarks>
    /// 全部入口都是 <see cref="ConditionalAttribute"/> 方法，Release 构建里调用与实参求值一起被编译器移除；
    /// 调试状态（窗口计数、登记次数）只存在本类，业务类型里不新增 Debug 专用字段。
    /// 收发可能来自网络线程，窗口与长度缓存都用锁串行化，日志输出放在锁外避免宿主日志回调重入。
    /// </remarks>
    internal static class DebugMessageTrace
    {
        private const double WindowSeconds = 0.5;
        private static readonly long WindowTicks = (long)(Stopwatch.Frequency * WindowSeconds);

        private static readonly Dictionary<string, Window> Windows = new Dictionary<string, Window>(StringComparer.Ordinal);
        private static readonly Dictionary<Type, PropertyInfo> LengthProperties = new Dictionary<Type, PropertyInfo>();
        private static int _registerAttempts;

        /// <summary>单个节流窗口：上次落日志的时刻与该窗口内累计的条数。</summary>
        private sealed class Window
        {
            internal long LastFlush;
            internal int Count;
        }

        /// <summary>通道登记尝试：记下第几次尝试与两侧结果、失败原因。</summary>
        [Conditional("DEBUG")]
        internal static void RegisterAttempt(bool sessionAvailable, bool serverOk, bool clientOk, bool primitiveAvailable)
        {
            _registerAttempts++;
            ModLog.Debug($"通道登记第 {_registerAttempts} 次尝试：服务端={DescribeResult(serverOk, sessionAvailable, primitiveAvailable)}，客户端={DescribeResult(clientOk, sessionAvailable, primitiveAvailable)}");
        }

        /// <summary>出站消息：子 ID、消息类型与写入器当前字节数。</summary>
        [Conditional("DEBUG")]
        internal static void Sent(IInteropWriter writer, ushort subId, string target)
        {
            Track("send:" + subId, "发送子 ID " + DescribeMessage(subId) + " → " + target + "，负载 " + DescribeBytes(WriterBytes(writer)));
        }

        /// <summary>入站消息：来源 clientId、子 ID 与消息体字节数。</summary>
        [Conditional("DEBUG")]
        internal static void Received(bool serverSide, uint clientId, ushort subId, int bytes)
        {
            string origin = serverSide ? "客户端 clientId " + clientId : "服务端（契约客户端接收器不带 clientId）";
            Track("recv:" + serverSide + ":" + subId, "接收子 ID " + DescribeMessage(subId) + "，来源 " + origin + "，负载 " + DescribeBytes(bytes));
        }

        /// <summary>服务端转发：收件人数量、跳过的来源与负载大小。</summary>
        [Conditional("DEBUG")]
        internal static void Forwarded(ushort subId, IInteropWriter writer, int recipients, ushort skipped)
        {
            Track("fwd:" + subId, "服务端转发子 ID " + DescribeMessage(subId) + "，负载 " + DescribeBytes(WriterBytes(writer)) + "，收件人 " + recipients + " 个，跳过来源 client " + skipped);
        }

        /// <summary>同一 key 在窗口内只落一条日志，并附该窗口累计条数；首条立即落，便于看到突发起点。</summary>
        private static void Track(string key, string message)
        {
            int count;
            lock (Windows)
            {
                if (!Windows.TryGetValue(key, out Window window))
                {
                    window = new Window();
                    Windows[key] = window;
                }

                window.Count++;
                long now = Stopwatch.GetTimestamp();
                if (window.LastFlush != 0 && now - window.LastFlush < WindowTicks)
                {
                    return;
                }

                count = window.Count;
                window.Count = 0;
                window.LastFlush = now;
            }

            ModLog.Debug(message + "（近 0.5 秒内共 " + count + " 条）");
        }

        /// <summary>从底层写入器读已写字节数；契约写入器没有长度成员，这里按目标写入器的 Length 属性探测。</summary>
        /// <remarks>属性缺失时返回 -1，由调用方输出「未知」，不因为诊断读数失败而影响诊断本身。</remarks>
        private static int WriterBytes(IInteropWriter writer)
        {
            object native = writer == null ? null : writer.NativeHandle;
            if (native == null)
            {
                return -1;
            }

            Type type = native.GetType();
            PropertyInfo length;
            lock (LengthProperties)
            {
                if (!LengthProperties.TryGetValue(type, out length))
                {
                    length = type.GetProperty("Length", BindingFlags.Public | BindingFlags.Instance);
                    LengthProperties[type] = length;
                }
            }

            object value = length == null ? null : length.GetValue(native, null);
            return value is int bytes ? bytes : -1;
        }

        private static string DescribeBytes(int bytes) => bytes < 0 ? "未知" : bytes + " 字节";

        private static string DescribeResult(bool ok, bool sessionAvailable, bool primitiveAvailable)
        {
            if (ok)
            {
                return "成功";
            }

            if (!sessionAvailable)
            {
                return "失败（目标不可用）";
            }

            return primitiveAvailable ? "失败（登记被拒或反射异常）" : "失败（原语缺失）";
        }

        private static string DescribeMessage(ushort subId)
        {
            switch (subId)
            {
                case SkinNetworkIDs.SkinChangeMessageId:
                    return "50000/SkinChange（SkinChangeMessage）";
                case SkinNetworkIDs.AccessorySyncMessageId:
                    return "50001/AccessorySync（AccessorySyncMessage）";
                case SkinNetworkIDs.TailSyncMessageId:
                    return "50002/TailSync（TailSyncMessage）";
                case SkinNetworkIDs.SkinPackRequestMessageId:
                    return "50003/SkinPackRequest（SkinPackRequestMessage）";
                case SkinNetworkIDs.SkinPackDataMessageId:
                    return "50004/SkinPackData（SkinPackDataMessage）";
                default:
                    return subId + "/未知（未知消息类型）";
            }
        }
    }
}
