using System;
using HarmonyLib;

namespace SkinSyncMod.Patches
{
    /// <summary>通过反射将 SkinSync 面板注册为 CuSaveManager 侧栏外部页签。</summary>
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
                Func<string> status = () => SkinSyncI18n.F("bridge.status_by_skinsync", SkinSync.Version);
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
