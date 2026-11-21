using System;
using System.Diagnostics;
using System.Reflection;
using BepInEx.Logging;

namespace SkinSyncMod
{
    /// <summary>统一输出 BepInEx 日志并按配置同步打印到游戏内控制台。</summary>
    internal static class ModLog
    {
        private const string Prefix = "[SkinSync] ";

        private static ManualLogSource _sink;
        private static Action<ConsoleScript, string> _consoleLog;
        private static bool _consoleResolved;

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
                if (instance == null || !ResolveConsoleLog()) return;
                string text = string.IsNullOrEmpty(color) ? Prefix + message : $"<color={color}>{Prefix}{message}</color>";
                _consoleLog(instance, text);
            }
            catch { }
        }

        private static bool ResolveConsoleLog()
        {
            if (_consoleResolved) return _consoleLog != null;
            _consoleResolved = true;
            MethodInfo logMethod = typeof(ConsoleScript).GetMethod(
                "LogToConsole",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                null,
                new[] { typeof(string) },
                null);
            if (logMethod != null)
            {
                _consoleLog = (Action<ConsoleScript, string>)Delegate.CreateDelegate(
                    typeof(Action<ConsoleScript, string>),
                    logMethod,
                    throwOnBindFailure: false);
            }
            return _consoleLog != null;
        }
    }
}
