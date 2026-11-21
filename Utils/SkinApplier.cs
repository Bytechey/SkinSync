using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SkinSyncMod.Interop.Consumers;
using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>加载皮肤贴图与配置并将其应用到角色的肢体、表情、尾巴、翅膀、配件与区块特效。</summary>
    public static partial class SkinApplier
    {
        internal const string WingsContainerName = "HwWings";
        internal const string AccessoriesContainerPrefix = "HwAccessories";
        internal const string ZonesContainerPrefix = "HwZones_";

        private const float PIXELS_PER_UNIT = 8f;
        private const float UPTORSO_OFFSET_X = 6f;
        private const float UPTORSO_OFFSET_Y = -10f;
        private const float RequestRetryCooldownSec = 30f;
        private static bool NormalizeSizeEnabled => false;

        internal static int SkinStateVersion { get; private set; }

        private static readonly Dictionary<string, Dictionary<string, Sprite>> _skinCache
            = new Dictionary<string, Dictionary<string, Sprite>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, WingsConfigLoader.Config> _wingsCache
            = new Dictionary<string, WingsConfigLoader.Config>(StringComparer.Ordinal);
        private static readonly HashSet<string> _emptySkinDirs
            = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> _skinsWithSidedSprites
            = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> _loggedMissingSkins
            = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<GameObject, string> _byChara
            = new Dictionary<GameObject, string>();
        private static readonly Dictionary<GameObject, int> _charTypeByChara
            = new Dictionary<GameObject, int>();
        private static readonly HashSet<GameObject> _vanillaApplied
            = new HashSet<GameObject>();
        private static readonly HashSet<GameObject> _pendingPackTargets = new HashSet<GameObject>();
        private static readonly Dictionary<string, float> _requestedPacks
            = new Dictionary<string, float>(StringComparer.Ordinal);
        private static readonly Dictionary<int, Material> _cleanLitMaterials
            = new Dictionary<int, Material>();

        private static string _currentCharacter;
        private static int _lastLimbTotal;
        private static int _lastLimbApplied;
        private static Action<GameObject, bool> _skinApplied;

        /// <summary>查询角色对象当前生效的皮肤名称；未应用或处于原版外观时返回 null。</summary>
        public static string GetCharacterByChara(GameObject chara)
        {
            if (chara == null || _vanillaApplied.Contains(chara)) return null;
            return _byChara.TryGetValue(chara, out var name) ? name : null;
        }

        internal static string GetAssignedSkin(GameObject chara)
        {
            return chara != null && _byChara.TryGetValue(chara, out var name) ? name : null;
        }

        /// <summary>沿 Body 父级向上查找当前生效的皮肤名，处于原版外观模式或未套用时返回 null。</summary>
        internal static string GetActiveSkinForBody(Body body)
        {
            if (body == null) return null;
            Transform t = body.transform;
            while (t != null)
            {
                GameObject go = t.gameObject;
                if (_byChara.TryGetValue(go, out string name) && !string.IsNullOrEmpty(name))
                {
                    if (_vanillaApplied.Contains(go) || ShouldUseVanillaSkin(go)) return null;
                    return name;
                }
                t = t.parent;
            }
            return null;
        }

        /// <summary>在指定半径内查找最近且已应用自定义皮肤的角色名称（兼容单机根对象与联机子级 Body 键）。</summary>
        public static bool TryGetCharacterByPosition(Vector2 pos, float radius, out string character)
        {
            character = null;
            float bestDist = radius;
            foreach (var body in UnityEngine.Object.FindObjectsOfType<Body>())
            {
                if (body == null) continue;
                float d = Vector2.Distance(pos, body.transform.position);
                if (d > bestDist) continue;
                string skin = GetActiveSkinForBody(body);
                if (string.IsNullOrEmpty(skin)) continue;
                bestDist = d;
                character = skin;
            }
            return character != null;
        }

        /// <summary>当全局所有非原版角色使用同一款皮肤时返回该皮肤名称。</summary>
        public static bool TryGetAnyAppliedCharacter(out string character)
        {
            character = null;
            string only = null;
            foreach (var kv in _byChara)
            {
                if (kv.Key == null || _vanillaApplied.Contains(kv.Key)) continue;
                if (string.IsNullOrEmpty(kv.Value)) continue;
                if (only == null) { only = kv.Value; continue; }
                if (only != kv.Value) return false;
            }
            if (only == null) return false;
            character = only;
            return true;
        }

        /// <summary>获取最近一次应用皮肤的贴图字典。</summary>
        public static Dictionary<string, Sprite> GetSpriteDict()
        {
            return GetSpriteDict(_currentCharacter);
        }

        /// <summary>获取指定皮肤的贴图字典，传空时回退最近应用的皮肤。</summary>
        public static Dictionary<string, Sprite> GetSpriteDict(string characterName)
        {
            string key = !string.IsNullOrEmpty(characterName) ? characterName : _currentCharacter;
            if (!string.IsNullOrEmpty(key) && _skinCache.TryGetValue(key, out var d)) return d;
            return null;
        }

        /// <summary>订阅皮肤应用完成事件，供外部模组反射挂载扩展逻辑。</summary>
        public static void RegisterSkinAppliedListener(Action<GameObject, bool> cb)
        {
            if (cb == null) return;
            _skinApplied -= cb;
            _skinApplied += cb;
        }

        /// <summary>取消订阅皮肤应用完成事件。</summary>
        public static void UnregisterSkinAppliedListener(Action<GameObject, bool> cb)
        {
            if (cb == null) return;
            _skinApplied -= cb;
        }

        /// <summary>判断目标角色当前是否挂载了自定义翅膀容器。</summary>
        public static bool HasWings(GameObject playerObj)
        {
            if (playerObj == null) return false;
            Body body = playerObj.GetComponentInChildren<Body>(true);
            if (body == null) return false;
            foreach (var tf in body.GetComponentsInChildren<Transform>(true))
            {
                if (tf != null && tf.name == WingsContainerName) return true;
            }
            return false;
        }

        private static void InvokeSkinApplied(GameObject playerObj, bool hasWings)
        {
            var handlers = _skinApplied;
            if (handlers == null) return;
            foreach (Action<GameObject, bool> cb in handlers.GetInvocationList())
            {
                try { cb(playerObj, hasWings); }
                catch (Exception ex) { ModLog.Warning("皮肤应用监听回调异常: " + ex.Message); }
            }
        }

        /// <summary>场景切换时清理已销毁角色的缓存引用、原版基线字典、远端临时覆盖与皮肤包请求冷却记录。</summary>
        internal static void OnSceneChanged()
        {
            PruneDeadObjects();
            ClearBaselineDictionaries();
            _pendingPackTargets.Clear();
            _requestedPacks.Clear();
            _loggedMissingSkins.Clear();
            AccessoryAttacher.ClearRemoteOverrides();
            TailDeformConfig.ClearRemoteOverrides();
            BloodAttacher.OnSceneChanged();
            WoundViewAttacher.OnSceneChanged();
            SkinStateVersion++;
        }

        private static void PruneDeadObjects()
        {
            var dead = new List<GameObject>();
            foreach (var kv in _byChara)
            {
                if (kv.Key == null) dead.Add(kv.Key);
            }
            foreach (var go in dead)
            {
                _byChara.Remove(go);
                _charTypeByChara.Remove(go);
            }
            _vanillaApplied.RemoveWhere(go => go == null);
            _pendingPackTargets.RemoveWhere(go => go == null);
        }

        /// <summary>将指定皮肤完整应用到目标角色对象。</summary>
        public static void ApplySkinToPlayer(GameObject playerObj, string characterName, int remoteCharType = -1)
        {
            if (playerObj == null) return;

            bool hadPrev = _byChara.TryGetValue(playerObj, out var prevSkin);
            bool wasVanilla = _vanillaApplied.Remove(playerObj);

            if (string.IsNullOrEmpty(characterName)) characterName = prevSkin;
            if (!string.IsNullOrEmpty(characterName)) _byChara[playerObj] = characterName;

            if (ShouldUseVanillaSkin(playerObj))
            {
                RestoreVanillaSkin(playerObj);
                return;
            }

            if (string.IsNullOrEmpty(characterName)) return;

            DisableConflictingReplacers(playerObj);

            Body bodyRef = playerObj.GetComponentInChildren<Body>(true);
            int charType = bodyRef != null ? bodyRef.charType : (remoteCharType >= 0 ? remoteCharType : (_charTypeByChara.TryGetValue(playerObj, out int cachedCt) ? cachedCt : 0));
            bool sameCharType = _charTypeByChara.TryGetValue(playerObj, out int prevCharType) && prevCharType == charType;
            bool sameSkin = !wasVanilla && hadPrev && prevSkin == characterName && sameCharType && !_pendingPackTargets.Contains(playerObj);
            if (hadPrev && !wasVanilla && prevSkin == characterName && !sameCharType)
            {
                ResetCharacterToVanillaBaseline(playerObj);
            }
            _charTypeByChara[playerObj] = charType;

            if (!_skinCache.TryGetValue(characterName, out var spriteDict))
            {
                if (_emptySkinDirs.Contains(characterName))
                {
                    _pendingPackTargets.Add(playerObj);
                    SkinStateVersion++;
                    return;
                }
                spriteDict = LoadCharacterSprites(characterName);
                if (spriteDict == null || spriteDict.Count == 0)
                {
                    // 记录因缺包早退尚未完成贴图替换的目标，避免后续补齐时被误判为 sameSkin。
                    _pendingPackTargets.Add(playerObj);
                    SkinStateVersion++;
                    return;
                }
                _skinCache[characterName] = spriteDict;
            }
            _pendingPackTargets.Remove(playerObj);

            string skinDir = SkinPathResolver.GetSkinDir(characterName);
            if (!_wingsCache.TryGetValue(characterName, out var wingsCfg))
            {
                string wingsJsonPath = string.IsNullOrEmpty(skinDir) ? null : Path.Combine(skinDir, "wings.json");
                wingsCfg = !string.IsNullOrEmpty(wingsJsonPath) && File.Exists(wingsJsonPath)
                    ? WingsConfigLoader.Load(wingsJsonPath)
                    : (LoadJsonFromMemoryPack(characterName, "wings.json", WingsConfigLoader.Parse) ?? WingsConfigLoader.Defaults());
                _wingsCache[characterName] = wingsCfg;
            }

            bool curIsRight = bodyRef == null || bodyRef.isRight;
            if (!sameSkin)
            {
                float fitScale = GlobalFitScale(spriteDict);

                int limbTotal = 0;
                int limbApplied = 0;
                foreach (var limb in playerObj.GetComponentsInChildren<Limb>(true))
                {
                    limbTotal++;
                    if (ReplaceLimbSprite(limb, spriteDict, curIsRight, charType, fitScale)) limbApplied++;
                }
                _lastLimbTotal = limbTotal;
                _lastLimbApplied = limbApplied;

                foreach (var tail in playerObj.GetComponentsInChildren<TailScript>(true))
                    ReplaceTailSprite(tail, spriteDict, curIsRight, charType, fitScale);

                foreach (var face in playerObj.GetComponentsInChildren<FacialExpression>(true))
                    ReplaceFacialExpressionSprites(face, spriteDict, curIsRight, charType, fitScale);

                EnsureWingsAttached(playerObj, spriteDict, wingsCfg);

                foreach (var tail in playerObj.GetComponentsInChildren<TailScript>(true))
                {
                    TailFlowDeform deform = null;
                    foreach (var d in tail.GetComponents<TailFlowDeform>())
                    {
                        if (d != null && !d.IsDisposed) { deform = d; break; }
                    }
                    if (deform == null && tail.enabled && tail.GetComponent<SpriteRenderer>() != null)
                        deform = tail.gameObject.AddComponent<TailFlowDeform>();
                    if (deform != null)
                    {
                        deform.BindOwner(playerObj);
                        deform.enabled = true;
                    }
                }
            }

            string accessoriesPath = string.IsNullOrEmpty(skinDir) ? null : Path.Combine(skinDir, "accessories.json");
            var accEntries = !string.IsNullOrEmpty(accessoriesPath) && File.Exists(accessoriesPath)
                ? AccessoryConfigLoader.Load(accessoriesPath)
                : (LoadJsonFromMemoryPack(characterName, "accessories.json", AccessoryConfigLoader.Parse) ?? new List<AccessoryConfigLoader.Entry>());
            AccessoryAttacher.Apply(playerObj, accEntries, spriteDict, characterName);
            ModLog.Debug($"应用皮肤 {characterName}：肢体 {playerObj.GetComponentsInChildren<Limb>(true).Length} 个，配件 {accEntries.Count} 项{(sameSkin ? "（皮肤未变，仅刷新配件）" : string.Empty)}");

            if (!sameSkin) ReportCoverage(characterName, playerObj);

            _currentCharacter = characterName;
            if (!sameSkin) ZonesAttacher.Apply(playerObj, characterName);

            if (BloodRenderConfig.Enabled)
                BloodAttacher.Apply(playerObj, characterName);
            else
                BloodAttacher.Restore(playerObj);

            EnsureLimbDirWatcher(playerObj, bodyRef, curIsRight);
            SkinStateVersion++;
            if (bodyRef != null) WoundViewAttacher.RefreshForBody(bodyRef);
            InvokeSkinApplied(playerObj, HasWings(playerObj));
        }

        /// <summary>在 FacialExpression.Start 后补刷被联机模组重置的面部与鼻血贴图；找到已套用皮肤的角色时返回 true。</summary>
        internal static bool ReapplyFaceIfNeeded(FacialExpression face)
        {
            if (face == null) return false;
            GameObject host = null;
            string characterName = null;
            Transform t = face.transform;
            while (t != null)
            {
                if (_byChara.TryGetValue(t.gameObject, out var name) && !string.IsNullOrEmpty(name))
                {
                    host = t.gameObject;
                    characterName = name;
                    break;
                }
                t = t.parent;
            }

            if (host == null) return false;
            if (_vanillaApplied.Contains(host)) return true;
            if (!_skinCache.TryGetValue(characterName, out var spriteDict) || spriteDict == null) return true;

            DisableConflictingReplacers(face.gameObject);

            Body body = face.body != null ? face.body : host.GetComponentInChildren<Body>(true);
            bool isRight = body == null || body.isRight;
            int charType = body != null ? body.charType : 0;
            float fitScale = GlobalFitScale(spriteDict);
            ReplaceFacialExpressionSprites(face, spriteDict, isRight, charType, fitScale);

            if (BloodRenderConfig.Enabled)
            {
                BloodAttacher.ApplyToFaceByCharacter(face, characterName);
            }
            return true;
        }

        private static bool ShouldUseVanillaSkin(GameObject playerObj)
        {
            var settings = SkinSync.Settings;
            if (settings == null) return false;
            return IsLocalCharacter(playerObj) ? settings.VanillaSkinForSelf.Value : settings.VanillaSkinForOthers.Value;
        }

        /// <summary>判断指定角色对象是否属于本机操控的玩家。</summary>
        internal static bool IsLocalCharacter(GameObject playerObj)
        {
            if (playerObj == null) return false;
            Body body = playerObj.GetComponentInChildren<Body>(true);

            // 联机运行中强制以权威名册归属为准，防止观战相机跟随队友或母版克隆阶段误把远端角色判为本机。
            if (SkinSync.TogetherInstalled && SessionConsumer.IsRunning)
            {
                if (RosterConsumer.TryGetLocalNetBody(out _, out _, out var localGo) && localGo != null
                    && (playerObj == localGo || (body != null && localGo == body.gameObject)))
                {
                    return true;
                }
                if (body != null && RosterConsumer.TryResolveBodyIdentity(body, out _, out _, out bool isLocalPlayer))
                {
                    return isLocalPlayer;
                }
                return false;
            }

            var cam = PlayerCamera.main;
            if (cam != null && cam.body != null) return body != null && body == cam.body;
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "PreGen") return true;
            return true;
        }

        /// <summary>获取受 2D 光照影响但不受肢体血污参数污染的共享干净材质。</summary>
        internal static Material GetSharedCleanLitMaterial(SpriteRenderer refSr)
        {
            if (WorldGeneration.world != null && WorldGeneration.world.defaultMat != null)
                return WorldGeneration.world.defaultMat;
            if (refSr == null || refSr.sharedMaterial == null || refSr.sharedMaterial.shader == null)
                return null;

            int shaderId = refSr.sharedMaterial.shader.GetInstanceID();
            if (_cleanLitMaterials.TryGetValue(shaderId, out var cached) && cached != null)
                return cached;

            var clean = new Material(refSr.sharedMaterial) { hideFlags = HideFlags.HideAndDontSave };
            TailFlowDeform.ResetTransientLimbUniforms(clean);
            _cleanLitMaterials[shaderId] = clean;
            return clean;
        }

        /// <summary>清除未登记皮肤的远端角色在克隆母版时继承的自定义贴图、配件与模组容器。</summary>
        internal static void EnsureUnassignedRemoteVanilla(GameObject playerObj, int charType)
        {
            if (playerObj == null) return;
            _byChara.Remove(playerObj);
            _charTypeByChara.Remove(playerObj);
            _vanillaApplied.Remove(playerObj);
            _pendingPackTargets.Remove(playerObj);
            SanitizeClonedBodyToStock(playerObj, charType);
            SkinStateVersion++;
            Body body = playerObj.GetComponentInChildren<Body>(true);
            if (body != null) WoundViewAttacher.RefreshForBody(body);
        }

        /// <summary>将已套用皮肤的角色恢复为游戏原版贴图并移除模组挂载物。</summary>
        public static void RestoreVanillaSkin(GameObject playerObj)
        {
            if (playerObj == null) return;
            ResetCharacterToVanillaBaseline(playerObj);
            _vanillaApplied.Add(playerObj);
            _pendingPackTargets.Remove(playerObj);
            SkinStateVersion++;
            Body body = playerObj.GetComponentInChildren<Body>(true);
            if (body != null) WoundViewAttacher.RefreshForBody(body);
            InvokeSkinApplied(playerObj, false);
        }

        private static void DestroyModContainers(GameObject playerObj)
        {
            // TailDeformMesh 由所属 TailFlowDeform.OnDestroy 独占释放，避免同帧延迟销毁误删存活组件的子网格。
            var doomed = new List<GameObject>();
            foreach (var tf in playerObj.GetComponentsInChildren<Transform>(true))
            {
                if (tf == null) continue;
                string n = tf.name;
                if (n == WingsContainerName
                    || n == AccessoriesContainerPrefix
                    || n.StartsWith(AccessoriesContainerPrefix + "_", StringComparison.Ordinal)
                    || n.StartsWith(ZonesContainerPrefix, StringComparison.Ordinal))
                {
                    doomed.Add(tf.gameObject);
                }
            }
            foreach (var go in doomed)
            {
                if (go != null) UnityEngine.Object.Destroy(go);
            }
        }

        /// <summary>按当前视觉设置重新刷新所有已接管角色的外观与血液状态。</summary>
        public static void RefreshAll()
        {
            PruneDeadObjects();
            var targets = new List<GameObject>();
            foreach (var kv in _byChara)
            {
                if (kv.Key != null) targets.Add(kv.Key);
            }
            foreach (var go in _vanillaApplied)
            {
                if (go != null && !targets.Contains(go)) targets.Add(go);
            }

            string localFallback = SkinSync.Settings != null ? SkinSync.Settings.CurrentSkin.Value : null;
            foreach (var go in targets)
            {
                string skin = _byChara.TryGetValue(go, out var name) && !string.IsNullOrEmpty(name)
                    ? name
                    : (IsLocalCharacter(go) ? localFallback : null);
                if (string.IsNullOrEmpty(skin))
                {
                    if (ShouldUseVanillaSkin(go)) RestoreVanillaSkin(go);
                    continue;
                }
                ApplySkinToPlayer(go, skin);
            }
            ModLog.Info($"视觉开关刷新：已处理 {targets.Count} 个角色");
        }

        /// <summary>角色左右朝向翻转时按需刷新带侧向变体的肢体、尾巴、面部与配件贴图。</summary>
        public static void RefreshSidedSprites(GameObject playerObj, bool isRight)
        {
            if (playerObj == null || _vanillaApplied.Contains(playerObj)) return;
            if (!_byChara.TryGetValue(playerObj, out var character) || string.IsNullOrEmpty(character)) return;
            if (!_skinCache.TryGetValue(character, out var spriteDict) || spriteDict == null) return;

            if (_skinsWithSidedSprites.Contains(character))
            {
                Body body = playerObj.GetComponentInChildren<Body>(true);
                int charType = body != null ? body.charType : 0;

                float fitScale = GlobalFitScale(spriteDict);
                foreach (var limb in playerObj.GetComponentsInChildren<Limb>(true))
                    ReplaceLimbSprite(limb, spriteDict, isRight, charType, fitScale);
                foreach (var tail in playerObj.GetComponentsInChildren<TailScript>(true))
                    ReplaceTailSprite(tail, spriteDict, isRight, charType, fitScale);
                foreach (var face in playerObj.GetComponentsInChildren<FacialExpression>(true))
                    ReplaceFacialExpressionSprites(face, spriteDict, isRight, charType, fitScale);
            }

            AccessoryAttacher.RefreshSidedSprites(playerObj, spriteDict, isRight);
        }

        private static void EnsureLimbDirWatcher(GameObject playerObj, Body body, bool isRight)
        {
            if (playerObj == null) return;
            var watcher = playerObj.GetComponent<LimbDirWatcher>();
            if (watcher == null) watcher = playerObj.AddComponent<LimbDirWatcher>();
            watcher.SyncDirection(body, isRight);
        }

        private static void ReportCoverage(string characterName, GameObject playerObj)
        {
            if (_lastLimbTotal <= 0) return;

            if (_lastLimbApplied == 0)
            {
                Body body = playerObj != null ? playerObj.GetComponentInChildren<Body>(true) : null;
                ModLog.Warning($"皮肤 {characterName} 没有任何肢体贴图能对应当前角色（charType={body?.charType}，肢体 {_lastLimbTotal} 个），已全部保留原图。");
                return;
            }

            if (_lastLimbApplied < _lastLimbTotal)
            {
                ModLog.Debug($"皮肤 {characterName}：替换 {_lastLimbApplied}/{_lastLimbTotal} 个肢体，其余 {_lastLimbTotal - _lastLimbApplied} 个保留原图。");
            }
        }

        /// <summary>遍历当前场景所有 Body 并应用指定皮肤。</summary>
        public static int ApplyToScene(string characterName)
        {
            if (string.IsNullOrEmpty(characterName)) return 0;
            int count = 0;
            foreach (var body in UnityEngine.Object.FindObjectsOfType<Body>())
            {
                if (body == null) continue;
                GameObject host = body.transform.parent != null ? body.transform.parent.gameObject : body.gameObject;
                ApplySkinToPlayer(host, characterName);
                count++;
            }
            return count;
        }

        internal static Sprite ResolveSidedSprite(Dictionary<string, Sprite> dict, string baseName, char side, bool isRight)
            => Resolve.SidedSpriteLookup.Find(dict, baseName, side, isRight);

        private static Dictionary<string, Sprite> LoadCharacterSprites(string character)
        {
            var dict = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
            string basePath = SkinPathResolver.GetSkinDir(character);
            if (string.IsNullOrEmpty(basePath) || !Directory.Exists(basePath))
            {
                if (TryLoadFromMemoryPack(character, dict))
                {
                    RecordSidedVariantFlag(character, dict);
                    return dict;
                }
                if (TryRequestSkinPack(character))
                    ModLog.Info($"本机缺少皮肤 {character}，已向其他玩家请求皮肤包");
                else if (_loggedMissingSkins.Add(character))
                    ModLog.Info($"本机缺少皮肤目录 {character}（{basePath}）；请将该皮肤包放入 plugins/CustomSprites/{character}");
                return dict;
            }

            var baseSizes = BaseSizesLoader.Load(character);
            foreach (var filePath in SkinPathResolver.GetSkinFiles(basePath))
            {
                if (!filePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
                if (filePath.EndsWith(".zones.png", StringComparison.OrdinalIgnoreCase)) continue;
                if (WoundViewAttacher.IsInsideWoundViewSubdir(basePath, filePath)) continue;
                string fileName = Path.GetFileNameWithoutExtension(filePath);
                if (string.IsNullOrEmpty(fileName) || dict.ContainsKey(fileName)) continue;
                try { BuildSpriteInto(dict, fileName, File.ReadAllBytes(filePath), baseSizes); }
                catch (Exception ex) { ModLog.Warning($"读取皮肤贴图失败 ({fileName}): {ex.Message}"); }
            }

            if (dict.Count == 0) _emptySkinDirs.Add(character);
            else RecordSidedVariantFlag(character, dict);
            return dict;
        }

        private static void RecordSidedVariantFlag(string character, Dictionary<string, Sprite> dict)
        {
            foreach (var name in dict.Keys)
            {
                if (IsSidedSpriteName(name))
                {
                    _skinsWithSidedSprites.Add(character);
                    return;
                }
            }
        }

        private static bool IsSidedSpriteName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return false;
            return fileName.StartsWith("R_", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith("F", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith("B", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith("Alt", StringComparison.OrdinalIgnoreCase);
        }

        private static void BuildSpriteInto(Dictionary<string, Sprite> dict, string fileName, byte[] data, Dictionary<string, Vector2Int> baseSizes)
        {
            if (string.IsNullOrEmpty(fileName) || data == null || data.Length == 0 || dict.ContainsKey(fileName)) return;
            if (!SkinPackCodec.IsPngDimensionsSafe(data, fileName)) return;

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.HideAndDontSave,
            };
            // 仅尾巴贴图需保留 CPU 可读副本供 TailFlowDeform 提取轮廓，其余贴图直接释放系统内存副本。
            bool markNonReadable = fileName.IndexOf("tail", StringComparison.OrdinalIgnoreCase) < 0;
            try
            {
                if (!ImageConversion.LoadImage(tex, data, markNonReadable))
                {
                    UnityEngine.Object.Destroy(tex);
                    return;
                }
            }
            catch
            {
                UnityEngine.Object.Destroy(tex);
                return;
            }

            float ppu = PIXELS_PER_UNIT;
            if (baseSizes != null && baseSizes.TryGetValue(fileName, out var baseSize) && baseSize.x > 0)
                ppu = PIXELS_PER_UNIT * (tex.width / (float)baseSize.x);

            var sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), ppu);
            sprite.name = fileName;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            dict[fileName] = sprite;
        }

        internal static T LoadJsonFromMemoryPack<T>(string character, string fileName, Func<string, T> parser) where T : class
        {
            var pack = SkinPackCodec.GetInMemory(character);
            if (pack == null) return null;
            foreach (var kv in pack)
            {
                if (kv.Key.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                    return parser(Encoding.UTF8.GetString(kv.Value));
            }
            return null;
        }

        private static bool TryLoadFromMemoryPack(string character, Dictionary<string, Sprite> dict)
        {
            var pack = SkinPackCodec.GetInMemory(character);
            if (pack == null) return false;
            Dictionary<string, Vector2Int> baseSizes = null;
            foreach (var kv in pack)
            {
                if (kv.Key.EndsWith("baseSizes.json", StringComparison.OrdinalIgnoreCase))
                    baseSizes = BaseSizesLoader.ParseContent(Encoding.UTF8.GetString(kv.Value));
            }
            foreach (var kv in pack)
            {
                if (!kv.Key.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
                if (kv.Key.EndsWith(".zones.png", StringComparison.OrdinalIgnoreCase)) continue;
                if (WoundViewAttacher.IsWoundViewRelPath(kv.Key)) continue;
                BuildSpriteInto(dict, Path.GetFileNameWithoutExtension(kv.Key), kv.Value, baseSizes);
            }
            return dict.Count > 0;
        }

        private static bool TryRequestSkinPack(string character)
        {
            if (string.IsNullOrEmpty(character)) return false;
            if (SkinSync.Settings == null || !SkinSync.Settings.EnableSkinPackSync.Value) return false;
            if (!SessionConsumer.IsAvailable || !SessionConsumer.IsRunning) return false;
            float now = Time.realtimeSinceStartup;
            if (_requestedPacks.TryGetValue(character, out float lastAt) && now - lastAt < RequestRetryCooldownSec)
                return false;
            _requestedPacks[character] = now;
            Network.MultiplayerSender.SendSkinPackRequest(character);
            return true;
        }

        /// <summary>释放单个皮肤包的请求冷却记录，允许分片传输超时或配额淘汰后立即重新请求。</summary>
        internal static void ClearRequestedPack(string skinId)
        {
            if (!string.IsNullOrEmpty(skinId)) _requestedPacks.Remove(skinId);
        }

        /// <summary>当远端皮肤包到达或本地重扫后清理对应缓存并重新应用。</summary>
        public static void ReapplyForSkin(string skinID)
        {
            if (string.IsNullOrEmpty(skinID)) return;
            var targets = new List<GameObject>();
            foreach (var kv in _byChara)
            {
                if (kv.Value == skinID && kv.Key != null) targets.Add(kv.Key);
            }
            foreach (var go in targets)
            {
                if (!_vanillaApplied.Contains(go)) ResetCharacterToVanillaBaseline(go);
                _byChara.Remove(go);
                _charTypeByChara.Remove(go);
                _pendingPackTargets.Remove(go);
            }

            _skinCache.TryGetValue(skinID, out var retiredDict);
            _skinCache.Remove(skinID);
            _wingsCache.Remove(skinID);
            _emptySkinDirs.Remove(skinID);
            _skinsWithSidedSprites.Remove(skinID);
            _loggedMissingSkins.Remove(skinID);
            _requestedPacks.Remove(skinID);
            SkinPathResolver.Invalidate(skinID);
            BaseSizesLoader.Invalidate(skinID);
            ZonesConfigLoader.Invalidate(skinID);
            BloodAttacher.Invalidate(skinID);
            WoundViewAttacher.Invalidate(skinID);
            SkinPreviewRenderer.Invalidate(skinID);
            SkinSync.InvalidateAccessoryRowsCache(skinID);
            DestroySpriteDict(retiredDict);

            foreach (var go in targets) ApplySkinToPlayer(go, skinID);
            SkinStateVersion++;
            WoundViewAttacher.RefreshAll();
        }

        /// <summary>清空全部已加载皮肤与血液缓存并对场景中已套用皮肤的角色重新装配。</summary>
        internal static void ReloadAllCachedSkins()
        {
            PruneDeadObjects();
            var active = new List<KeyValuePair<GameObject, string>>();
            foreach (var kv in _byChara)
            {
                if (kv.Key != null && !string.IsNullOrEmpty(kv.Value))
                    active.Add(kv);
            }

            foreach (var kv in active)
            {
                if (!_vanillaApplied.Contains(kv.Key)) ResetCharacterToVanillaBaseline(kv.Key);
                _byChara.Remove(kv.Key);
                _charTypeByChara.Remove(kv.Key);
                _pendingPackTargets.Remove(kv.Key);
            }

            var retired = new List<Dictionary<string, Sprite>>(_skinCache.Values);
            _skinCache.Clear();
            _wingsCache.Clear();
            _emptySkinDirs.Clear();
            _skinsWithSidedSprites.Clear();
            _loggedMissingSkins.Clear();
            _requestedPacks.Clear();
            ZonesConfigLoader.Invalidate(null);
            SkinSync.InvalidateAccessoryRowsCache(null);
            BloodAttacher.ClearCaches();
            WoundViewAttacher.ClearCaches();
            ClearFittedSprites();
            for (int i = 0; i < retired.Count; i++) DestroySpriteDict(retired[i]);

            foreach (var kv in active) ApplySkinToPlayer(kv.Key, kv.Value);
            SkinStateVersion++;
            WoundViewAttacher.RefreshAll();
        }

        private static void ResetCharacterToVanillaBaseline(GameObject playerObj)
        {
            RestoreVanillaParts(playerObj);
            BloodAttacher.Restore(playerObj);
            AccessoryAttacher.Apply(playerObj, null, null, null);
            DestroyModContainers(playerObj);
        }

        private static void DestroySpriteDict(Dictionary<string, Sprite> dict)
        {
            if (dict == null) return;
            foreach (var kv in dict)
            {
                if (kv.Value == null) continue;
                if (kv.Value.texture != null) UnityEngine.Object.Destroy(kv.Value.texture);
                UnityEngine.Object.Destroy(kv.Value);
            }
        }
    }
}
