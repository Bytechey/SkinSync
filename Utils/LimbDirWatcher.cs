using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>监听角色左右朝向翻转并刷新其侧向部位与配件贴图。</summary>
    [DisallowMultipleComponent]
    public class LimbDirWatcher : MonoBehaviour
    {
        private Body _body;
        private bool _lastIsRight;
        private bool _initialized;

        internal void SyncDirection(Body body, bool isRight)
        {
            if (body != null) _body = body;
            _lastIsRight = isRight;
            _initialized = true;
        }

        private void Awake()
        {
            if (_body == null) _body = GetComponentInChildren<Body>(true);
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
