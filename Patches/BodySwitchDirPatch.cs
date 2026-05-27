using HarmonyLib;
using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>翻身后把 Body 子树所有 SpriteRenderer 的 sortingOrder 取负，让前后视觉与朝向同步。</summary>
    [HarmonyPatch(typeof(Body), nameof(Body.SwitchDir))]
    public static class BodySwitchDirPatch
    {
        static void Postfix(Body __instance)
        {
            if (__instance == null) return;
            var renderers = __instance.GetComponentsInChildren<SpriteRenderer>(true);
            foreach (var sr in renderers)
            {
                if (sr == null) continue;
                sr.sortingOrder = -sr.sortingOrder;
            }
        }
    }
}
