using System.Collections.Generic;
using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>
    /// 朝右时把 SpriteRenderer.sprite 切到 R_ 前缀的 sprite 并设 flipX 抵消父级镜像，缺失 R_ 版本则维持原图。
    /// </summary>
    [DisallowMultipleComponent]
    public class DirectionalSpriteSwitcher : MonoBehaviour
    {
        private const string Prefix = "R_";

        private SpriteRenderer _sr;
        private Body _body;
        private Dictionary<string, Sprite> _sprites;

        /// <summary>
        /// 注入或刷新 Body 引用与皮肤 sprite 字典；每次切皮肤都重调一次。
        /// </summary>
        public void Configure(Body body, Dictionary<string, Sprite> sprites)
        {
            _sr = GetComponent<SpriteRenderer>();
            _body = body;
            _sprites = sprites;
        }

        private void LateUpdate()
        {
            if (_sr == null || _body == null || _sprites == null) return;
            if (_sr.sprite == null) return;

            bool wantRight = _body.isRight;
            string current = _sr.sprite.name;
            string baseName = current.StartsWith(Prefix) ? current.Substring(Prefix.Length) : current;
            string targetName = wantRight ? Prefix + baseName : baseName;

            if (current != targetName && _sprites.TryGetValue(targetName, out Sprite target))
                _sr.sprite = target;

            _sr.flipX = wantRight && _sr.sprite.name.StartsWith(Prefix);
        }
    }
}
