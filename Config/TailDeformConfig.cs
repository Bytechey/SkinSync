using System;
using System.Collections.Generic;
using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>单套尾巴物理形变参数快照。</summary>
    public struct TailDeformParams
    {
        public bool Enabled;
        public bool FrontGuard;
        public int Segments;
        public int ConstraintIters;
        public float Damping;
        public float SpeedDamping;
        public float Stiffness;
        public float GravityX;
        public float GravityY;
        public float WindFreq;
        public float WindAmp;
        public float SpeedDisturb;
        public float MaxBendDeg;
        public float AnchorFollow;
        public float Smoothness;
        public float FrontGuardMargin;
        public float MaxStep;
        public float MaxFixedDt;
    }

    /// <summary>管理本机尾巴物理形变参数及按角色隔离的远端玩家尾巴配置。</summary>
    public static class TailDeformConfig
    {
        public static int Segments = 10;
        public static int ConstraintIters = 6;

        public static float Damping = 0.45f;
        public static float SpeedDamping = 0.05f;
        public static float Stiffness = 1.0f;
        public static float GravityX = 0f;
        public static float GravityY = -1.2f;

        public static float WindFreq = 1.2f;
        public static float WindAmp = 0.012f;
        public static float SpeedDisturb = 0.005f;

        public static float MaxBendDeg = 18f;
        public static float AnchorFollow = 1.0f;

        public static float Smoothness = 0.55f;
        public static bool FrontGuard = true;
        public static float FrontGuardMargin = 0.05f;
        public static float MaxStep = 0.15f;
        public static float MaxFixedDt = 0.02f;

        public static bool Enabled = true;

        private static readonly Dictionary<GameObject, Dictionary<string, TailDeformParams>> _remoteOverrides
            = new Dictionary<GameObject, Dictionary<string, TailDeformParams>>();

        private static readonly Dictionary<uint, Dictionary<string, TailDeformParams>> _pendingRemoteOverrides
            = new Dictionary<uint, Dictionary<string, TailDeformParams>>();

        /// <summary>返回尾巴形变的默认参数快照。</summary>
        public static TailDeformParams Defaults => new TailDeformParams
        {
            Enabled = true,
            FrontGuard = true,
            Segments = 10,
            ConstraintIters = 6,
            Damping = 0.45f,
            SpeedDamping = 0.05f,
            Stiffness = 1.0f,
            GravityX = 0f,
            GravityY = -1.2f,
            WindFreq = 1.2f,
            WindAmp = 0.012f,
            SpeedDisturb = 0.005f,
            MaxBendDeg = 18f,
            AnchorFollow = 1.0f,
            Smoothness = 0.55f,
            FrontGuardMargin = 0.05f,
            MaxStep = 0.15f,
            MaxFixedDt = 0.02f,
        };

        /// <summary>返回本机当前静态配置的参数快照。</summary>
        public static TailDeformParams LocalSnapshot() => new TailDeformParams
        {
            Enabled = Enabled,
            FrontGuard = FrontGuard,
            Segments = Segments,
            ConstraintIters = ConstraintIters,
            Damping = Damping,
            SpeedDamping = SpeedDamping,
            Stiffness = Stiffness,
            GravityX = GravityX,
            GravityY = GravityY,
            WindFreq = WindFreq,
            WindAmp = WindAmp,
            SpeedDisturb = SpeedDisturb,
            MaxBendDeg = MaxBendDeg,
            AnchorFollow = AnchorFollow,
            Smoothness = Smoothness,
            FrontGuardMargin = FrontGuardMargin,
            MaxStep = MaxStep,
            MaxFixedDt = MaxFixedDt,
        };

        /// <summary>按角色归属解析生效的尾巴参数：本机角色读取本地配置，远端角色读取独立覆盖或默认值。</summary>
        internal static TailDeformParams ResolveFor(GameObject owner, Body body = null)
        {
            bool isLocal;
            var cam = PlayerCamera.main;
            if (cam != null && cam.body != null && body != null)
            {
                isLocal = body == cam.body;
            }
            else
            {
                isLocal = owner != null && SkinApplier.IsLocalCharacter(owner);
            }

            if (isLocal)
            {
                return LocalSnapshot();
            }

            GameObject bodyGo = body != null ? body.gameObject : null;
            GameObject parentGo = body != null && body.transform.parent != null ? body.transform.parent.gameObject : null;
            // 联机与单机路径可能分别以根对象或子级 Body 作为登记键，按候选顺序回退避免键值错位。
            string fallbackSkinId = SkinApplier.GetAssignedSkin(owner)
                ?? SkinApplier.GetAssignedSkin(bodyGo)
                ?? SkinApplier.GetAssignedSkin(parentGo);

            if (TryLookupRemoteOverride(owner, fallbackSkinId, out var remoteCfg)) return remoteCfg;
            if (bodyGo != null && bodyGo != owner && TryLookupRemoteOverride(bodyGo, fallbackSkinId, out remoteCfg)) return remoteCfg;
            if (parentGo != null && parentGo != owner && parentGo != bodyGo && TryLookupRemoteOverride(parentGo, fallbackSkinId, out remoteCfg)) return remoteCfg;

            return Defaults;
        }

        private static bool TryLookupRemoteOverride(GameObject candidate, string fallbackSkinId, out TailDeformParams cfg)
        {
            cfg = default;
            if (candidate == null) return false;
            string skinId = SkinApplier.GetAssignedSkin(candidate);
            if (string.IsNullOrEmpty(skinId)) skinId = fallbackSkinId;
            if (string.IsNullOrEmpty(skinId)) return false;
            return _remoteOverrides.TryGetValue(candidate, out var map)
                && map != null
                && map.TryGetValue(skinId, out cfg);
        }

        internal static void SetRemoteOverride(GameObject playerObj, string skinId, TailDeformParams cfg)
        {
            if (playerObj == null || string.IsNullOrEmpty(skinId)) return;
            PruneDeadOwners();
            if (!_remoteOverrides.TryGetValue(playerObj, out var map))
            {
                map = new Dictionary<string, TailDeformParams>(StringComparer.Ordinal);
                _remoteOverrides[playerObj] = map;
            }
            map[skinId] = cfg;
        }

        internal static void StagePendingRemoteOverride(uint netId, string skinId, TailDeformParams cfg)
        {
            if (netId == 0 || string.IsNullOrEmpty(skinId)) return;
            if (!_pendingRemoteOverrides.TryGetValue(netId, out var map))
            {
                map = new Dictionary<string, TailDeformParams>(StringComparer.Ordinal);
                _pendingRemoteOverrides[netId] = map;
            }
            map[skinId] = cfg;
        }

        internal static void BindPendingOverrides(uint netId, GameObject playerObj)
        {
            if (netId == 0 || playerObj == null) return;
            if (!_pendingRemoteOverrides.TryGetValue(netId, out var pending)) return;
            // 保留按 netId 索引的远端覆盖副本，以便同 netId 换物种重建新躯体时再次绑定继承。
            if (!_remoteOverrides.TryGetValue(playerObj, out var target))
            {
                _remoteOverrides[playerObj] = new Dictionary<string, TailDeformParams>(pending, StringComparer.Ordinal);
                return;
            }
            foreach (var kv in pending)
            {
                target[kv.Key] = kv.Value;
            }
        }

        internal static void DropPendingOverrides(uint netId)
        {
            if (netId != 0) _pendingRemoteOverrides.Remove(netId);
        }

        internal static void ClearRemoteOverrides()
        {
            _remoteOverrides.Clear();
            _pendingRemoteOverrides.Clear();
        }

        internal static void ClearSceneObjectOverrides()
        {
            // 局内切图仅释放旧场景 GameObject 引用，保留按 netId 暂存的待绑定覆盖供新躯体恢复。
            _remoteOverrides.Clear();
        }

        private static void PruneDeadOwners()
        {
            List<GameObject> dead = null;
            foreach (var kv in _remoteOverrides)
            {
                if (kv.Key == null) (dead ?? (dead = new List<GameObject>())).Add(kv.Key);
            }
            if (dead != null)
            {
                for (int i = 0; i < dead.Count; i++) _remoteOverrides.Remove(dead[i]);
            }
        }
    }
}
