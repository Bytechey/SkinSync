using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace SkinSyncMod
{
    /// <summary>
    /// 为后挂的渲染器添加 URP 2D ShadowCaster2D，复用其轮廓投射阴影。
    /// </summary>
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
