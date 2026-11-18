using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using HarmonyLib;
using SkinSyncMod.Interop;
using SkinSyncMod.Interop.Consumers;
using SkinSyncMod.Network;
using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>BepInEx 插件主入口，协调皮肤目录扫描、热键切换、配置面板与多人网络同步。</summary>
    [BepInPlugin("com.Bytechey.skinsync", "Skin Sync Mod", "1.2.3")]
    [BepInDependency(SaveManagerGuid, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(TogetherGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class SkinSync : BaseUnityPlugin
    {
        private const string SaveManagerGuid = "com.casualtiesUnknown.saveManager";
        private const string TogetherGuid = "CasualtiesMP";
        private const int TogetherRegisterMaxAttempts = 60;
        private const float TogetherRegisterRetrySeconds = 1f;

        internal static Harmony harmony;

        private readonly List<string> availableSkins = new List<string>();
        private int currentSkinIndex;

        private GameObject localPlayerObject;
        private object localNetBodyBox;
        private GameObject localChara;
        private bool _embeddedInSaveManager;

        internal static SkinSyncSettings Settings { get; private set; }
        internal static bool IsMultiplayerSession => SessionConsumer.IsAvailable;

        internal static string Version
        {
            get
            {
                if (_version != null) return _version;
                var attr = (BepInPlugin)System.Attribute.GetCustomAttribute(typeof(SkinSync), typeof(BepInPlugin));
                _version = attr != null ? attr.Version.ToString() : "";
                return _version;
            }
        }
        private static string _version;

        private static SkinSync _instance;

        private SkinSyncWindow _window;
        private InGameOverlay _overlay;

        private void Awake()
        {
            _instance = this;
            ModLog.Init(Logger);
            ModLog.Debug("[Build] Debug 构建，详细日志已启用");
            gameObject.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            harmony = new Harmony("com.Bytechey.skinsync");
            try { if (Directory.Exists(SkinPathResolver.SyncCacheRoot)) Directory.Delete(SkinPathResolver.SyncCacheRoot, true); }
            catch (System.Exception ex) { ModLog.Warning("clear SyncCacheRoot failed: " + ex.Message); }
            InteropBootstrap.Initialize();
            RegisterPatches();
            Settings = new SkinSyncSettings(Config);
            ModLog.ShowInConsole = Settings.ShowLogInConsole.Value;
            Settings.ShowLogInConsole.SettingChanged += (_, __) => ModLog.ShowInConsole = Settings.ShowLogInConsole.Value;
            Patches.UpdateChecker.Enabled = Settings.AcceptUpdateNotice.Value;
            Settings.AcceptUpdateNotice.SettingChanged += (_, __) => Patches.UpdateChecker.Enabled = Settings.AcceptUpdateNotice.Value;
            ScanAvailableSkins();
            if (!string.IsNullOrEmpty(Settings.CurrentSkin.Value))
            {
                ApplyTailOverrideToConfig(Settings.GetTailOverride(Settings.CurrentSkin.Value));
            }
            ModLog.Info($"SkinSync Mod loaded. Found {availableSkins.Count} skins. Together={(SessionConsumer.IsAvailable ? "yes" : "no")}.");
            Character.CharacterCatalog.EnsureLoaded();
            ModLog.Debug(Character.CharacterCatalog.DescribeAll());

            gameObject.AddComponent<Patches.SkinCacheBroadcaster>();
            gameObject.AddComponent<Patches.PendingSkinApplier>();
            gameObject.AddComponent<Patches.UpdateChecker>();
            gameObject.AddComponent<EquipmentHider>();
            EquipmentHider.Active = Settings.HideGameWearables.Value;
            gameObject.AddComponent<AccessoryEnforcer>();
            AccessoryEnforcer.Active = Settings.RequireEquipmentForAccessories.Value;
            BloodRenderConfig.Enabled = Settings.RenderCustomBlood.Value;
            Settings.RenderCustomBlood.SettingChanged += (_, __) => BloodRenderConfig.Enabled = Settings.RenderCustomBlood.Value;
            TailDeformConfig.Enabled = Settings.TailDeformEnabled.Value;
            Settings.TailDeformEnabled.SettingChanged += (_, __) =>
            {
                TailDeformConfig.Enabled = Settings.TailDeformEnabled.Value;
                string cur = ResolveCurrentSkin();
                if (!string.IsNullOrEmpty(cur)) BroadcastTailDelta(cur);
            };

            _overlay = new InGameOverlay();
            _window = new SkinSyncWindow(
                cfg: Settings,
                getSkins: () => new List<string>(availableSkins),
                getCurrentSkin: ResolveCurrentSkin,
                onApplySkin: ApplySkinByName,
                onRescan: () =>
                {
                    ScanAvailableSkins();
                    if (availableSkins.Count > 0 && !availableSkins.Contains(Settings.CurrentSkin.Value))
                    {
                        currentSkinIndex = 0;
                    }
                },
                getAccessories: GetCurrentAccessoryRows,
                onToggleAccessory: ToggleAccessory,
                onSetAccessoryTransform: SetAccessoryTransform,
                onSaveTail: SaveCurrentTailToSettings,
                onResetTail: ResetCurrentTailToDefaults,
                onOpened: () => UiBlocker.Block(),
                onClosed: () => UiBlocker.Unblock());

            bool saveManagerInstalled = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(SaveManagerGuid);
            _embeddedInSaveManager = saveManagerInstalled && Patches.SaveManagerTabBridge.Register(_window);
            if (!_embeddedInSaveManager)
            {
                MenuButtonInjector.Setup(() => _window.OpenPanel());
            }

            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
            Application.quitting += OnApplicationQuitting;
        }

        private void RegisterPatches()
        {
            Patches.LegacyTogetherSkinSuppressor.TrySuppress(harmony);

            harmony.CreateClassProcessor(typeof(SkinSyncAdaptiveButtonOverlayActiveGuard)).Patch();
            harmony.CreateClassProcessor(typeof(SkinSyncAdaptiveButtonClickedGuard)).Patch();
            harmony.CreateClassProcessor(typeof(SkinSyncPlayerCameraHandleInputGuard)).Patch();
            harmony.CreateClassProcessor(typeof(SkinSyncPlayerCameraMouseGuard)).Patch();
            harmony.CreateClassProcessor(typeof(SkinSyncPreRunScriptStartPatch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.BleedSpawnTranspiler)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.LimbAwakeBleedColorPatch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.FacialExpressionStartSkinPatch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.WoundViewStartSkinPatch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.WoundViewUpdateSkinPatch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.WoundViewAddImageSkinPatch)).Patch();

            StartCoroutine(InstallTogetherReceiverRoutine());
        }

        private System.Collections.IEnumerator InstallTogetherReceiverRoutine()
        {
            Patches.LegacyTogetherSkinSuppressor.TrySuppress(harmony);
            Patches.TogetherHooksBridge.TryBind();
            if (Patches.TogetherReceiverRegistrar.Register())
            {
                Patches.LegacyTogetherSkinSuppressor.TrySuppress(harmony);
                Patches.TogetherHooksBridge.TryBind();
                yield break;
            }

            for (int attempt = 0; attempt < TogetherRegisterMaxAttempts; attempt++)
            {
                yield return new WaitForSecondsRealtime(TogetherRegisterRetrySeconds);
                Patches.LegacyTogetherSkinSuppressor.TrySuppress(harmony);
                Patches.TogetherHooksBridge.TryBind();
                if (Patches.TogetherReceiverRegistrar.Register())
                {
                    Patches.LegacyTogetherSkinSuppressor.TrySuppress(harmony);
                    Patches.TogetherHooksBridge.TryBind();
                    yield break;
                }
            }

            ModLog.Warning("皮肤同步包在 " + TogetherRegisterMaxAttempts + " 秒内未登记成功：Together 未加载或缺少 16 位原语，本次会话皮肤同步不可用。");
        }

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            _window?.ClosePanel();
            _persistentApplied = null;
            _appliedTargetObject = null;
            _appliedCharType = -1;
            _localSkinReportedNetId = 0;
            _reportedLocalCharType = -1;
            localPlayerObject = null;
            localNetBodyBox = null;
            localChara = null;
            RosterConsumer.ResetLocalCache();
            Network.MultiplayerSender.ClearOutboundQueue();
            SkinApplier.OnSceneChanged();
            SkinMessageHandlers.OnSceneChanged();
        }

        private static bool _quitting;
        private void OnApplicationQuitting()
        {
            if (_quitting) return;
            _quitting = true;
            try { Patches.LegacyTogetherSkinSuppressor.Dispose(harmony); } catch { }
            try { Patches.TogetherHooksBridge.Dispose(); } catch { }
            try { MenuButtonInjector.Dispose(); } catch { }
            try { UiBlocker.Unblock(); } catch { }
            try { UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded; } catch { }
            try { Application.quitting -= OnApplicationQuitting; } catch { }
        }

        private void ScanAvailableSkins()
        {
            availableSkins.Clear();
            SkinPathResolver.Invalidate();
            BaseSizesLoader.Invalidate(null);
            SkinPreviewRenderer.InvalidateAll();

            string customSpritesPath = Path.Combine(Paths.PluginPath, "CustomSprites");
            if (!Directory.Exists(customSpritesPath))
            {
                ModLog.Warning($"CustomSprites folder not found at {customSpritesPath}");
                SkinApplier.ReloadAllCachedSkins();
                return;
            }

            var dirs = Directory.GetDirectories(customSpritesPath)
                                .Select(Path.GetFileName)
                                .Where(name => !string.IsNullOrEmpty(name)
                                    && name[0] != '_'
                                    && name[0] != '.'
                                    && !Patches.LegacyTogetherSkinSuppressor.IsLegacyContainerDir(customSpritesPath, name))
                                .ToList();
            dirs.Sort(NaturalCompare);
            availableSkins.AddRange(dirs);

            if (availableSkins.Count == 0)
                ModLog.Warning("No skin folders found in CustomSprites.");
            else
                ModLog.Info($"Available skins: {string.Join(", ", availableSkins)}");

            SkinApplier.ReloadAllCachedSkins();
        }

        /// <summary>当远端皮肤包持久化落盘后将其增量纳入可用皮肤列表并保持自然排序。</summary>
        internal static void NotifyDiskSkinsChanged(string skinID)
        {
            if (_instance == null || string.IsNullOrEmpty(skinID) || _instance.availableSkins.Contains(skinID))
            {
                return;
            }

            _instance.availableSkins.Add(skinID);
            _instance.availableSkins.Sort(NaturalCompare);
        }

        private static int NaturalCompare(string a, string b)
        {
            int ai = 0, bi = 0;
            while (ai < a.Length && bi < b.Length)
            {
                if (char.IsDigit(a[ai]) && char.IsDigit(b[bi]))
                {
                    int aend = ai; while (aend < a.Length && char.IsDigit(a[aend])) aend++;
                    int bend = bi; while (bend < b.Length && char.IsDigit(b[bend])) bend++;
                    if (long.TryParse(a.Substring(ai, aend - ai), out long an)
                        && long.TryParse(b.Substring(bi, bend - bi), out long bn))
                    {
                        if (an != bn) return an < bn ? -1 : 1;
                    }
                    ai = aend; bi = bend;
                }
                else
                {
                    char ca = char.ToLowerInvariant(a[ai]);
                    char cb = char.ToLowerInvariant(b[bi]);
                    if (ca != cb) return ca < cb ? -1 : 1;
                    ai++; bi++;
                }
            }
            return (a.Length - ai) - (b.Length - bi);
        }

        private void Update()
        {
            if (_quitting) return;
            Network.MultiplayerSender.TickOutboundChunks();
            UiBlocker.EnforceBlocked();

            if (!_embeddedInSaveManager && SkinSyncSettings.TriggeredThisFrame(Settings.TogglePanelHotkey))
            {
                if (_window.Open) _window.ClosePanel();
                else _window.OpenPanel();
            }

            if (availableSkins.Count == 0) return;

            if (!TryGetLocalPlayer())
                return;

            ApplyPersistentSkinIfNeeded();

            if (SkinSyncSettings.TriggeredThisFrame(Settings.NextSkinHotkey))
            {
                currentSkinIndex = (currentSkinIndex + 1) % availableSkins.Count;
                SwitchToSkin(availableSkins[currentSkinIndex]);
            }
            else if (SkinSyncSettings.TriggeredThisFrame(Settings.PrevSkinHotkey))
            {
                currentSkinIndex = (currentSkinIndex - 1 + availableSkins.Count) % availableSkins.Count;
                SwitchToSkin(availableSkins[currentSkinIndex]);
            }
            else if (SkinSyncSettings.TriggeredThisFrame(Settings.RescanSkinsHotkey))
            {
                ScanAvailableSkins();
                ModLog.Info("Rescanned skins.");
            }
        }

        private void LateUpdate()
        {
            if (_quitting || _embeddedInSaveManager) return;
            // 在 LateUpdate 处理 Esc 关闭，避免同帧提前解除阻塞触发游戏原生暂停菜单。
            if (_window != null && _window.Open && !_window.IsCapturingHotkey && Input.GetKeyDown(KeyCode.Escape))
            {
                _window.ClosePanel();
            }
        }

        private void OnGUI()
        {
            if (_quitting || _embeddedInSaveManager) return;
            _overlay.Draw(() => _window.OpenPanel());
            _window.Draw();
        }

        private void OnDisable()
        {
            try { _window?.ClosePanel(); } catch { }
            try { UiBlocker.Unblock(); } catch { }
        }

        private string _persistentApplied;
        private GameObject _appliedTargetObject;
        private int _appliedCharType = -1;

        private void ApplyPersistentSkinIfNeeded()
        {
            string cached = Settings.CurrentSkin.Value;
            if (string.IsNullOrEmpty(cached)) return;
            if (!availableSkins.Contains(cached)) return;

            GameObject activeTarget = localNetBodyBox != null ? localChara : localPlayerObject;
            int curCharType = ReadCharType(activeTarget);
            if (_persistentApplied == cached
                && activeTarget != null
                && _appliedTargetObject == activeTarget
                && _appliedCharType == curCharType)
            {
                return;
            }

            currentSkinIndex = availableSkins.IndexOf(cached);
            _switchAppliedToScene = false;
            SwitchToSkin(cached);
        }

        private static int ReadCharType(GameObject chara)
        {
            if (chara == null) return 0;
            Body body = chara.GetComponentInChildren<Body>(true);
            return body != null ? body.charType : 0;
        }

        internal void ApplySkinByName(string skinId)
        {
            if (string.IsNullOrEmpty(skinId)) return;
            int idx = availableSkins.IndexOf(skinId);
            if (idx < 0) return;
            currentSkinIndex = idx;
            SwitchToSkin(skinId);
        }

        private string ResolveCurrentSkin()
        {
            string s = Settings != null ? Settings.CurrentSkin.Value : null;
            if (string.IsNullOrEmpty(s)) return "";
            if (availableSkins.Contains(s)) return s;
            string dir = SkinPathResolver.GetSkinDir(s);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return s;
            if (SkinPackCodec.HasInMemory(s)) return s;
            return "";
        }

        internal static List<AccessoryConfigLoader.Entry> LoadAccessoryEntriesForSkin(string skin)
        {
            string skinDir = SkinPathResolver.GetSkinDir(skin);
            if (!string.IsNullOrEmpty(skinDir))
            {
                string path = Path.Combine(skinDir, "accessories.json");
                if (File.Exists(path)) return AccessoryConfigLoader.Load(path);
            }
            return SkinApplier.LoadJsonFromMemoryPack(skin, "accessories.json", AccessoryConfigLoader.Parse)
                ?? new List<AccessoryConfigLoader.Entry>();
        }

        internal List<SkinSyncWindow.AccessoryRow> GetCurrentAccessoryRows()
        {
            var rows = new List<SkinSyncWindow.AccessoryRow>();
            string skin = ResolveCurrentSkin();
            if (string.IsNullOrEmpty(skin)) return rows;
            var entries = LoadAccessoryEntriesForSkin(skin);
            foreach (var e in entries)
            {
                var ov = Settings.GetAccessoryOverride(skin, e.Id);
                rows.Add(new SkinSyncWindow.AccessoryRow
                {
                    Id = e.Id,
                    Enabled = ov?.Enabled ?? e.Enabled,
                    OffX = ov?.OffX ?? e.OffX,
                    OffY = ov?.OffY ?? e.OffY,
                    Rotation = ov?.Rotation ?? e.Rotation,
                    ZOrder = ov?.ZOrder ?? e.ZOrder,
                    RequireSlot = e.RequireWornSlot,
                });
            }
            return rows;
        }

        internal void ToggleAccessory(string accId, bool enabled)
        {
            string skin = ResolveCurrentSkin();
            if (string.IsNullOrEmpty(skin)) return;
            Settings.SetAccessoryEnabled(skin, accId, enabled);
            AccessoryAttacher.SetEnabled(accId, enabled);
            SkinPreviewRenderer.Invalidate(skin);
            BroadcastAccessoryDelta(skin, accId);
        }

        internal void SetAccessoryTransform(string accId, int offX, int offY, float rot, int z)
        {
            string skin = ResolveCurrentSkin();
            if (string.IsNullOrEmpty(skin)) return;
            Settings.SetAccessoryTransform(skin, accId, offX, offY, rot, z);
            AccessoryAttacher.SetTransform(accId, offX, offY, rot, z);
            SkinPreviewRenderer.Invalidate(skin);
            BroadcastAccessoryDelta(skin, accId);
        }

        private void BroadcastAccessoryDelta(string skin, string accId)
        {
            if (Settings == null || !Settings.SyncAccessories.Value) return;
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "PreGen") return;
            if (localNetBodyBox == null) return;

            var entries = LoadAccessoryEntriesForSkin(skin);
            AccessoryConfigLoader.Entry baseEntry = null;
            foreach (var e in entries) { if (e.Id == accId) { baseEntry = e; break; } }
            if (baseEntry == null) return;
            if (!TryGetLocalNetId(out uint netId)) return;
            var ov = Settings.GetAccessoryOverride(skin, accId);
            MultiplayerSender.SendAccessory(
                netId: netId,
                skinID: skin,
                accId: accId,
                enabled: ov?.Enabled ?? baseEntry.Enabled,
                offX: ov?.OffX ?? baseEntry.OffX,
                offY: ov?.OffY ?? baseEntry.OffY,
                rotation: ov?.Rotation ?? baseEntry.Rotation,
                zOrder: ov?.ZOrder ?? baseEntry.ZOrder);
        }

        /// <summary>将指定皮肤的尾巴覆盖参数写入全局 TailDeformConfig，传 null 时恢复默认值。</summary>
        internal static void ApplyTailOverrideToConfig(SkinSyncSettings.TailDeformOverride ov)
        {
            TailDeformConfig.FrontGuard = ov?.FrontGuard ?? true;
            TailDeformConfig.Segments = ov?.Segments ?? 10;
            TailDeformConfig.ConstraintIters = ov?.ConstraintIters ?? 6;
            TailDeformConfig.Damping = ov?.Damping ?? 0.45f;
            TailDeformConfig.SpeedDamping = ov?.SpeedDamping ?? 0.05f;
            TailDeformConfig.Stiffness = ov?.Stiffness ?? 1.0f;
            TailDeformConfig.MaxBendDeg = ov?.MaxBendDeg ?? 18f;
            TailDeformConfig.AnchorFollow = ov?.AnchorFollow ?? 1.0f;
            TailDeformConfig.Smoothness = ov?.Smoothness ?? 0.55f;
            TailDeformConfig.MaxStep = ov?.MaxStep ?? 0.15f;
            TailDeformConfig.MaxFixedDt = ov?.MaxFixedDt ?? 0.02f;
            TailDeformConfig.FrontGuardMargin = ov?.FrontGuardMargin ?? 0.05f;
            TailDeformConfig.GravityX = ov?.GravityX ?? 0f;
            TailDeformConfig.GravityY = ov?.GravityY ?? -1.2f;
            TailDeformConfig.WindFreq = ov?.WindFreq ?? 1.2f;
            TailDeformConfig.WindAmp = ov?.WindAmp ?? 0.012f;
            TailDeformConfig.SpeedDisturb = ov?.SpeedDisturb ?? 0.005f;
        }

        internal void SaveCurrentTailToSettings()
        {
            string skin = ResolveCurrentSkin();
            if (string.IsNullOrEmpty(skin)) return;
            var ov = new SkinSyncSettings.TailDeformOverride
            {
                FrontGuard = TailDeformConfig.FrontGuard,
                Segments = TailDeformConfig.Segments,
                ConstraintIters = TailDeformConfig.ConstraintIters,
                Damping = TailDeformConfig.Damping,
                SpeedDamping = TailDeformConfig.SpeedDamping,
                Stiffness = TailDeformConfig.Stiffness,
                MaxBendDeg = TailDeformConfig.MaxBendDeg,
                AnchorFollow = TailDeformConfig.AnchorFollow,
                Smoothness = TailDeformConfig.Smoothness,
                MaxStep = TailDeformConfig.MaxStep,
                MaxFixedDt = TailDeformConfig.MaxFixedDt,
                FrontGuardMargin = TailDeformConfig.FrontGuardMargin,
                GravityX = TailDeformConfig.GravityX,
                GravityY = TailDeformConfig.GravityY,
                WindFreq = TailDeformConfig.WindFreq,
                WindAmp = TailDeformConfig.WindAmp,
                SpeedDisturb = TailDeformConfig.SpeedDisturb,
            };
            Settings.SetTailOverride(skin, ov);
            BroadcastTailDelta(skin);
        }

        internal void ResetCurrentTailToDefaults()
        {
            string skin = ResolveCurrentSkin();
            if (string.IsNullOrEmpty(skin)) return;
            Settings.SetTailOverride(skin, null);
            ApplyTailOverrideToConfig(null);
            BroadcastTailDelta(skin);
        }

        private void BroadcastTailDelta(string skin)
        {
            if (Settings == null || !Settings.SyncTailDeform.Value) return;
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "PreGen") return;
            if (localNetBodyBox == null) return;
            if (!TryGetLocalNetId(out uint netId)) return;
            MultiplayerSender.SendTailFromConfig(netId, skin);
        }

        private bool TryGetLocalPlayer()
        {
            if (localNetBodyBox != null)
            {
                bool deadUnityObj = localNetBodyBox is UnityEngine.Object uo && uo == null;
                if (deadUnityObj || localChara == null)
                {
                    localNetBodyBox = null;
                    localChara = null;
                }
            }

            if (localNetBodyBox == null && SessionConsumer.IsAvailable)
            {
                if (RosterConsumer.TryGetLocalNetBody(out object box, out uint nid, out GameObject chara) && chara != null)
                {
                    localNetBodyBox = box;
                    localChara = chara;
                    ModLog.Info($"多人模式：找到本地 NetBody (netId {nid})");
                }
            }

            if (localNetBodyBox != null)
            {
                ReportLocalSkinIfNeeded();
                return true;
            }

            if (SessionConsumer.IsRunning)
            {
                return false;
            }

            if (localPlayerObject == null)
            {
                var cam = PlayerCamera.main;
                Body playerBody = cam != null && cam.body != null ? cam.body : FindObjectOfType<Body>();
                if (playerBody != null)
                {
                    localPlayerObject = playerBody.transform.parent?.gameObject ?? playerBody.gameObject;
                    ModLog.Info($"单机模式：通过 Body 找到本地玩家：{localPlayerObject.name}");
                }
                else
                {
                    localPlayerObject = GameObject.FindGameObjectWithTag("Player");
                    if (localPlayerObject != null)
                        ModLog.Info($"单机模式：通过 Tag 找到本地玩家：{localPlayerObject.name}");
                }
            }

            return localPlayerObject != null;
        }

        private static bool TryGetLocalNetId(out uint netId)
        {
            if (RosterConsumer.TryGetLocalNetBody(out _, out netId, out _) && netId != 0)
            {
                return true;
            }

            netId = 0;
            return false;
        }

        private void SwitchToSkin(string skinID)
        {
            if (Settings != null)
            {
                Settings.CurrentSkin.Value = skinID;
                ApplyTailOverrideToConfig(Settings.GetTailOverride(skinID));
            }

            try
            {
                ulong steamId = RosterConsumer.TryGetLocalSteamId();
                if (steamId != 0UL) SkinCacheStore.Set(steamId, skinID);
            }
            catch (System.Exception ex)
            {
                ModLog.Warning("write SkinCacheStore failed: " + ex.Message);
            }

            SkinPreviewRenderer.Invalidate(skinID);

            if (localNetBodyBox != null)
            {
                SkinApplier.ApplySkinToPlayer(localChara, skinID);
                _appliedTargetObject = localChara;
                _appliedCharType = ReadCharType(localChara);
                if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "PreGen")
                {
                    if (TryGetLocalNetId(out uint netId))
                    {
                        MultiplayerSender.SendSkinChange(netId, skinID);
                        SyncSavedOverridesOnEnter(skinID);
                        _localSkinReportedNetId = netId;
                        _reportedLocalCharType = _appliedCharType;
                        ModLog.Info($"角色加载模式：多人 - 切到 {skinID} 并已广播 (netId {netId})");
                    }
                    else
                    {
                        ModLog.Info($"角色加载模式：多人 - 切到 {skinID}，等本地 netId 分配后补报");
                    }
                }
                else
                {
                    ModLog.Info($"角色加载模式：多人主菜单 - 仅本地应用 {skinID}，不广播");
                }
            }
            else if (localPlayerObject != null)
            {
                SkinApplier.ApplySkinToPlayer(localPlayerObject, skinID);
                _appliedTargetObject = localPlayerObject;
                _appliedCharType = ReadCharType(localPlayerObject);
                ModLog.Info($"角色加载模式：单机 - 切到 {skinID}（基于本地玩家）");
            }
            else
            {
                int n = SkinApplier.ApplyToScene(skinID);
                if (n > 0)
                {
                    ModLog.Info($"角色加载模式：单机兜底 - 切到 {skinID}（场景扫到 {n} 个 Body）");
                    _switchAppliedToScene = true;
                }
                else
                {
                    _switchAppliedToScene = false;
                }
            }

            if (localNetBodyBox != null || localPlayerObject != null || _switchAppliedToScene)
            {
                _persistentApplied = skinID;
            }
        }

        private bool _switchAppliedToScene;
        private uint _localSkinReportedNetId;
        private int _reportedLocalCharType = -1;

        private void ReportLocalSkinIfNeeded()
        {
            if (localNetBodyBox == null) return;
            if (!TryGetLocalNetId(out uint netId)) return;
            int curCharType = ReadCharType(localChara);
            if (_localSkinReportedNetId == netId && _reportedLocalCharType == curCharType) return;
            string cached = Settings != null ? Settings.CurrentSkin.Value : null;
            if (string.IsNullOrEmpty(cached)) return;
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "PreGen") return;
            MultiplayerSender.SendSkinChange(netId, cached);
            SyncSavedOverridesOnEnter(cached);
            _localSkinReportedNetId = netId;
            _reportedLocalCharType = curCharType;
            ModLog.Info($"多人模式：本地皮肤已上报 (netId {netId}, charType {curCharType} -> {cached})");
        }

        /// <summary>响应联机角色生成或同 netId 换物种重建网格通知，为新躯体恢复对应皮肤与覆盖参数。</summary>
        internal static void OnExternalBodyReadyOrRebuilt(Body body, uint netId, int charType, bool isRebuild)
        {
            if (body == null) return;

            GameObject chara = null;
            bool isLocal = false;
            if (netId != 0)
            {
                RosterConsumer.TryGetNetBodyFromId(netId, out _, out chara, out isLocal);
            }
            else
            {
                RosterConsumer.TryResolveBodyIdentity(body, out netId, out chara, out isLocal);
            }

            GameObject targetGo = chara != null ? chara : body.gameObject;
            int resolvedCharType = charType >= 0 ? charType : body.charType;

            if (isLocal)
            {
                if (_instance == null) return;
                if (RosterConsumer.TryGetLocalNetBody(out object freshBox, out _, out _) && freshBox != null)
                {
                    _instance.localNetBodyBox = freshBox;
                }
                _instance.localChara = targetGo;

                string localSkin = _instance.ResolveCurrentSkin();
                if (string.IsNullOrEmpty(localSkin)) return;

                if (!isRebuild
                    && _instance._appliedTargetObject == targetGo
                    && _instance._appliedCharType == resolvedCharType
                    && _instance._persistentApplied == localSkin
                    && SkinApplier.GetAssignedSkin(targetGo) == localSkin)
                {
                    _instance.ReportLocalSkinIfNeeded();
                    return;
                }

                _instance._appliedTargetObject = targetGo;
                _instance._appliedCharType = resolvedCharType;
                _instance._persistentApplied = localSkin;
                SkinApplier.ApplySkinToPlayer(targetGo, localSkin, resolvedCharType);
                _instance.ReportLocalSkinIfNeeded();
                return;
            }

            if (netId == 0 || !SkinMessageHandlers.TryGetRecordedRemoteSkin(netId, out string remoteSkin)) return;
            if (!isRebuild && SkinApplier.GetAssignedSkin(targetGo) == remoteSkin) return;

            AccessoryAttacher.BindPendingOverrides(netId, targetGo);
            TailDeformConfig.BindPendingOverrides(netId, targetGo);
            SkinApplier.ApplySkinToPlayer(targetGo, remoteSkin, resolvedCharType);
            ModLog.Info($"同 netId 躯体重建恢复：netId {netId} (charType {resolvedCharType}) -> {remoteSkin}");
        }

        private void SyncSavedOverridesOnEnter(string skin)
        {
            if (Settings == null || Settings.SyncMode.Value == "Passive") return;

            var accOverrides = Settings.GetAllAccessoryOverridesForSkin(skin);
            if (Settings.SyncAccessories.Value && accOverrides != null && accOverrides.Count > 0)
            {
                foreach (var kv in accOverrides)
                {
                    BroadcastAccessoryDelta(skin, kv.Key);
                }
            }

            if (Settings.SyncTailDeform.Value && (Settings.GetTailOverride(skin) != null || !Settings.TailDeformEnabled.Value))
            {
                BroadcastTailDelta(skin);
            }
        }
    }
}
