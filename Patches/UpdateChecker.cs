using System;
using System.Collections;
using BepInEx.Bootstrap;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace SkinSyncMod.Patches
{
    /// <summary>启动后检查 GitHub 最新版本并在发现新版本时绘制界面跳转提示。</summary>
    [DisallowMultipleComponent]
    public class UpdateChecker : MonoBehaviour
    {
        private const string PluginGuid = "com.Bytechey.skinsync";
        private const string ApiUrl = "https://api.github.com/repos/Bytechey/SkinSync/releases/latest";
        private const string ReleasesUrl = "https://github.com/Bytechey/SkinSync/releases";
        private const int UpdateNoticeSlot = 3;

        private static bool _checked;
        private static string _currentVersion = "";
        private static string _latestTag = "";
        private static bool _updateAvailable;
        private static GUIStyle _noticeStyle;

        internal static bool Enabled = true;

        private void Start()
        {
            TryStartCheck();
        }

        private void Update()
        {
            if (!_checked && Enabled)
            {
                TryStartCheck();
            }
        }

        private void TryStartCheck()
        {
            if (_checked || !Enabled) return;

            try
            {
                if (Chainloader.PluginInfos.TryGetValue(PluginGuid, out var info))
                    _currentVersion = info.Metadata.Version.ToString();
            }
            catch (Exception ex)
            {
                SkinSyncMod.ModLog.Warning("UpdateChecker init failed: " + ex.Message);
                _checked = true;
                return;
            }

            _checked = true;
            StartCoroutine(CheckForUpdates());
        }

        private IEnumerator CheckForUpdates()
        {
            using (UnityWebRequest www = UnityWebRequest.Get(ApiUrl))
            {
                www.timeout = 10;
                www.SetRequestHeader("User-Agent", "SkinSyncMod");
                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    SkinSyncMod.ModLog.Warning("Update check failed: " + www.error);
                    yield break;
                }

                try
                {
                    var root = JObject.Parse(www.downloadHandler.text);
                    _latestTag = root["tag_name"]?.ToString() ?? "";
                }
                catch
                {
                    yield break;
                }

                if (string.IsNullOrEmpty(_latestTag)) yield break;

                if (IsNewer(_currentVersion, _latestTag))
                {
                    _updateAvailable = true;
                    SkinSyncMod.ModLog.Warning($"Update available: {_currentVersion} -> {_latestTag}");
                }
                else
                {
                    SkinSyncMod.ModLog.Info($"Up to date. Current: {_currentVersion}, Latest: {_latestTag}");
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

        private static GUIStyle EnsureStyle()
        {
            if (_noticeStyle != null) return _noticeStyle;
            _noticeStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 26,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperLeft,
                richText = false,
                normal = { textColor = new Color(1f, 0.3f, 0.3f) },
                hover = { textColor = new Color(1f, 0.55f, 0.55f) },
                active = { textColor = new Color(1f, 0.2f, 0.2f) },
            };
            return _noticeStyle;
        }

        private void OnGUI()
        {
            if (!_updateAvailable || !Enabled) return;

            var style = EnsureStyle();
            string text = SkinSyncI18n.F("update.notice", _latestTag);
            float x = 32f;
            float y = Screen.height * 0.12f + UpdateNoticeSlot * 40f;
            Vector2 size = style.CalcSize(new GUIContent(text));
            var rect = new Rect(x, y, size.x + 8f, size.y + 4f);
            if (GUI.Button(rect, text, style))
            {
                try { Application.OpenURL(ReleasesUrl); } catch { }
            }
        }
    }
}
