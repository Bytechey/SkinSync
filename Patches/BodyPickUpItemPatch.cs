using HarmonyLib;
using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>新拿起的物品 sortingOrder 跟随当前朝向同步，朝左时取负与 limb 对齐。</summary>
    [HarmonyPatch(typeof(Body), nameof(Body.PickUpItem))]
    public static class BodyPickUpItemPatch
    {
        static void Postfix(Body __instance, Item item, int slot)
        {
            if (__instance == null || item == null) return;
            if (slot < 0 || __instance.slots == null || slot >= __instance.slots.Length) return;
            if (item.transform.parent != __instance.slots[slot].transform) return;
            var sr = item.GetComponent<SpriteRenderer>();
            if (sr == null) return;
            if (!__instance.isRight)
            {
                sr.sortingOrder = -sr.sortingOrder;
            }
        }
    }
}
