using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using SkinSyncMod.Interop;

namespace SkinSyncMod
{
    /// <summary>在 Debug 构建下按 0.5 秒时间窗节流输出网络通道收发诊断日志。</summary>
    internal static class DebugMessageTrace
    {
        [Conditional("DEBUG")]
        internal static void RegisterAttempt(bool sessionAvailable, bool serverOk, bool clientOk, bool primitiveAvailable)
        {
#if DEBUG
            _registerAttempts++;
            ModLog.Debug($"通道登记第 {_registerAttempts} 次尝试：服务端={DescribeResult(serverOk, sessionAvailable, primitiveAvailable)}，客户端={DescribeResult(clientOk, sessionAvailable, primitiveAvailable)}");
#endif
        }

        [Conditional("DEBUG")]
        internal static void Sent(IInteropWriter writer, ushort subId, string target)
        {
#if DEBUG
            Track("send:" + subId, "发送子 ID " + DescribeMessage(subId) + " -> " + target + "，负载 " + DescribeBytes(WriterBytes(writer)));
#endif
        }

        [Conditional("DEBUG")]
        internal static void Received(bool serverSide, uint clientId, ushort subId, int bytes)
        {
#if DEBUG
            string origin = serverSide ? "客户端 clientId " + clientId : "服务端";
            Track("recv:" + serverSide + ":" + subId, "接收子 ID " + DescribeMessage(subId) + "，来源 " + origin + "，负载 " + DescribeBytes(bytes));
#endif
        }

        [Conditional("DEBUG")]
        internal static void Forwarded(ushort subId, IInteropWriter writer, int recipients, ushort skipped)
        {
#if DEBUG
            Track("fwd:" + subId, "服务端转发子 ID " + DescribeMessage(subId) + "，负载 " + DescribeBytes(WriterBytes(writer)) + "，收件人 " + recipients + " 个，跳过来源 client " + skipped);
#endif
        }

#if DEBUG
        private const double WindowSeconds = 0.5;
        private static readonly long WindowTicks = (long)(Stopwatch.Frequency * WindowSeconds);

        private static readonly Dictionary<string, Window> Windows = new Dictionary<string, Window>(StringComparer.Ordinal);
        private static readonly Dictionary<Type, PropertyInfo> LengthProperties = new Dictionary<Type, PropertyInfo>();
        private static int _registerAttempts;

        private sealed class Window
        {
            internal long LastFlush;
            internal int Count;
        }

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
            if (ok) return "成功";
            if (!sessionAvailable) return "失败（目标不可用）";
            return primitiveAvailable ? "失败（登记被拒或反射异常）" : "失败（原语缺失）";
        }

        private static string DescribeMessage(ushort subId)
        {
            switch (subId)
            {
                case SkinNetworkIDs.SkinChangeMessageId:
                    return "50000/SkinChange";
                case SkinNetworkIDs.AccessorySyncMessageId:
                    return "50001/AccessorySync";
                case SkinNetworkIDs.TailSyncMessageId:
                    return "50002/TailSync";
                case SkinNetworkIDs.SkinPackRequestMessageId:
                    return "50003/SkinPackRequest";
                case SkinNetworkIDs.SkinPackDataMessageId:
                    return "50004/SkinPackData";
                case SkinNetworkIDs.SkinPackChunkMessageId:
                    return "50005/SkinPackChunk";
                default:
                    return subId + "/Unknown";
            }
        }
#endif
    }
}
