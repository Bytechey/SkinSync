using BepInEx.Bootstrap;
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace SkinSyncMod.Patches
{
    /// <summary>
    /// 启动时拉取 GitHub releases/latest 比对当前 mod 版本，发现新版即在屏幕右上角显示红字提示并提供打开 release 页的按钮。
    /// </summary>
    [DisallowMultipleComponent]
    public class UpdateChecker : MonoBehaviour
    {
        private const string PluginGuid = "com.Bytechey.skinsync";
        private const string ApiUrl = "https://api.github.com/repos/Bytechey/SkinSync/releases/latest";
        private const string ReleasesUrl = "https://github.com/Bytechey/SkinSync/releases";

        private static bool _checked;
        private static string _currentVersion = "";
        private static string _latestTag = "";
        private static bool _updateAvailable;

        private void Start()
        {
            if (_checked) return;
            _checked = true;

            try
            {
                if (Chainloader.PluginInfos.TryGetValue(PluginGuid, out var info))
                    _currentVersion = info.Metadata.Version.ToString();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[SkinSync] UpdateChecker init failed: " + ex.Message);
                return;
            }

            StartCoroutine(CheckForUpdates());
        }

        private IEnumerator CheckForUpdates()
        {
            using (UnityWebRequest www = UnityWebRequest.Get(ApiUrl))
            {
                www.SetRequestHeader("User-Agent", "SkinSyncMod");
                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning("[SkinSync] Update check failed: " + www.error);
                    yield break;
                }

                string json = www.downloadHandler.text;
                const string key = "\"tag_name\":\"";
                int idx = json.IndexOf(key, StringComparison.Ordinal);
                if (idx < 0) yield break;
                int start = idx + key.Length;
                int end = json.IndexOf('"', start);
                if (end <= start) yield break;
                _latestTag = json.Substring(start, end - start);

                if (IsNewer(_currentVersion, _latestTag))
                {
                    _updateAvailable = true;
                    Debug.LogWarning($"[SkinSync] Update available: {_currentVersion} -> {_latestTag}");
                }
                else
                {
                    Debug.Log($"[SkinSync] Up to date. Current: {_currentVersion}, Latest: {_latestTag}");
                }
            }
        }

        private static bool IsNewer(string current, string latest)
        {
            if (string.IsNullOrEmpty(current) || string.IsNullOrEmpty(latest)) return false;
            string cur = current.TrimStart('v', 'V').Trim();
            string lat = latest.TrimStart('v', 'V').Trim();
            return Version.TryParse(cur, out Version vc) && Version.TryParse(lat, out Version vl) && vl > vc;
        }

        private void OnGUI()
        {
            if (!_updateAvailable) return;

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 26,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperLeft,
                richText = false,
                normal = { textColor = new Color(1f, 0.3f, 0.3f) },
                hover = { textColor = new Color(1f, 0.55f, 0.55f) },
                active = { textColor = new Color(1f, 0.2f, 0.2f) },
            };

            string text = $"SkinSync 有新版本：{_latestTag}（点击此处打开 release 页）";
            float x = 32f;
            float y = Screen.height * 0.12f;
            Vector2 size = style.CalcSize(new GUIContent(text));
            var rect = new Rect(x, y, size.x + 8f, size.y + 4f);
            if (GUI.Button(rect, text, style))
                Application.OpenURL(ReleasesUrl);
        }
    }
}
