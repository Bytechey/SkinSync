using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.UI;

namespace SkinSyncMod
{
    /// <summary>
    /// 把当前场景所有 GraphicRaycaster.enabled 暂存并设为 false，阻止下层 uGUI 穿透接收点击。
    /// EnforceBlocked 每帧重设以抵消其他脚本对 GraphicRaycaster.enabled 的复位。
    /// 与 SaveManager 同款实现，复制一份避免跨 mod 依赖。
    /// </summary>
    internal static class UiBlocker
    {
        private static readonly List<(GraphicRaycaster gr, bool wasEnabled)> _disabled
            = new List<(GraphicRaycaster, bool)>();

        private static bool _isBlocking;

        internal static bool IsBlocking => _isBlocking;

        internal static void Block(ManualLogSource log)
        {
            try
            {
                _disabled.Clear();
                var all = UnityEngine.Object.FindObjectsOfType<GraphicRaycaster>(includeInactive: true);
                foreach (var gr in all)
                {
                    if (gr == null) continue;
                    _disabled.Add((gr, gr.enabled));
                    gr.enabled = false;
                }
                _isBlocking = true;
            }
            catch (System.Exception ex)
            {
                log?.LogWarning($"[SkinSync] UiBlocker.Block 失败：{ex.Message}");
            }
        }

        internal static void EnforceBlocked(ManualLogSource log)
        {
            if (!_isBlocking) return;
            try
            {
                foreach (var (gr, _) in _disabled)
                {
                    if (gr == null) continue;
                    if (gr.enabled) gr.enabled = false;
                }
            }
            catch { }
        }

        internal static void Unblock(ManualLogSource log)
        {
            try
            {
                foreach (var (gr, was) in _disabled)
                {
                    if (gr != null) gr.enabled = was;
                }
                _disabled.Clear();
                _isBlocking = false;
            }
            catch (System.Exception ex)
            {
                log?.LogWarning($"[SkinSync] UiBlocker.Unblock 失败：{ex.Message}");
            }
        }
    }
}
