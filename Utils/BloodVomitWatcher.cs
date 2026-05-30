using System.Collections.Generic;
using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>
    /// 监视 chara 上 limbs[0] 子节点新生成的 vomit / vomitBlood 粒子，按当前皮肤的 blood.json 染色。
    /// 为什么 watcher 而非 patch：游戏 Vomiter.DoBloodVomit 是 IEnumerator 内 Object.Instantiate，
    /// patch 不易精确命中；watcher 跟踪 limb 子节点变化，新粒子加入 0.25s 内必被染色。
    /// </summary>
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
        }

        private void Update()
        {
            if (!BloodRenderConfig.Enabled) return;
            if (_body == null || string.IsNullOrEmpty(_character)) return;
            if (Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + TickIntervalSec;

            // 扫 limbs[0]（UpTorso 或类似根 limb）的所有子节点 ParticleSystem——vomitBloodParticle / vomitParticle 都挂在此。
            if (_body.limbs == null || _body.limbs.Length == 0) return;
            var root = _body.limbs[0];
            if (root == null) return;
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps == null) continue;
                int id = ps.GetInstanceID();
                if (_processed.Contains(id)) continue;
                // 仅处理名字含 vomit / blood 的（粒子 GO 名继承自 prefab：vomitParticle / vomitBloodParticle）。
                string n = ps.gameObject.name ?? "";
                if (!ContainsAny(n, "vomit", "Vomit", "blood", "Blood")) continue;
                // 排除 Limb.Awake 已染色过的 bleedPart / waterBleedPart——它们的 GameObject 名通常不含 vomit。
                BloodAttacher.RecolorParticleByCharacter(ps, _character);
                _processed.Add(id);
            }
        }

        private static bool ContainsAny(string s, params string[] needles)
        {
            foreach (var n in needles) if (s.IndexOf(n, System.StringComparison.Ordinal) >= 0) return true;
            return false;
        }
    }
}
