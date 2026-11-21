using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SkinSyncMod
{
    // 独立模式下向主菜单克隆注入入口按钮，并保持挂载层级位于全屏弹窗之下
    internal static class MenuButtonInjector
    {
        private const string InjectedName = "SkinSync_MenuButton";
        private const string MainMenuScene = "PreGen";

        private static Action _onClick;
        private static GameObject _injectedButton;

        internal static bool Active { get; private set; }
        internal static RectTransform InjectedRect;

        internal static void Setup(Action onClick)
        {
            _onClick = onClick;
            Active = onClick != null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (!Active) return;
            SceneManager.sceneLoaded += OnSceneLoaded;
            if (PreRunScript.instance != null)
            {
                OnPreRunScriptStarted(PreRunScript.instance);
            }
        }

        internal static void Dispose()
        {
            try { SceneManager.sceneLoaded -= OnSceneLoaded; } catch { }
            if (_injectedButton != null)
            {
                try { UnityEngine.Object.Destroy(_injectedButton); } catch { }
            }
            Active = false;
            _injectedButton = null;
            InjectedRect = null;
            _onClick = null;
        }

        internal static void RefreshLabel()
        {
            if (_injectedButton == null) return;
            ReplaceAllTexts(_injectedButton, SkinSyncI18n.T("app.menu_button"));
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!Active || scene.name != MainMenuScene || PreRunScript.instance == null) return;
            OnPreRunScriptStarted(PreRunScript.instance);
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

            // loadButton 位于默认隐藏的 runSettingsScreen 下，必须直接挂到 PreRunScript 根节点
            var parent = pre.transform;
            for (int i = 0; i < parent.childCount; i++)
            {
                var existing = parent.GetChild(i);
                if (existing.name == InjectedName)
                {
                    _injectedButton = existing.gameObject;
                    InjectedRect = existing as RectTransform;
                    RefreshLabel();
                    return;
                }
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
            clone.AddComponent<MenuButtonLabelSync>();

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

            // 插入在主菜单全屏遮罩节点之前，避免遮挡开场警告、新局设置与制作人员页
            PlaceBeforeOverlayScreens(pre, parent, clone.transform);
            _injectedButton = clone;
            InjectedRect = rt;

            ModLog.Info($"主菜单按钮已注入：parent={parent.name} sibling={clone.transform.GetSiblingIndex()}");
        }

        private static void PlaceBeforeOverlayScreens(PreRunScript pre, Transform parent, Transform buttonTransform)
        {
            int minSibling = int.MaxValue;
            ConsiderSibling(pre.creditsScreen != null ? pre.creditsScreen.transform : null, parent, buttonTransform, ref minSibling);
            ConsiderSibling(pre.runSettingsScreen != null ? pre.runSettingsScreen.transform : null, parent, buttonTransform, ref minSibling);
            ConsiderSibling(pre.contentWarning != null ? pre.contentWarning.transform : null, parent, buttonTransform, ref minSibling);
            ConsiderSibling(pre.cover != null ? pre.cover.transform : null, parent, buttonTransform, ref minSibling);
            ConsiderSibling(pre.tutorialWarning != null ? pre.tutorialWarning.transform : null, parent, buttonTransform, ref minSibling);
            ConsiderSibling(parent.Find("Credits"), parent, buttonTransform, ref minSibling);

            if (minSibling < int.MaxValue) buttonTransform.SetSiblingIndex(minSibling);
            else buttonTransform.SetAsLastSibling();
        }

        private static void ConsiderSibling(Transform candidate, Transform parent, Transform self, ref int minSibling)
        {
            if (candidate == null || candidate == self || candidate.parent != parent) return;
            int idx = candidate.GetSiblingIndex();
            if (idx < minSibling) minSibling = idx;
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
            Transform primaryTextChild = null;
            for (int i = 0; i < rootTransform.childCount; i++)
            {
                var child = rootTransform.GetChild(i);
                if (HasTextComponent(child))
                {
                    primaryTextChild = child;
                    break;
                }
            }

            for (int i = rootTransform.childCount - 1; i >= 0; i--)
            {
                var child = rootTransform.GetChild(i);
                if (child != primaryTextChild)
                {
                    UnityEngine.Object.Destroy(child.gameObject);
                }
            }
        }

        private static bool HasTextComponent(Transform root)
        {
            if (root.GetComponentInChildren<Text>(true) != null) return true;
            var allMb = root.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < allMb.Length; i++)
            {
                var mb = allMb[i];
                var fn = mb != null ? mb.GetType().FullName : null;
                if (fn != null && fn.StartsWith("TMPro.TextMesh", StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        internal static void ReplaceAllTexts(GameObject clone, string newText)
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
                if (type.FullName == null || !type.FullName.StartsWith("TMPro.TextMesh", StringComparison.Ordinal)) continue;
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

        private sealed class MenuButtonLabelSync : MonoBehaviour
        {
            private string _appliedText;
            private float _nextCheckAt;

            private void OnEnable() => SyncNow();

            private void Update()
            {
                if (Time.unscaledTime < _nextCheckAt) return;
                _nextCheckAt = Time.unscaledTime + 0.5f;
                SyncNow();
            }

            private void SyncNow()
            {
                string expected = SkinSyncI18n.T("app.menu_button");
                if (expected == _appliedText) return;
                _appliedText = expected;
                ReplaceAllTexts(gameObject, expected);
            }
        }
    }

    [HarmonyPatch(typeof(PreRunScript), "Start")]
    internal static class SkinSyncPreRunScriptStartPatch
    {
        private static void Postfix(PreRunScript __instance)
        {
            if (!MenuButtonInjector.Active) return;
            MenuButtonInjector.OnPreRunScriptStarted(__instance);
        }
    }
}
