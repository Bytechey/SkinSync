using System.Diagnostics;

namespace SkinSyncMod
{
    /// <summary>
    /// 通信层四段式诊断：客户端发送 → 主机接收 → 主机广播 → 客机接收。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="DebugMessageTrace"/> 遵循同一套约定：全部入口标 Conditional("DEBUG")，
    /// 方法体整体包在 #if DEBUG 内。Release 构建时调用点（含实参求值）被编译器移除，
    /// 方法体也不残留日志常量，分发产物保持干净。
    ///
    /// 与 DebugMessageTrace 的分工：后者面向皮肤包这类高频收发，做 0.5 秒窗口节流；
    /// 本类面向低频的皮肤切换，每次切换都落一条、不节流，便于把一次切换的四段链路按时间对齐。
    ///
    /// 出口是 ModLog.Debug —— 其内部用 LogInfo 输出，BepInEx 默认日志级别即可写进 LogOutput.log。
    /// 排查时在日志里过滤 "[Comms]" 即可。
    /// </remarks>
    internal static class CommsTrace
    {
        /// <summary>点 1：本端（客机或主机）向服务端上报皮肤切换。</summary>
        [Conditional("DEBUG")]
        internal static void ClientSend(string skinId, uint netId, int charType)
        {
#if DEBUG
            ModLog.Debug("[Comms] 1-发送 本端→服务端：skin=" + Describe(skinId)
                + "，netId=" + netId + "，charType=" + charType);
#endif
        }

        /// <summary>点 1 的旁路：本该发送却因为没有活动会话被静默丢弃 —— 通信"毫无反应"最常见的起点。</summary>
        [Conditional("DEBUG")]
        internal static void SendSkipped(string skinId, uint netId, string reason)
        {
#if DEBUG
            ModLog.Debug("[Comms] 1-发送 已跳过：skin=" + Describe(skinId)
                + "，netId=" + netId + "，原因=" + reason);
#endif
        }

        /// <summary>点 2：主机收到客户端上报的皮肤切换。</summary>
        [Conditional("DEBUG")]
        internal static void HostReceive(uint callerId, string skinId, uint netId, int charType)
        {
#if DEBUG
            ModLog.Debug("[Comms] 2-主机接收 client " + callerId + "：skin=" + Describe(skinId)
                + "，netId=" + netId + "，charType=" + charType);
#endif
        }

        /// <summary>点 2 的旁路：主机收到但因校验失败被拒（netId 反查失败 / 冒充他人）。</summary>
        [Conditional("DEBUG")]
        internal static void HostRejected(uint callerId, uint netId, string skinId, string reason)
        {
#if DEBUG
            ModLog.Debug("[Comms] 2-主机已拒绝 client " + callerId + "：skin=" + Describe(skinId)
                + "，netId=" + netId + "，原因=" + reason);
#endif
        }

        /// <summary>点 3：主机把皮肤切换转发/广播出去。recipients 为负表示"全部客户端"（该层无法计数）。</summary>
        [Conditional("DEBUG")]
        internal static void HostBroadcast(string skinId, uint netId, int recipients, uint excludedClientId)
        {
#if DEBUG
            string scope = recipients < 0 ? "全部客户端" : recipients + " 个客户端";
            ModLog.Debug("[Comms] 3-主机广播：skin=" + Describe(skinId) + "，netId=" + netId
                + "，收件人 " + scope + "，跳过来源 client " + excludedClientId);
#endif
        }

        /// <summary>点 3 的旁支：主机向单个客户端单播（新玩家加入/重生时补发已有玩家的皮肤）。</summary>
        [Conditional("DEBUG")]
        internal static void HostUnicast(uint targetClientId, string skinId, uint netId, int charType)
        {
#if DEBUG
            ModLog.Debug("[Comms] 3-主机单播 → client " + targetClientId + "：skin=" + Describe(skinId)
                + "，netId=" + netId + "，charType=" + charType);
#endif
        }

        /// <summary>点 4：客机收到主机转发/广播的皮肤切换。</summary>
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
