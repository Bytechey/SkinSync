using System;
using System.Collections.Generic;
using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>按 accessories.json 配置在角色各肢体上挂载、刷新并按角色隔离管理自定义配件。</summary>
    public static class AccessoryAttacher
    {
        private const float PIXELS_PER_UNIT = 8f;
        private const string ContainerName = "HwAccessories";

        public static readonly Dictionary<string, GameObject> Mounted = new Dictionary<string, GameObject>();

        internal sealed class MountMeta
        {
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

        internal static readonly Dictionary<string, MountMeta> Meta = new Dictionary<string, MountMeta>();

        private static readonly Dictionary<Body, Dictionary<string, MountMeta>> _byBody
            = new Dictionary<Body, Dictionary<string, MountMeta>>();

        private static readonly Dictionary<GameObject, Dictionary<string, SkinSyncSettings.AccessoryOverride>> _remoteOverrides
            = new Dictionary<GameObject, Dictionary<string, SkinSyncSettings.AccessoryOverride>>();

        private static readonly Dictionary<uint, Dictionary<string, SkinSyncSettings.AccessoryOverride>> _pendingRemoteOverrides
            = new Dictionary<uint, Dictionary<string, SkinSyncSettings.AccessoryOverride>>();

        internal static void SetRemoteOverride(GameObject playerObj, string skinId, string accId,
            bool enabled, int offX, int offY, float rotation, int zOrder)
        {
            if (playerObj == null || string.IsNullOrEmpty(skinId) || string.IsNullOrEmpty(accId)) return;
            if (!_remoteOverrides.TryGetValue(playerObj, out var map))
            {
                map = new Dictionary<string, SkinSyncSettings.AccessoryOverride>(StringComparer.Ordinal);
                _remoteOverrides[playerObj] = map;
            }
            map[skinId + "|" + accId] = new SkinSyncSettings.AccessoryOverride
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
            map[skinId + "|" + accId] = new SkinSyncSettings.AccessoryOverride
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
            if (isLocal)
            {
                Mounted.Clear();
                Meta.Clear();
            }

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
                    SkinSyncMod.ModLog.Warning($"accessory {entry.Id} target limb {entry.Limb} not found");
                    continue;
                }
                Sprite sprite = ResolveAccessorySprite(sprites, entry.Sprite, entry.Limb, isRight);
                if (sprite == null)
                {
                    SkinSyncMod.ModLog.Warning($"accessory {entry.Id} sprite {entry.Sprite} not loaded");
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

                var go = AttachOne(
                    parent: limb.transform,
                    name: ContainerName + "_" + entry.Id,
                    sprite: sprite,
                    offX: effOffX, offY: effOffY, rot: effRot,
                    limbSr: limb.GetComponent<SpriteRenderer>(),
                    zOffset: effZ,
                    litMat: litMat);
                go.SetActive(effEnabled);

                var meta = new MountMeta
                {
                    RequireSlot = entry.RequireWornSlot,
                    DesiredEnabled = effEnabled,
                    Body = body,
                    MountLimb = entry.Limb,
                    SpriteBaseName = entry.Sprite,
                    PrimaryGo = go,
                };
                bodyMounts[entry.Id] = meta;
                if (isLocal)
                {
                    Mounted[entry.Id] = go;
                    Meta[entry.Id] = meta;
                }

                if (entry.SecondaryLimbs != null)
                {
                    foreach (var sec in entry.SecondaryLimbs)
                    {
                        if (sec == null) continue;
                        Limb secLimb = body.LimbByName(sec.Limb);
                        if (secLimb == null)
                        {
                            SkinSyncMod.ModLog.Warning($"accessory {entry.Id} secondary limb {sec.Limb} not found");
                            continue;
                        }
                        Sprite secSprite = ResolveAccessorySprite(sprites, sec.Sprite, sec.Limb, isRight);
                        if (secSprite == null)
                        {
                            SkinSyncMod.ModLog.Warning($"accessory {entry.Id} secondary sprite {sec.Sprite} not loaded");
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
                        secGo.SetActive(effEnabled);
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

        public static void SetEnabled(string id, bool enabled)
        {
            if (Meta.TryGetValue(id, out var m) && m != null)
            {
                m.DesiredEnabled = enabled;
                SetMountActive(m, enabled);
            }
            else if (Mounted.TryGetValue(id, out var go) && go != null)
            {
                go.SetActive(enabled);
            }
        }

        public static void SetTransform(string id, int offX, int offY, float rot, int z)
        {
            if (!Mounted.TryGetValue(id, out var go) || go == null) return;
            go.transform.localPosition = new Vector3(offX / PIXELS_PER_UNIT, -offY / PIXELS_PER_UNIT, 0f);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, rot);
            var sr = go.GetComponent<SpriteRenderer>();
            if (sr == null) return;
            var limbSr = go.transform.parent != null ? go.transform.parent.GetComponent<SpriteRenderer>() : null;
            int baseOrder = limbSr != null ? limbSr.sortingOrder : 0;
            sr.sortingOrder = baseOrder + z;
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
