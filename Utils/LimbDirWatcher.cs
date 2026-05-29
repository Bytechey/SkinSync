using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>每帧 poll body.isRight，变化时触发 SkinApplier.RefreshSidedSprites 重选 R_/无前缀 sprite。</summary>
    [DisallowMultipleComponent]
    public class LimbDirWatcher : MonoBehaviour
    {
        private Body _body;
        private bool _lastIsRight;
        private bool _initialized;

        private void Awake()
        {
            _body = GetComponentInChildren<Body>(true);
        }

        private void Update()
        {
            if (_body == null)
            {
                _body = GetComponentInChildren<Body>(true);
                if (_body == null) return;
            }
            bool cur = _body.isRight;
            if (!_initialized)
            {
                _lastIsRight = cur;
                _initialized = true;
                return;
            }
            if (cur == _lastIsRight) return;
            _lastIsRight = cur;
            SkinApplier.RefreshSidedSprites(gameObject, cur);
        }
    }
}
