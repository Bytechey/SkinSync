using System.Diagnostics;

namespace SkinSyncMod
{
    /// <summary>在 Debug 构建下输出皮肤切换网络同步链路的四阶段跟踪日志。</summary>
    internal static class CommsTrace
    {
        [Conditional("DEBUG")]
        internal static void ClientSend(string skinId, uint netId, int charType)
        {
#if DEBUG
            ModLog.Debug("[Comms] 1-发送 本端->服务端：skin=" + Describe(skinId)
                + "，netId=" + netId + "，charType=" + charType);
#endif
        }

        [Conditional("DEBUG")]
        internal static void SendSkipped(string skinId, uint netId, string reason)
        {
#if DEBUG
            ModLog.Debug("[Comms] 1-发送 已跳过：skin=" + Describe(skinId)
                + "，netId=" + netId + "，原因=" + reason);
#endif
        }

        [Conditional("DEBUG")]
        internal static void HostReceive(uint callerId, string skinId, uint netId, int charType)
        {
#if DEBUG
            ModLog.Debug("[Comms] 2-主机接收 client " + callerId + "：skin=" + Describe(skinId)
                + "，netId=" + netId + "，charType=" + charType);
#endif
        }

        [Conditional("DEBUG")]
        internal static void HostRejected(uint callerId, uint netId, string skinId, string reason)
        {
#if DEBUG
            ModLog.Debug("[Comms] 2-主机已拒绝 client " + callerId + "：skin=" + Describe(skinId)
                + "，netId=" + netId + "，原因=" + reason);
#endif
        }

        [Conditional("DEBUG")]
        internal static void HostBroadcast(string skinId, uint netId, int recipients, uint excludedClientId)
        {
#if DEBUG
            string scope = recipients < 0 ? "全部客户端" : recipients + " 个客户端";
            ModLog.Debug("[Comms] 3-主机广播：skin=" + Describe(skinId) + "，netId=" + netId
                + "，收件人 " + scope + "，跳过来源 client " + excludedClientId);
#endif
        }

        [Conditional("DEBUG")]
        internal static void HostUnicast(uint targetClientId, string skinId, uint netId, int charType)
        {
#if DEBUG
            ModLog.Debug("[Comms] 3-主机单播 -> client " + targetClientId + "：skin=" + Describe(skinId)
                + "，netId=" + netId + "，charType=" + charType);
#endif
        }

        [Conditional("DEBUG")]
        internal static void ClientReceive(string skinId, uint netId, int charType, bool isLocalEcho)
        {
#if DEBUG
            ModLog.Debug("[Comms] 4-客机接收：skin=" + Describe(skinId) + "，netId=" + netId
                + "，charType=" + charType + "，本地回显=" + (isLocalEcho ? "是" : "否"));
#endif
        }

#if DEBUG
        private static string Describe(string skinId)
        {
            return string.IsNullOrEmpty(skinId) ? "(空)" : skinId;
        }
#endif
    }
}
