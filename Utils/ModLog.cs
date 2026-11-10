using System.Diagnostics;
using System.Reflection;
using BepInEx.Logging;

namespace SkinSyncMod
{
    /// <summary>
    /// 全模组统一日志出口。写入 BepInEx 日志；ShowInConsole 开启时同步打印到游戏内控制台。
    /// </summary>
    internal static class ModLog
    {
        private const string Prefix = "[SkinSync] ";

        private static ManualLogSource _sink;
        private static MethodInfo _consoleLog;
        private static bool _consoleResolved;

        /// <summary>是否把日志同步到游戏内控制台（` 键打开的控制台）。</summary>
        internal static bool ShowInConsole { get; set; }

        internal static void Init(ManualLogSource sink) => _sink = sink;

        internal static void Info(string message)
        {
            _sink?.LogInfo(message);
            WriteToConsole(message, null);
        }

        internal static void Warning(string message)
        {
            _sink?.LogWarning(message);
            WriteToConsole(message, "yellow");
        }

        internal static void Error(string message)
        {
            _sink?.LogError(message);
            WriteToConsole(message, "red");
        }

        /// <summary>Debug 构建专用诊断日志。Release 构建中带此特性的方法调用会被编译器整体移除，含实参求值。</summary>
        /// <remarks>
        /// 用 LogInfo 而非 LogDebug：BepInEx 5 的 LogLevels 默认不含 Debug（游戏侧配置为 Fatal, Error, Warning, Message, Info），
        /// LogDebug 不会写进 LogOutput.log，而诊断必须能事后翻查。
        /// 方法体整体包在 #if DEBUG 内：[Conditional] 只删调用点，方法体仍会编译进 Release 并残留 "[DBG] " 常量。
        /// </remarks>
        [Conditional("DEBUG")]
        internal static void Debug(string message)
        {
#if DEBUG
            _sink?.LogInfo(Prefix + "[DBG] " + message);
            WriteToConsole(message, "grey");
#endif
        }

        private static void WriteToConsole(string message, string color)
        {
            if (!ShowInConsole) return;
            try
            {
                var instance = ConsoleScript.instance;
                if (instance == null) return;
                if (!ResolveConsoleLog()) return;
                string text = string.IsNullOrEmpty(color) ? Prefix + message : $"<color={color}>{Prefix}{message}</color>";
                _consoleLog.Invoke(instance, new object[] { text });
            }
            catch { }
        }

        private static bool ResolveConsoleLog()
        {
            if (_consoleResolved) return _consoleLog != null;
            _consoleResolved = true;
            _consoleLog = typeof(ConsoleScript).GetMethod("LogToConsole",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                null, new[] { typeof(string) }, null);
            return _consoleLog != null;
        }
    }
}
