using System;
using System.Collections.Generic;
using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>监听角色躯干下新生成的呕血粒子系统并按当前皮肤血液配置染色。</summary>
    public class BloodVomitWatcher : MonoBehaviour
    {
        private const float TickIntervalSec = 0.25f;
        private const int PruneThreshold = 64;

        // Unity Destroy 延迟至帧末生效，同帧还原再重挂时需用显式标记区分待销毁旧实例。
        internal bool IsDisposed { get; private set; }

        private float _nextTick;
        private Body _body;
        private string _character;
        private readonly HashSet<ParticleSystem> _processed = new HashSet<ParticleSystem>();

        internal void MarkDisposed()
        {
            IsDisposed = true;
            enabled = false;
            _body = null;
            _character = null;
            _processed.Clear();
        }

        public void Configure(Body body, string character)
        {
            _body = body;
            _character = character;
            _processed.Clear();
        }

        private void Update()
        {
            if (IsDisposed) return;
            if (!BloodRenderConfig.Enabled) return;
            if (_body == null || string.IsNullOrEmpty(_character)) return;
            if (Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + TickIntervalSec;

            if (_body.limbs == null || _body.limbs.Length == 0) return;
            var root = _body.limbs[0];
            if (root == null) return;

            bool addedAny = false;
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps == null || !_processed.Add(ps)) continue;
                addedAny = true;
                string n = ps.gameObject.name;
                // 常规流血与呕吐粒子同样记入 _processed 跳过后续字符串匹配，仅对呕血粒子执行染色。
                if (string.IsNullOrEmpty(n)
                    || n.IndexOf("vomit", StringComparison.OrdinalIgnoreCase) < 0
                    || n.IndexOf("blood", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                BloodAttacher.RecolorParticleByCharacter(ps, _character);
            }

            if (addedAny && _processed.Count > PruneThreshold)
            {
                _processed.RemoveWhere(p => p == null);
            }
        }
    }
}
