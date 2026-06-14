using System;
using HarmonyLib;

namespace SkinSyncMod.Patches
{
    /// <summary>反射对接 SaveManager.ExternalTabRegistry 把皮肤同步面板嵌入侧栏分页。</summary>
    internal static class SaveManagerTabBridge
    {
        internal static bool Register(SkinSyncWindow window)
        {
            try
            {
                var type = AccessTools.TypeByName("CasualtiesUnknown.SaveManager.ExternalTabRegistry");
                if (type == null) return false;
                Action draw = window.DrawEmbedded;
                Func<string> title = () => SkinSyncI18n.T("app.name");
                Func<string> status = () => "由 SkinSync 提供 · v" + SkinSync.Version;
                var fn3 = AccessTools.Method(type, "Register", new[] { typeof(Func<string>), typeof(Action), typeof(Func<string>) });
                if (fn3 != null) { fn3.Invoke(null, new object[] { title, draw, status }); return true; }
                var fn = AccessTools.Method(type, "Register", new[] { typeof(Func<string>), typeof(Action) });
                if (fn == null) return false;
                fn.Invoke(null, new object[] { title, draw });
                return true;
            }
            catch (Exception ex) { ModLog.Warning("[SaveManagerTabBridge] " + ex.Message); return false; }
        }
    }
}
