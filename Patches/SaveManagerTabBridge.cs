using System;
using System.Reflection;
using BepInEx.Bootstrap;
using UnityEngine;

namespace SkinSyncMod.Patches
{
    /// <summary>通过反射将 SkinSync 面板注册为 CuSaveManager 侧栏外部页签，并在宿主在场时共享统一 UI 缩放。</summary>
    internal static class SaveManagerTabBridge
    {
        private const string SaveManagerGuid = "com.casualtiesUnknown.saveManager";
        private const string RegistryTypeName = "CasualtiesUnknown.SaveManager.ExternalTabRegistry";
        private const BindingFlags StaticFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        private static bool _scalePropResolved;
        private static PropertyInfo _uiScaleProp;

        internal static bool Register(SkinSyncWindow window)
        {
            if (window == null) return false;
            try
            {
                if (!Chainloader.PluginInfos.TryGetValue(SaveManagerGuid, out var info))
                    return false;

                Type type = info?.Instance?.GetType().Assembly.GetType(RegistryTypeName, throwOnError: false);
                if (type == null) return false;

                Action draw = window.DrawEmbedded;
                Func<string> title = () => SkinSyncI18n.T("app.name");
                Func<string> status = () => SkinSyncI18n.F("bridge.status_by_skinsync", SkinSync.Version);

                MethodInfo fn3 = type.GetMethod("Register", StaticFlags, null, new[] { typeof(Func<string>), typeof(Action), typeof(Func<string>) }, null);
                if (fn3 != null)
                {
                    fn3.Invoke(null, new object[] { title, draw, status });
                    return true;
                }

                MethodInfo fn = type.GetMethod("Register", StaticFlags, null, new[] { typeof(Func<string>), typeof(Action) }, null);
                if (fn == null) return false;
                fn.Invoke(null, new object[] { title, draw });
                return true;
            }
            catch (Exception ex)
            {
                ModLog.Warning("[SaveManagerTabBridge] " + ex.Message);
                return false;
            }
        }

        internal static int GetEffectiveUiScalePercent(SkinSyncSettings settings)
        {
            PropertyInfo prop = ResolveScaleProperty();
            if (prop != null)
            {
                try
                {
                    if (prop.GetValue(null, null) is int hostPct)
                    {
                        return Mathf.Clamp(hostPct, 75, 150);
                    }
                }
                catch (Exception ex)
                {
                    ModLog.Warning("[SaveManagerTabBridge] Read UiScalePercent failed: " + ex.Message);
                }
            }

            int localPct = settings?.UiScalePercent != null ? settings.UiScalePercent.Value : 100;
            return Mathf.Clamp(localPct, 75, 150);
        }

        internal static void SetEffectiveUiScalePercent(SkinSyncSettings settings, int percent)
        {
            int clamped = Mathf.Clamp(percent, 75, 150);
            PropertyInfo prop = ResolveScaleProperty();
            if (prop != null && prop.CanWrite)
            {
                try
                {
                    prop.SetValue(null, clamped, null);
                    return;
                }
                catch (Exception ex)
                {
                    ModLog.Warning("[SaveManagerTabBridge] Write UiScalePercent failed: " + ex.Message);
                }
            }

            settings?.SetUiScalePercent(clamped);
        }

        // 惰性反射宿主公开静态属性，确保在未安装 CuSaveManager 时零硬依赖降级回本地配置
        private static PropertyInfo ResolveScaleProperty()
        {
            if (_scalePropResolved) return _uiScaleProp;
            if (Chainloader.PluginInfos == null) return null;
            _scalePropResolved = true;
            try
            {
                if (!Chainloader.PluginInfos.TryGetValue(SaveManagerGuid, out var info))
                    return null;

                Type type = info?.Instance?.GetType().Assembly.GetType(RegistryTypeName, throwOnError: false);
                if (type == null) return null;

                PropertyInfo prop = type.GetProperty("UiScalePercent", BindingFlags.Public | BindingFlags.Static);
                if (prop != null && prop.PropertyType == typeof(int) && prop.CanRead)
                {
                    _uiScaleProp = prop;
                }
            }
            catch (Exception ex)
            {
                ModLog.Warning("[SaveManagerTabBridge] Resolve UiScalePercent failed: " + ex.Message);
            }
            return _uiScaleProp;
        }
    }
}
