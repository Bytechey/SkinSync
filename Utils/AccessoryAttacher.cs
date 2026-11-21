using System;
using System.Collections.Generic;
using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>按 accessories.json 配置在角色各肢体上挂载、刷新并按角色隔离管理自定义配件。</summary>
    public static class AccessoryAttacher
    {
        private const float PIXELS_PER_UNIT = 8f;
        private const string ContainerName = SkinApplier.AccessoriesContainerPrefix;
        private const int MaxRemoteOverridesPerCharacter = 64;

        internal sealed class MountMeta
        {
            public GameObject Owner;
            public string SkinId;
            public string RequireSlot;
            public bool DesiredEnabled;
            public Body Body;
            public string MountLimb;
            public string SpriteBaseName;
            public GameObject PrimaryGo;
            public List<SecondaryRef> Secondaries;
        }

        internal sealed class SecondaryRef
        {
            public string MountLimb;
            public string SpriteBaseName;
            public GameObject Go;
        }

        private static readonly Dictionary<Body, Dictionary<string, MountMeta>> _byBody
            = new Dictionary<Body, Dictionary<string, MountMeta>>();

        private static readonly Dictionary<GameObject, Dictionary<string, SkinSyncSettings.AccessoryOverride>> _remoteOverrides
            = new Dictionary<GameObject, Dictionary<string, SkinSyncSettings.AccessoryOverride>>();

        private static readonly Dictionary<uint, Dictionary<string, SkinSyncSettings.AccessoryOverride>> _pendingRemoteOverrides
            = new Dictionary<uint, Dictionary<string, SkinSyncSettings.AccessoryOverride>>();

        private static readonly HashSet<string> _warnedMissing
            = new HashSet<string>(StringComparer.Ordinal);

        internal static void SetRemoteOverride(GameObject playerObj, string skinId, string accId,
            bool enabled, int offX, int offY, float rotation, int zOrder)
        {
            if (playerObj == null || string.IsNullOrEmpty(skinId) || string.IsNullOrEmpty(accId)) return;
            if (!_remoteOverrides.TryGetValue(playerObj, out var map))
            {
                map = new Dictionary<string, SkinSyncSettings.AccessoryOverride>(StringComparer.Ordinal);
                _remoteOverrides[playerObj] = map;
            }
            string key = skinId + "|" + accId;
            if (!map.ContainsKey(key) && map.Count >= MaxRemoteOverridesPerCharacter) return;
            map[key] = new SkinSyncSettings.AccessoryOverride
            {
                Enabled = enabled,
                OffX = offX,
                OffY = offY,
                Rotation = rotation,
                ZOrder = zOrder,
            };
        }

        internal static void StagePendingRemoteOverride(uint netId, string skinId, string accId,
            bool enabled, int offX, int offY, float rotation, int zOrder)
        {
            if (netId == 0 || string.IsNullOrEmpty(skinId) || string.IsNullOrEmpty(accId)) return;
            if (!_pendingRemoteOverrides.TryGetValue(netId, out var map))
            {
                map = new Dictionary<string, SkinSyncSettings.AccessoryOverride>(StringComparer.Ordinal);
                _pendingRemoteOverrides[netId] = map;
            }
            string key = skinId + "|" + accId;
            if (!map.ContainsKey(key) && map.Count >= MaxRemoteOverridesPerCharacter) return;
            map[key] = new SkinSyncSettings.AccessoryOverride
            {
                Enabled = enabled,
                OffX = offX,
                OffY = offY,
                Rotation = rotation,
                ZOrder = zOrder,
            };
        }

        internal static void BindPendingOverrides(uint netId, GameObject playerObj)
        {
            if (netId == 0 || playerObj == null) return;
            if (!_pendingRemoteOverrides.TryGetValue(netId, out var pending)) return;
            // 保留按 netId 索引的远端覆盖副本，以便同 netId 换物种重建新躯体时再次绑定继承。
            if (!_remoteOverrides.TryGetValue(playerObj, out var target))
            {
                _remoteOverrides[playerObj] = new Dictionary<string, SkinSyncSettings.AccessoryOverride>(pending, StringComparer.Ordinal);
                return;
            }
            foreach (var kv in pending)
            {
                if (!target.ContainsKey(kv.Key) && target.Count >= MaxRemoteOverridesPerCharacter) continue;
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
            PruneDeadBodies();
        }

        internal static void ClearSceneObjectOverrides()
        {
            // 局内切图仅释放旧场景 GameObject 引用，保留按 netId 暂存的待绑定覆盖供新躯体恢复。
            _remoteOverrides.Clear();
            PruneDeadBodies();
        }

        /// <summary>清理目标角色的旧配件并按配置与覆盖参数重新挂载。</summary>
        public static void Apply(GameObject playerObj, IList<AccessoryConfigLoader.Entry> entries, Dictionary<string, Sprite> sprites, string skinId = null)
        {
            if (playerObj == null) return;
            Body body = playerObj.GetComponentInChildren<Body>(true);
            if (body == null) return;
            bool isRight = body.isRight;
            bool isLocal = SkinApplier.IsLocalCharacter(playerObj);

            PruneDeadBodies();
            ClearExisting(body);
            _byBody.Remove(body);

            if (entries == null || entries.Count == 0) return;

            var bodyRenderer = body.GetComponentInChildren<SpriteRenderer>();
            Material litMat = SkinApplier.GetSharedCleanLitMaterial(bodyRenderer);
            var bodyMounts = new Dictionary<string, MountMeta>(StringComparer.Ordinal);
            _byBody[body] = bodyMounts;

            foreach (var entry in entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.Id)) continue;
                Limb limb = body.LimbByName(entry.Limb);
                if (limb == null)
                {
                    WarnMissingOnce(skinId, entry.Id, $"accessory {entry.Id} target limb {entry.Limb} not found");
                    continue;
                }
                Sprite sprite = ResolveAccessorySprite(sprites, entry.Sprite, entry.Limb, isRight);
                if (sprite == null)
                {
                    WarnMissingOnce(skinId, entry.Id, $"accessory {entry.Id} sprite {entry.Sprite} not loaded");
                    continue;
                }

                int effOffX = entry.OffX;
                int effOffY = entry.OffY;
                float effRot = entry.Rotation;
                int effZ = entry.ZOrder;
                bool effEnabled = entry.Enabled;

                SkinSyncSettings.AccessoryOverride ov = ResolveOverride(playerObj, isLocal, skinId, entry.Id);
                if (ov != null)
                {
                    if (ov.Enabled.HasValue) effEnabled = ov.Enabled.Value;
                    if (ov.OffX.HasValue) effOffX = ov.OffX.Value;
                    if (ov.OffY.HasValue) effOffY = ov.OffY.Value;
                    if (ov.Rotation.HasValue) effRot = ov.Rotation.Value;
                    if (ov.ZOrder.HasValue) effZ = ov.ZOrder.Value;
                }

                bool initialActive = ComputeEffectiveActive(body, effEnabled, entry.RequireWornSlot);
                var go = AttachOne(
                    parent: limb.transform,
                    name: ContainerName + "_" + entry.Id,
                    sprite: sprite,
                    offX: effOffX, offY: effOffY, rot: effRot,
                    limbSr: limb.GetComponent<SpriteRenderer>(),
                    zOffset: effZ,
                    litMat: litMat);
                go.SetActive(initialActive);

                var meta = new MountMeta
                {
                    Owner = playerObj,
                    SkinId = skinId,
                    RequireSlot = entry.RequireWornSlot,
                    DesiredEnabled = effEnabled,
                    Body = body,
                    MountLimb = entry.Limb,
                    SpriteBaseName = entry.Sprite,
                    PrimaryGo = go,
                };
                bodyMounts[entry.Id] = meta;

                if (entry.SecondaryLimbs != null)
                {
                    foreach (var sec in entry.SecondaryLimbs)
                    {
                        if (sec == null) continue;
                        Limb secLimb = body.LimbByName(sec.Limb);
                        if (secLimb == null)
                        {
                            WarnMissingOnce(skinId, entry.Id, $"accessory {entry.Id} secondary limb {sec.Limb} not found");
                            continue;
                        }
                        Sprite secSprite = ResolveAccessorySprite(sprites, sec.Sprite, sec.Limb, isRight);
                        if (secSprite == null)
                        {
                            WarnMissingOnce(skinId, entry.Id, $"accessory {entry.Id} secondary sprite {sec.Sprite} not loaded");
                            continue;
                        }
                        // 副件保留在目标副肢体节点下跟随动作旋转，显隐与主件同步。
                        var secGo = AttachOne(
                            parent: secLimb.transform,
                            name: ContainerName + "_" + entry.Id + "__" + sec.Limb,
                            sprite: secSprite,
                            offX: sec.OffX, offY: sec.OffY, rot: sec.Rotation,
                            limbSr: secLimb.GetComponent<SpriteRenderer>(),
                            zOffset: sec.ZOrder,
                            litMat: litMat);
                        secGo.SetActive(initialActive);
                        if (meta.Secondaries == null) meta.Secondaries = new List<SecondaryRef>();
                        meta.Secondaries.Add(new SecondaryRef
                        {
                            MountLimb = sec.Limb,
                            SpriteBaseName = sec.Sprite,
                            Go = secGo,
                        });
                    }
                }
            }
        }

        private static void WarnMissingOnce(string skinId, string accId, string message)
        {
            string key = (skinId ?? string.Empty) + "|" + (accId ?? string.Empty);
            if (_warnedMissing.Add(key))
            {
                ModLog.Warning(message);
            }
        }

        private static SkinSyncSettings.AccessoryOverride ResolveOverride(GameObject playerObj, bool isLocal, string skinId, string accId)
        {
            if (string.IsNullOrEmpty(skinId) || string.IsNullOrEmpty(accId)) return null;
            if (!isLocal && _remoteOverrides.TryGetValue(playerObj, out var remoteMap)
                && remoteMap.TryGetValue(skinId + "|" + accId, out var remoteOv))
            {
                return remoteOv;
            }
            if (isLocal && SkinSync.Settings != null)
            {
                return SkinSync.Settings.GetAccessoryOverride(skinId, accId);
            }
            return null;
        }

        private static GameObject AttachOne(Transform parent, string name, Sprite sprite,
            int offX, int offY, float rot, SpriteRenderer limbSr, int zOffset,
            Material litMat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = new Vector3(offX / PIXELS_PER_UNIT, -offY / PIXELS_PER_UNIT, 0f);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, rot);
            go.transform.localScale = Vector3.one;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            if (limbSr != null)
            {
                sr.sortingLayerID = limbSr.sortingLayerID;
                sr.sortingOrder = limbSr.sortingOrder + zOffset;
                sr.color = limbSr.color;
            }
            if (litMat != null) sr.sharedMaterial = litMat;
            ShadowAttacher.Ensure(go);
            return go;
        }

        /// <summary>在目标角色左右朝向翻转时更新其已挂载配件的侧向贴图。</summary>
        public static void RefreshSidedSprites(GameObject playerObj, Dictionary<string, Sprite> sprites, bool isRight)
        {
            if (playerObj == null || sprites == null) return;
            Body body = playerObj.GetComponentInChildren<Body>(true);
            if (body == null || !_byBody.TryGetValue(body, out var bodyMounts)) return;

            foreach (var kv in bodyMounts)
            {
                var meta = kv.Value;
                if (meta == null) continue;
                if (meta.PrimaryGo != null)
                {
                    var sr = meta.PrimaryGo.GetComponent<SpriteRenderer>();
                    if (sr != null)
                    {
                        var s = ResolveAccessorySprite(sprites, meta.SpriteBaseName, meta.MountLimb, isRight);
                        if (s != null) sr.sprite = s;
                    }
                }
                if (meta.Secondaries == null) continue;
                foreach (var sec in meta.Secondaries)
                {
                    if (sec == null || sec.Go == null) continue;
                    var secSr = sec.Go.GetComponent<SpriteRenderer>();
                    if (secSr == null) continue;
                    var s = ResolveAccessorySprite(sprites, sec.SpriteBaseName, sec.MountLimb, isRight);
                    if (s != null) secSr.sprite = s;
                }
            }
        }

        internal static void EnforceWearableSlots(bool requireSlotActive)
        {
            PruneDeadBodies();
            foreach (var bodyKv in _byBody)
            {
                Body body = bodyKv.Key;
                if (body == null) continue;
                foreach (var kv in bodyKv.Value)
                {
                    var meta = kv.Value;
                    if (meta == null) continue;
                    bool want = meta.DesiredEnabled;
                    if (want && requireSlotActive && !string.IsNullOrEmpty(meta.RequireSlot))
                    {
                        want = body.GetWearableBySlotID(meta.RequireSlot) != null;
                    }
                    SetMountActive(meta, want);
                }
            }
        }

        private static bool ComputeEffectiveActive(Body body, bool desiredEnabled, string requireSlot)
        {
            if (!desiredEnabled) return false;
            if (AccessoryEnforcer.Active && !string.IsNullOrEmpty(requireSlot) && body != null)
            {
                return body.GetWearableBySlotID(requireSlot) != null;
            }
            return true;
        }

        private static void SetMountActive(MountMeta meta, bool active)
        {
            if (meta.PrimaryGo != null && meta.PrimaryGo.activeSelf != active)
                meta.PrimaryGo.SetActive(active);
            if (meta.Secondaries == null) return;
            for (int i = 0; i < meta.Secondaries.Count; i++)
            {
                var sec = meta.Secondaries[i];
                if (sec != null && sec.Go != null && sec.Go.activeSelf != active)
                    sec.Go.SetActive(active);
            }
        }

        private static Sprite ResolveAccessorySprite(Dictionary<string, Sprite> sprites, string baseName, string limbName, bool isRight)
        {
            if (sprites == null || string.IsNullOrEmpty(baseName)) return null;
            char side = '\0';
            if (!string.IsNullOrEmpty(limbName))
            {
                char last = limbName[limbName.Length - 1];
                if (last == 'F' || last == 'B') side = last;
            }
            return SkinApplier.ResolveSidedSprite(sprites, baseName, side, isRight);
        }

        private static bool TryFindLocalMountMeta(string id, out MountMeta meta)
        {
            meta = null;
            if (string.IsNullOrEmpty(id)) return false;

            var cam = PlayerCamera.main;
            if (cam != null && cam.body != null
                && _byBody.TryGetValue(cam.body, out var camMounts)
                && camMounts != null
                && camMounts.TryGetValue(id, out meta) && meta != null)
            {
                return true;
            }

            foreach (var kv in _byBody)
            {
                if (kv.Key == null || kv.Value == null) continue;
                if (!kv.Value.TryGetValue(id, out var candidate) || candidate == null) continue;
                GameObject host = candidate.Owner != null ? candidate.Owner : kv.Key.gameObject;
                if (SkinApplier.IsLocalCharacter(host))
                {
                    meta = candidate;
                    return true;
                }
            }
            return false;
        }

        private static void ApplyPrimaryTransform(GameObject go, int offX, int offY, float rot, int zOrder)
        {
            if (go == null) return;
            go.transform.localPosition = new Vector3(offX / PIXELS_PER_UNIT, -offY / PIXELS_PER_UNIT, 0f);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, rot);
            var sr = go.GetComponent<SpriteRenderer>();
            if (sr == null) return;
            var limbSr = go.transform.parent != null ? go.transform.parent.GetComponent<SpriteRenderer>() : null;
            int baseOrder = limbSr != null ? limbSr.sortingOrder : 0;
            sr.sortingOrder = baseOrder + zOrder;
        }

        public static void SetEnabled(string id, bool enabled)
        {
            if (!TryFindLocalMountMeta(id, out var meta)) return;
            meta.DesiredEnabled = enabled;
            SetMountActive(meta, ComputeEffectiveActive(meta.Body, enabled, meta.RequireSlot));
        }

        public static void SetTransform(string id, int offX, int offY, float rot, int z)
        {
            if (!TryFindLocalMountMeta(id, out var meta) || meta.PrimaryGo == null) return;
            ApplyPrimaryTransform(meta.PrimaryGo, offX, offY, rot, z);
        }

        /// <summary>对已挂载对应皮肤的远端角色原地更新单个配件的显隐与变换；未就绪或皮肤不匹配时返回 false。</summary>
        internal static bool TryApplySingleRemoteOverride(GameObject playerObj, string skinId, string accId,
            bool enabled, int offX, int offY, float rot, int zOrder)
        {
            if (playerObj == null || string.IsNullOrEmpty(skinId) || string.IsNullOrEmpty(accId)) return false;
            string activeSkin = SkinApplier.GetCharacterByChara(playerObj);
            if (!string.Equals(activeSkin, skinId, StringComparison.Ordinal)) return false;

            Body body = playerObj.GetComponentInChildren<Body>(true);
            if (body == null || !_byBody.TryGetValue(body, out var bodyMounts) || bodyMounts == null) return false;
            if (!bodyMounts.TryGetValue(accId, out var meta) || meta == null || meta.PrimaryGo == null) return false;
            if (!string.Equals(meta.SkinId, skinId, StringComparison.Ordinal)) return false;

            meta.DesiredEnabled = enabled;
            SetMountActive(meta, ComputeEffectiveActive(body, enabled, meta.RequireSlot));
            ApplyPrimaryTransform(meta.PrimaryGo, offX, offY, rot, zOrder);
            return true;
        }

        private static void PruneDeadBodies()
        {
            List<Body> dead = null;
            foreach (var kv in _byBody)
            {
                if (kv.Key == null) (dead ?? (dead = new List<Body>())).Add(kv.Key);
            }
            if (dead != null)
            {
                for (int i = 0; i < dead.Count; i++) _byBody.Remove(dead[i]);
            }

            List<GameObject> deadGo = null;
            foreach (var kv in _remoteOverrides)
            {
                if (kv.Key == null) (deadGo ?? (deadGo = new List<GameObject>())).Add(kv.Key);
            }
            if (deadGo != null)
            {
                for (int i = 0; i < deadGo.Count; i++) _remoteOverrides.Remove(deadGo[i]);
            }
        }

        private static void ClearExisting(Body body)
        {
            foreach (var tf in body.GetComponentsInChildren<Transform>(true))
            {
                if (tf == null || tf.gameObject == null) continue;
                if (tf.name.StartsWith(ContainerName + "_", StringComparison.Ordinal))
                    UnityEngine.Object.Destroy(tf.gameObject);
            }
        }
    }
}
