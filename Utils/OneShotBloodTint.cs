using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>在 LateUpdate 对墙面血迹 SpriteRenderer 执行一次着色后自动销毁自身组件。</summary>
    [DisallowMultipleComponent]
    public class OneShotBloodTint : MonoBehaviour
    {
        private Color _rgb;
        private SpriteRenderer _sr;

        public void Configure(SpriteRenderer sr, Color rgb)
        {
            _sr = sr;
            _rgb = rgb;
        }

        private void LateUpdate()
        {
            if (_sr != null)
            {
                _sr.color = new Color(_rgb.r, _rgb.g, _rgb.b, _sr.color.a);
            }
            Destroy(this);
        }
    }
}
