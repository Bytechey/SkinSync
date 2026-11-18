using System;
using System.Collections.Generic;
using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>监听角色躯干下新生成的呕血粒子系统并按当前皮肤血液配置染色。</summary>
    [DisallowMultipleComponent]
    public class BloodVomitWatcher : MonoBehaviour
    {
        private const float TickIntervalSec = 0.25f;
        private float _nextTick;
        private Body _body;
        private string _character;
        private readonly HashSet<int> _processed = new HashSet<int>();

        public void Configure(Body body, string character)
        {
            _body = body;
            _character = character;
            _processed.Clear();
        }

        private void Update()
        {
            if (!BloodRenderConfig.Enabled) return;
            if (_body == null || string.IsNullOrEmpty(_character)) return;
            if (Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + TickIntervalSec;

            if (_body.limbs == null || _body.limbs.Length == 0) return;
            var root = _body.limbs[0];
            if (root == null) return;
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps == null) continue;
                int id = ps.GetInstanceID();
                if (_processed.Contains(id)) continue;
                string n = ps.gameObject.name ?? "";
                // 仅匹配呕血粒子（vomitBloodParticle），排除普通呕吐物（vomitParticle）与常驻流血粒子。
                if (n.IndexOf("vomit", StringComparison.OrdinalIgnoreCase) < 0
                    || n.IndexOf("blood", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                BloodAttacher.RecolorParticleByCharacter(ps, _character);
                _processed.Add(id);
            }
        }
    }
}
