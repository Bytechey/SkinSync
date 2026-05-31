using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>给墙血对象延迟到 LateUpdate 染色一次后自毁，绕过游戏在生成当帧把 color 覆盖为白色。</summary>
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
