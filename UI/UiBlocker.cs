using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SkinSyncMod
{
    /// <summary>暂存并禁用场景中的 GraphicRaycaster 以阻止下层 uGUI 接收点击。</summary>
    internal static class UiBlocker
    {
        private static readonly List<(GraphicRaycaster gr, bool wasEnabled)> _disabled
            = new List<(GraphicRaycaster, bool)>();

        private static bool _isBlocking;

        internal static bool IsBlocking => _isBlocking;

        internal static void Block()
        {
            if (_isBlocking) return;
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
                ModLog.Warning($"UiBlocker.Block 失败：{ex.Message}");
            }
        }

        internal static void EnforceBlocked()
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

        internal static void Unblock()
        {
            try
            {
                foreach (var (gr, was) in _disabled)
                {
                    if (gr != null) gr.enabled = was;
                }
            }
            catch (System.Exception ex)
            {
                ModLog.Warning($"UiBlocker.Unblock 失败：{ex.Message}");
            }
            finally
            {
                _disabled.Clear();
                _isBlocking = false;
            }
        }
    }
}
