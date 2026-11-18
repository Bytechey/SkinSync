using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SkinSyncMod
{
    /// <summary>向主菜单注入打开 SkinSync 面板的入口按钮。</summary>
    internal static class MenuButtonInjector
    {
        private const string InjectedName = "SkinSync_MenuButton";

        private static Action _onClick;
        internal static RectTransform InjectedRect;

        internal static void Setup(Action onClick)
        {
            _onClick = onClick;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        internal static void Dispose()
        {
            try { SceneManager.sceneLoaded -= OnSceneLoaded; } catch { }
            InjectedRect = null;
            _onClick = null;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != "PreGen") return;
            if (PreRunScript.instance == null) return;
            try { InjectOnce(PreRunScript.instance); }
            catch (Exception ex) { ModLog.Warning("sceneLoaded 注入失败：" + ex.Message); }
        }

        internal static void OnPreRunScriptStarted(PreRunScript pre)
        {
            try { InjectOnce(pre); }
            catch (Exception ex) { ModLog.Warning("主菜单按钮注入失败：" + ex.Message); }
        }

        private static void InjectOnce(PreRunScript pre)
        {
            if (_onClick == null) return;
            if (pre == null || pre.loadButton == null)
            {
                ModLog.Warning("PreRunScript / loadButton 为空，跳过注入。");
                return;
            }

            var parent = pre.transform;

            for (int i = 0; i < parent.childCount; i++)
            {
                if (parent.GetChild(i).name == InjectedName) return;
            }

            var clone = UnityEngine.Object.Instantiate(pre.loadButton.gameObject, parent, false);
            clone.name = InjectedName;
            clone.SetActive(true);

            var btn = clone.GetComponent<Button>();
            if (btn != null)
            {
                int n = btn.onClick.GetPersistentEventCount();
                for (int i = 0; i < n; i++)
                {
                    btn.onClick.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.Off);
                }
                btn.onClick.RemoveAllListeners();
                btn.interactable = true;
                btn.onClick.AddListener(InvokeOnClick);
            }

            DestroyLocalizers(clone);
            CleanupLoadButtonChildren(clone);
            ReplaceAllTexts(clone, SkinSyncI18n.T("app.menu_button"));

            var rt = clone.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = new Vector2(1f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(1f, 1f);
                rt.sizeDelta = new Vector2(280f, 96f);
                rt.anchoredPosition = new Vector2(-24f, -240f);
                rt.localScale = Vector3.one;
            }
            clone.transform.SetAsLastSibling();
            InjectedRect = rt;

            ModLog.Info($"主菜单按钮已注入：parent={parent.name}");
        }

        private static void InvokeOnClick()
        {
            try { _onClick?.Invoke(); }
            catch (Exception ex) { ModLog.Warning("按钮点击异常：" + ex.Message); }
        }

        private static void DestroyLocalizers(GameObject clone)
        {
            var components = clone.GetComponentsInChildren<MonoBehaviour>(includeInactive: true);
            foreach (var c in components)
            {
                if (c == null) continue;
                var name = c.GetType().Name;
                if (name == null) continue;
                if (name.IndexOf("Localiz", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("LocaleText", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Tooltip", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    UnityEngine.Object.Destroy(c);
                }
            }
        }

        private static void CleanupLoadButtonChildren(GameObject clone)
        {
            var rootTransform = clone.transform;
            for (int i = rootTransform.childCount - 1; i >= 0; i--)
            {
                var child = rootTransform.GetChild(i);
                bool hasText = child.GetComponentInChildren<Text>(true) != null;
                if (!hasText)
                {
                    var allMb = child.GetComponentsInChildren<MonoBehaviour>(true);
                    foreach (var mb in allMb)
                    {
                        if (mb == null) continue;
                        var fn = mb.GetType().FullName;
                        if (fn != null && fn.StartsWith("TMPro.TextMesh"))
                        {
                            hasText = true;
                            break;
                        }
                    }
                }
                if (!hasText)
                {
                    UnityEngine.Object.Destroy(child.gameObject);
                }
            }
        }

        private static void ReplaceAllTexts(GameObject clone, string newText)
        {
            bool first = true;

            var legacyTexts = clone.GetComponentsInChildren<Text>(includeInactive: true);
            foreach (var t in legacyTexts)
            {
                if (t == null) continue;
                t.text = first ? newText : "";
                first = false;
            }

            var allMb = clone.GetComponentsInChildren<MonoBehaviour>(includeInactive: true);
            foreach (var mb in allMb)
            {
                if (mb == null) continue;
                var type = mb.GetType();
                if (type.FullName == null || !type.FullName.StartsWith("TMPro.TextMesh")) continue;
                var prop = type.GetProperty("text", BindingFlags.Public | BindingFlags.Instance);
                if (prop == null || !prop.CanWrite) continue;
                try
                {
                    prop.SetValue(mb, first ? newText : "", null);
                    first = false;
                }
                catch { }
            }
        }
    }

    /// <summary>在 PreRunScript.Start 后触发主菜单入口按钮注入。</summary>
    [HarmonyPatch(typeof(PreRunScript), "Start")]
    internal static class SkinSyncPreRunScriptStartPatch
    {
        private static void Postfix(PreRunScript __instance)
        {
            MenuButtonInjector.OnPreRunScriptStarted(__instance);
        }
    }
}
