using System;
using System.Collections;
using BepInEx.Bootstrap;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace SkinSyncMod.Patches
{
    /// <summary>启动后检查 GitHub 最新版本，并在主菜单左上角绘制黑白框可关闭的新版本跳转提示。</summary>
    [DisallowMultipleComponent]
    public class UpdateChecker : MonoBehaviour
    {
        private const string ApiUrl = "https://api.github.com/repos/Bytechey/SkinSync/releases/latest";
        private const string ReleasesUrl = "https://github.com/Bytechey/SkinSync/releases";
        private const int UpdateNoticeSlot = 3;
        private const float NoticeRowStride = 44f;
        private const float NoticeHeight = 38f;

        private static bool _checked;
        private static string _currentVersion = "";
        private static string _latestTag = "";
        private static bool _updateAvailable;
        private static bool _dismissed;

        // OnGUI 无外层 BlackWhiteSkin.Push，独立维护九宫格黑白框纹理与样式并在销毁时释放
        private Texture2D _bgNormal;
        private Texture2D _bgHover;
        private Texture2D _bgActive;
        private GUIStyle _noticeStyle;
        private GUIStyle _closeStyle;
        private readonly GUIContent _measureContent = new GUIContent();

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

        private void OnDestroy()
        {
            DestroyTexture(ref _bgNormal);
            DestroyTexture(ref _bgHover);
            DestroyTexture(ref _bgActive);
            _noticeStyle = null;
            _closeStyle = null;
        }

        private void TryStartCheck()
        {
            if (_checked || !Enabled) return;

            try
            {
                _currentVersion = SkinSync.Version;
                if (string.IsNullOrEmpty(_currentVersion)
                    && Chainloader.PluginInfos.TryGetValue(SkinSync.PluginGuid, out var info)
                    && info?.Metadata?.Version != null)
                {
                    _currentVersion = info.Metadata.Version.ToString();
                }
            }
            catch (Exception ex)
            {
                ModLog.Warning("UpdateChecker init failed: " + ex.Message);
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
                    ModLog.Info("Update check failed: " + www.error);
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
                    ModLog.Warning($"Update available: {_currentVersion} -> {_latestTag}");
                }
                else
                {
                    ModLog.Info($"Up to date. Current: {_currentVersion}, Latest: {_latestTag}");
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

        private void EnsureStyles()
        {
            if (_noticeStyle != null && _bgNormal != null) return;

            _bgNormal = MakeBorderedTex(0, 0, 0, 150, 255, 255, 255, 220, 2);
            _bgHover = MakeBorderedTex(60, 60, 60, 190, 255, 255, 255, 255, 2);
            _bgActive = MakeBorderedTex(90, 90, 90, 210, 255, 255, 255, 255, 2);

            _noticeStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                richText = false,
                wordWrap = false,
                clipping = TextClipping.Overflow,
                border = new RectOffset(2, 2, 2, 2),
                padding = new RectOffset(14, 14, 4, 4),
                margin = new RectOffset(0, 0, 0, 0),
            };
            ApplyStates(_noticeStyle, _bgNormal, _bgHover, _bgActive);

            _closeStyle = new GUIStyle(_noticeStyle)
            {
                fontSize = 17,
                padding = new RectOffset(10, 10, 4, 4),
            };
            ApplyStates(_closeStyle, _bgNormal, _bgHover, _bgActive);
        }

        private static void ApplyStates(GUIStyle style, Texture2D normal, Texture2D hover, Texture2D active)
        {
            style.normal.background = normal;
            style.onNormal.background = normal;
            style.hover.background = hover;
            style.onHover.background = hover;
            style.active.background = active;
            style.onActive.background = active;
            style.focused.background = normal;
            style.onFocused.background = normal;

            style.normal.textColor = Color.white;
            style.onNormal.textColor = Color.white;
            style.hover.textColor = Color.white;
            style.onHover.textColor = Color.white;
            style.active.textColor = Color.white;
            style.onActive.textColor = Color.white;
            style.focused.textColor = Color.white;
            style.onFocused.textColor = Color.white;
        }

        private static Texture2D MakeBorderedTex(byte fr, byte fg, byte fb, byte fa,
            byte br, byte bg, byte bb, byte ba, int borderPx)
        {
            int size = borderPx * 2 + 4;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            var fill = new Color32(fr, fg, fb, fa);
            var edge = new Color32(br, bg, bb, ba);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool isEdge = x < borderPx || x >= size - borderPx || y < borderPx || y >= size - borderPx;
                    pixels[y * size + x] = isEdge ? edge : fill;
                }
            }
            t.SetPixels32(pixels);
            t.Apply();
            return t;
        }

        private static void DestroyTexture(ref Texture2D tex)
        {
            if (tex != null)
            {
                try { UnityEngine.Object.Destroy(tex); } catch { }
                tex = null;
            }
        }

        private void OnGUI()
        {
            if (!_updateAvailable || !Enabled || _dismissed) return;
            if (!string.Equals(SceneManager.GetActiveScene().name, "PreGen", StringComparison.Ordinal)) return;

            EnsureStyles();
            string text = SkinSyncI18n.F("update.notice", _latestTag);
            float x = 32f;
            float y = Screen.height * 0.12f + UpdateNoticeSlot * NoticeRowStride;

            _measureContent.text = text;
            float width = Mathf.Ceil(_noticeStyle.CalcSize(_measureContent).x) + 8f;
            var rect = new Rect(x, y, width, NoticeHeight);
            if (GUI.Button(rect, text, _noticeStyle))
            {
                GUIUtility.keyboardControl = 0;
                try { Application.OpenURL(ReleasesUrl); } catch { }
            }

            const string closeLabel = "[×]";
            _measureContent.text = closeLabel;
            float closeWidth = Mathf.Max(42f, Mathf.Ceil(_closeStyle.CalcSize(_measureContent).x) + 8f);
            var closeRect = new Rect(rect.xMax + 6f, y, closeWidth, NoticeHeight);
            if (GUI.Button(closeRect, closeLabel, _closeStyle))
            {
                GUIUtility.keyboardControl = 0;
                _dismissed = true;
            }
        }
    }
}
