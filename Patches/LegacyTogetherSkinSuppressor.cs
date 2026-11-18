using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace SkinSyncMod.Patches
{
    /// <summary>检测并压制 Steam 原版 CasualtiesMP (v5.5.x) 内置的全局命名空间皮肤替换子系统，消除贴图覆盖、热键与双按钮冲突。</summary>
    internal static class LegacyTogetherSkinSuppressor
    {
        private const string TogetherAssemblyName = "CasualtiesMP";
        private const string LegacyHarmonyId = "com.yourname.spritereplacer";
        private const BindingFlags AllFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        private struct PatchedEntry
        {
            public MethodBase Target;
            public MethodInfo Patch;
        }

        private static bool _suppressed;
        private static bool _noLegacyDetected;
        private static readonly List<PatchedEntry> _shortCircuitedMethods = new List<PatchedEntry>();
        private static readonly MethodInfo _skipPrefixMethod = typeof(LegacyTogetherSkinSuppressor)
            .GetMethod(nameof(SkipPrefix), BindingFlags.NonPublic | BindingFlags.Static);

        /// <summary>检测宿主是否包含原版内置 SpriteReplacerPlugin 并执行一次性压制；未加载 Together 时返回 false 供后续重试。</summary>
        internal static bool TrySuppress(Harmony harmony)
        {
            if (_suppressed || _noLegacyDetected) return true;

            Assembly togetherAsm = FindTogetherAssembly();
            if (togetherAsm == null) return false;

            // 原版 v5.5.3.0 内置皮肤类全部位于全局根命名空间；普通版 main 与 Feature 版已移除该子系统。
            Type pluginType = togetherAsm.GetType("SpriteReplacerPlugin", throwOnError: false)
                ?? togetherAsm.GetType("Together.Skins.SpriteReplacerPlugin", throwOnError: false);
            if (pluginType == null)
            {
                _noLegacyDetected = true;
                return true;
            }

            try
            {
                int restoredReplacers = RestoreAndDestroyExistingReplacers(togetherAsm, pluginType);

                Harmony.UnpatchID(LegacyHarmonyId);

                int stoppedListeners = DisablePluginAndInputListeners(pluginType);
                DetachSceneCallbacks(pluginType);
                HideLegacySkinMenuUi(togetherAsm);

                int patchedCount = InstallShortCircuitPatches(harmony, togetherAsm, pluginType);

                _suppressed = true;
                ModLog.Info($"LegacyTogetherSkinSuppressor: 已压制原版内置皮肤子系统（还原组件 {restoredReplacers} 个，销毁监听器 {stoppedListeners} 个，短路入口 {patchedCount} 处）。");
            }
            catch (Exception ex)
            {
                ModLog.Warning("LegacyTogetherSkinSuppressor: 压制原版内置皮肤子系统时出现异常：" + ex.Message);
            }

            return true;
        }

        /// <summary>退出时卸载挂载在原版内置皮肤类上的短路补丁。</summary>
        internal static void Dispose(Harmony harmony)
        {
            if (harmony != null)
            {
                for (int i = 0; i < _shortCircuitedMethods.Count; i++)
                {
                    PatchedEntry entry = _shortCircuitedMethods[i];
                    try { harmony.Unpatch(entry.Target, entry.Patch); }
                    catch { }
                }
            }
            _shortCircuitedMethods.Clear();
            _suppressed = false;
            _noLegacyDetected = false;
        }

        /// <summary>识别原版 SpriteReplacerPlugin 在 CustomSprites 根下自动创建的 Exp/Milky/Dune 空壳物种容器目录。</summary>
        internal static bool IsLegacyContainerDir(string customSpritesRoot, string dirName)
        {
            if (!string.Equals(dirName, "Exp", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(dirName, "Milky", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(dirName, "Dune", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string fullDir = Path.Combine(customSpritesRoot, dirName);
            try
            {
                // 若顶层直接存在 PNG 贴图或皮肤 JSON 配置，判定为用户自建的同名合法皮肤，绝不误排除。
                if (Directory.GetFiles(fullDir, "*.png", SearchOption.TopDirectoryOnly).Length > 0
                    || Directory.GetFiles(fullDir, "*.json", SearchOption.TopDirectoryOnly).Length > 0)
                {
                    return false;
                }

                string[] subDirs = Directory.GetDirectories(fullDir, "*", SearchOption.TopDirectoryOnly);
                for (int i = 0; i < subDirs.Length; i++)
                {
                    if (!IsLegacySlotOrBackupSubdir(Path.GetFileName(subDirs[i])))
                        return false;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsLegacySlotOrBackupSubdir(string name)
        {
            if (string.IsNullOrEmpty(name)) return true;
            if (string.Equals(name, "default", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "backups", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (name.StartsWith("slot", StringComparison.OrdinalIgnoreCase) && name.Length == 5)
            {
                char c = name[4];
                return c >= '0' && c <= '9';
            }
            if (name.StartsWith("st", StringComparison.OrdinalIgnoreCase) && name.Length == 3)
            {
                char c = name[2];
                return c >= '0' && c <= '9';
            }
            return false;
        }

        private static bool SkipPrefix() => false;

        private static Assembly FindTogetherAssembly()
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Assembly asm = assemblies[i];
                if (asm != null && string.Equals(asm.GetName().Name, TogetherAssemblyName, StringComparison.Ordinal))
                    return asm;
            }
            return null;
        }

        private static int RestoreAndDestroyExistingReplacers(Assembly togetherAsm, Type pluginType)
        {
            int count = 0;
            Type replacerType = togetherAsm.GetType("SkinSpriteReplacer", throwOnError: false);
            if (replacerType != null)
            {
                MethodInfo restoreMethod = replacerType.GetMethod("Restore", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                PropertyInfo activeProp = replacerType.GetProperty("Active", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                UnityEngine.Object[] replacers = Resources.FindObjectsOfTypeAll(replacerType);
                for (int i = 0; i < replacers.Length; i++)
                {
                    Component comp = replacers[i] as Component;
                    if (comp == null) continue;
                    try { restoreMethod?.Invoke(comp, null); } catch { }
                    try { activeProp?.SetValue(comp, false, null); } catch { }
                    UnityEngine.Object.Destroy(comp);
                    count++;
                }
            }

            // 清空原版在 Awake 预载入的槽位贴图字典，并将已篡改的面部组件还原回原版基线，防止污染 SkinSync 首次捕获的原始引用。
            try
            {
                if (pluginType.GetField("LoadedSprites", AllFlags)?.GetValue(null) is IDictionary loaded)
                    loaded.Clear();

                MethodInfo restoreFace = pluginType.GetMethod("RestoreFaceToStock", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(FacialExpression) }, null);
                if (restoreFace != null)
                {
                    FacialExpression[] faces = Resources.FindObjectsOfTypeAll<FacialExpression>();
                    for (int i = 0; i < faces.Length; i++)
                    {
                        if (faces[i] == null || !faces[i].gameObject.scene.IsValid()) continue;
                        try { restoreFace.Invoke(null, new object[] { faces[i] }); } catch { }
                    }
                }
            }
            catch { }

            return count;
        }

        private static int DisablePluginAndInputListeners(Type pluginType)
        {
            int count = 0;
            Type listenerType = pluginType.GetNestedType("InputListener", BindingFlags.Public | BindingFlags.NonPublic);

            // 原版 SpriteReplacer 与 SpriteReplacerInputListener 均标记了 HideFlags.HideAndDontSave，必须通过 Resources.FindObjectsOfTypeAll 检索。
            MonoBehaviour[] behaviours = Resources.FindObjectsOfTypeAll<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour mb = behaviours[i];
                if (mb == null) continue;
                Type t = mb.GetType();
                bool isPlugin = t == pluginType || string.Equals(t.FullName, "SpriteReplacerPlugin", StringComparison.Ordinal);
                bool isListener = (listenerType != null && t == listenerType)
                    || string.Equals(t.FullName, "SpriteReplacerPlugin+InputListener", StringComparison.Ordinal);
                if (!isPlugin && !isListener) continue;

                try { mb.StopAllCoroutines(); } catch { }
                mb.enabled = false;
                if (mb.gameObject != null
                    && (string.Equals(mb.gameObject.name, "SpriteReplacer", StringComparison.Ordinal)
                        || string.Equals(mb.gameObject.name, "SpriteReplacerInputListener", StringComparison.Ordinal)))
                {
                    UnityEngine.Object.Destroy(mb.gameObject);
                }
                else
                {
                    UnityEngine.Object.Destroy(mb);
                }
                count++;
            }

            return count;
        }

        private static void DetachSceneCallbacks(Type pluginType)
        {
            try
            {
                MethodInfo onLoaded = pluginType.GetMethod("OnSceneLoaded", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(Scene), typeof(LoadSceneMode) }, null);
                if (onLoaded != null)
                {
                    var del = (UnityAction<Scene, LoadSceneMode>)Delegate.CreateDelegate(typeof(UnityAction<Scene, LoadSceneMode>), onLoaded, throwOnBindFailure: false);
                    if (del != null) SceneManager.sceneLoaded -= del;
                }

                MethodInfo onUnloaded = pluginType.GetMethod("OnSceneUnloaded", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(Scene) }, null);
                if (onUnloaded != null)
                {
                    var del = (UnityAction<Scene>)Delegate.CreateDelegate(typeof(UnityAction<Scene>), onUnloaded, throwOnBindFailure: false);
                    if (del != null) SceneManager.sceneUnloaded -= del;
                }

                MethodInfo onChanged = pluginType.GetMethod("OnActiveSceneChanged", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(Scene), typeof(Scene) }, null);
                if (onChanged != null)
                {
                    var del = (UnityAction<Scene, Scene>)Delegate.CreateDelegate(typeof(UnityAction<Scene, Scene>), onChanged, throwOnBindFailure: false);
                    if (del != null) SceneManager.activeSceneChanged -= del;
                }
            }
            catch { }
        }

        private static void HideLegacySkinMenuUi(Assembly togetherAsm)
        {
            Type menuType = togetherAsm.GetType("SkinMenuUI", throwOnError: false);
            if (menuType == null) return;

            try
            {
                MethodInfo setVisible = menuType.GetMethod("SetVisible", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(bool) }, null);
                setVisible?.Invoke(null, new object[] { false });
            }
            catch { }

            // AlignMenuButtonBesideMp 会把 _menuButtonRt 从 _canvas 移挂至主菜单按钮父节点，须单独停用其 GameObject。
            try
            {
                if (menuType.GetField("_menuButtonRt", AllFlags)?.GetValue(null) is RectTransform btnRt && btnRt != null && btnRt.gameObject != null)
                    btnRt.gameObject.SetActive(false);
            }
            catch { }

            try
            {
                if (menuType.GetField("_canvas", AllFlags)?.GetValue(null) is GameObject canvasGo && canvasGo != null)
                    canvasGo.SetActive(false);
            }
            catch { }
        }

        private static int InstallShortCircuitPatches(Harmony harmony, Assembly togetherAsm, Type pluginType)
        {
            if (harmony == null || _skipPrefixMethod == null) return 0;
            int count = 0;

            Type woundType = togetherAsm.GetType("WoundViewIntegration", throwOnError: false);
            if (woundType != null)
            {
                if (TryShortCircuitStaticMethods(harmony, woundType, "Apply")) count++;
                if (TryShortCircuitStaticMethods(harmony, woundType, "RefreshForBody")) count++;
            }

            if (TryShortCircuitStaticMethods(harmony, pluginType, "ApplyToAll")) count++;

            Type mpSkinSyncType = togetherAsm.GetType("MpSkinSync", throwOnError: false);
            if (mpSkinSyncType != null)
            {
                if (TryShortCircuitStaticMethods(harmony, mpSkinSyncType, "OnBodyPartReady")) count++;
            }

            return count;
        }

        private static bool TryShortCircuitStaticMethods(Harmony harmony, Type targetType, string methodName)
        {
            if (targetType == null) return false;
            bool any = false;
            MethodInfo[] methods = targetType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (!string.Equals(method.Name, methodName, StringComparison.Ordinal)) continue;
                try
                {
                    harmony.Patch(method, prefix: new HarmonyMethod(_skipPrefixMethod));
                    _shortCircuitedMethods.Add(new PatchedEntry { Target = method, Patch = _skipPrefixMethod });
                    any = true;
                }
                catch (Exception ex)
                {
                    ModLog.Debug($"LegacyTogetherSkinSuppressor: 短路 {targetType.Name}.{methodName} 失败：{ex.Message}");
                }
            }
            return any;
        }
    }
}
