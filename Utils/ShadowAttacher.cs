using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace SkinSyncMod
{
    /// <summary>为动态创建的贴图挂载点附加 URP ShadowCaster2D 组件。</summary>
    public static class ShadowAttacher
    {
        public static void Ensure(GameObject target)
        {
            if (target == null) return;
            var sc = target.GetComponent<ShadowCaster2D>();
            if (sc == null) sc = target.AddComponent<ShadowCaster2D>();
            sc.useRendererSilhouette = true;
            sc.castsShadows = true;
            sc.selfShadows = false;
        }
    }
}
