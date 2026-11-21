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
    [BepInPlugin(PluginGuid, "Skin Sync Mod", BuildInfo.Version)]
    [BepInDependency(SaveManagerGuid, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(TogetherPluginGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class SkinSync : BaseUnityPlugin
    {
        internal const string PluginGuid = "com.Bytechey.skinsync";
        internal const string TogetherPluginGuid = "CasualtiesMP";
        private const string SaveManagerGuid = "com.casualtiesUnknown.saveManager";
        private const int TogetherRegisterMaxAttempts = 60;
        private const float TogetherRegisterRetrySeconds = 1f;
        private const float LocalPlayerSearchCooldown = 0.25f;

        internal static Harmony harmony;
        internal static bool TogetherInstalled { get; private set; } = true;

        private readonly List<string> availableSkins = new List<string>();
        private int currentSkinIndex;

        private GameObject localPlayerObject;
        private Body _localBody;
        private float _nextPlayerSearchTime;
        private object localNetBodyBox;
        private GameObject localChara;
        private bool _embeddedInSaveManager;
        private string _activeSceneName = "";

        private List<AccessoryConfigLoader.Entry> _cachedAccEntries;
        private string _cachedAccEntriesSkin;
        private List<SkinSyncWindow.AccessoryRow> _cachedAccRows;
        private string _cachedAccRowsSkin;

        internal static SkinSyncSettings Settings { get; private set; }
        internal static bool IsMultiplayerSession => TogetherInstalled && SessionConsumer.IsAvailable;

        internal static string Version
        {
            get
            {
                if (_version != null) return _version;
                var attr = (BepInPlugin)System.Attribute.GetCustomAttribute(typeof(SkinSync), typeof(BepInPlugin));
                return _version = attr != null ? attr.Version.ToString() : "";
            }
        }
        private static string _version;
        private static SkinSync _instance;

        private SkinSyncWindow _window;
        private InGameOverlay _overlay;
        private System.Action _openWindowAction;

        private void Awake()
        {
            _instance = this;
            // 默认 BepInEx HideManagerGameObject=false，单装模组切场景会销毁共享宿主，强制设 HideAndDontSave 与 DontDestroyOnLoad 保活 OnGUI/Update 及 _instance
            gameObject.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            ModLog.Init(Logger);
            ModLog.Debug("[Build] Debug 构建，详细日志已启用");
            TogetherInstalled = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(TogetherPluginGuid);
            _activeSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

            DeleteDirectorySafely(SkinPathResolver.SyncCacheRoot);
            DeleteDirectorySafely(SkinPathResolver.LegacySyncCacheRoot);

            Settings = new SkinSyncSettings(Config);
            ModLog.ShowInConsole = Settings.ShowLogInConsole.Value;
            Settings.ShowLogInConsole.SettingChanged += (_, __) => ModLog.ShowInConsole = Settings.ShowLogInConsole.Value;
            Patches.UpdateChecker.Enabled = Settings.AcceptUpdateNotice.Value;
            Settings.AcceptUpdateNotice.SettingChanged += (_, __) => Patches.UpdateChecker.Enabled = Settings.AcceptUpdateNotice.Value;
            EquipmentHider.Active = Settings.HideGameWearables.Value;
            Settings.HideGameWearables.SettingChanged += (_, __) => EquipmentHider.Active = Settings.HideGameWearables.Value;
            AccessoryEnforcer.Active = Settings.RequireEquipmentForAccessories.Value;
            Settings.RequireEquipmentForAccessories.SettingChanged += (_, __) => AccessoryEnforcer.Active = Settings.RequireEquipmentForAccessories.Value;
            BloodRenderConfig.Enabled = Settings.RenderCustomBlood.Value;
            Settings.RenderCustomBlood.SettingChanged += (_, __) => { BloodRenderConfig.Enabled = Settings.RenderCustomBlood.Value; SkinApplier.RefreshAll(); };
            TailDeformConfig.Enabled = Settings.TailDeformEnabled.Value;
            Settings.TailDeformEnabled.SettingChanged += (_, __) =>
            {
                TailDeformConfig.Enabled = Settings.TailDeformEnabled.Value;
                string cur = ResolveCurrentSkin();
                if (!string.IsNullOrEmpty(cur)) BroadcastTailDelta(cur);
            };
            Settings.VanillaSkinForSelf.SettingChanged += (_, __) => SkinApplier.RefreshAll();
            Settings.VanillaSkinForOthers.SettingChanged += (_, __) => SkinApplier.RefreshAll();
            Settings.PreferredLanguage.SettingChanged += (_, __) => { SkinSyncI18n.InvalidateCache(); MenuButtonInjector.RefreshLabel(); };

            _overlay = new InGameOverlay();
            _window = new SkinSyncWindow(
                Settings, () => availableSkins, ResolveCurrentSkin, ApplySkinByName,
                () =>
                {
                    ScanAvailableSkins();
                    if (availableSkins.Count > 0 && !availableSkins.Contains(Settings.CurrentSkin.Value)) currentSkinIndex = 0;
                },
                GetCurrentAccessoryRows, ToggleAccessory, SetAccessoryTransform,
                SaveCurrentTailToSettings, ResetCurrentTailToDefaults,
                () => UiBlocker.Block(), () => UiBlocker.Unblock());
            _openWindowAction = () => _window?.OpenPanel();

            bool saveManagerInstalled = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(SaveManagerGuid);
            _embeddedInSaveManager = saveManagerInstalled && Patches.SaveManagerTabBridge.Register(_window);
            if (!_embeddedInSaveManager) MenuButtonInjector.Setup(_openWindowAction);

            if (TogetherInstalled)
            {
                InteropBootstrap.Initialize();
                gameObject.AddComponent<Patches.SkinCacheBroadcaster>();
                gameObject.AddComponent<Patches.PendingSkinApplier>();
            }
            else ModLog.Info("未检测到 Together 联机模组，多人皮肤同步功能已停用。");

            gameObject.AddComponent<Patches.UpdateChecker>();
            gameObject.AddComponent<EquipmentHider>();
            gameObject.AddComponent<AccessoryEnforcer>();

            harmony = new Harmony(PluginGuid);
            RegisterPatches();

            ScanAvailableSkins();
            if (!string.IsNullOrEmpty(Settings.CurrentSkin.Value))
                ApplyTailOverrideToConfig(Settings.GetTailOverride(Settings.CurrentSkin.Value));
            ModLog.Info($"SkinSync Mod loaded. Found {availableSkins.Count} skins. Together={(IsMultiplayerSession ? "yes" : "no")}.");
            Character.CharacterCatalog.EnsureLoaded();
            ModLog.Debug(Character.CharacterCatalog.DescribeAll());

            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
            Application.quitting += OnApplicationQuitting;
        }

        private static void DeleteDirectorySafely(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { if (Directory.Exists(path)) Directory.Delete(path, true); }
            catch (System.Exception ex) { ModLog.Warning($"清理缓存目录失败 ({path})：{ex.Message}"); }
        }

        private void RegisterPatches()
        {
            if (TogetherInstalled) Patches.LegacyTogetherSkinSuppressor.TrySuppress(harmony);
            PatchSafely(typeof(SkinSyncAdaptiveButtonOverlayActiveGuard));
            PatchSafely(typeof(SkinSyncAdaptiveButtonClickedGuard));
            PatchSafely(typeof(SkinSyncPlayerCameraHandleInputGuard));
            PatchSafely(typeof(SkinSyncPlayerCameraMouseGuard));
            PatchSafely(typeof(SkinSyncPreRunScriptStartPatch));
            PatchSafely(typeof(Patches.BleedSpawnTranspiler));
            PatchSafely(typeof(Patches.LimbAwakeBleedColorPatch));
            PatchSafely(typeof(Patches.FacialExpressionStartSkinPatch));
            PatchSafely(typeof(Patches.WoundViewStartSkinPatch));
            PatchSafely(typeof(Patches.WoundViewUpdateSkinPatch));
            PatchSafely(typeof(Patches.WoundViewAddImageSkinPatch));
            if (TogetherInstalled) StartCoroutine(InstallTogetherReceiverRoutine());
        }

        private void PatchSafely(System.Type patchType)
        {
            try { harmony.CreateClassProcessor(patchType).Patch(); }
            catch (System.Exception ex) { ModLog.Warning($"补丁 {patchType.Name} 安装失败：{ex.Message}"); }
        }

        private System.Collections.IEnumerator InstallTogetherReceiverRoutine()
        {
            if (TryBindAndRegisterTogether()) yield break;
            for (int attempt = 0; attempt < TogetherRegisterMaxAttempts; attempt++)
            {
                yield return new WaitForSecondsRealtime(TogetherRegisterRetrySeconds);
                if (TryBindAndRegisterTogether()) yield break;
            }
            ModLog.Warning("皮肤同步包在 " + TogetherRegisterMaxAttempts + " 秒内未登记成功：Together 未加载或缺少 16 位原语，本次会话皮肤同步不可用。");
        }

        private bool TryBindAndRegisterTogether()
        {
            Patches.LegacyTogetherSkinSuppressor.TrySuppress(harmony);
            Patches.TogetherHooksBridge.TryBind();
            if (!Patches.TogetherReceiverRegistrar.Register()) return false;
            Patches.LegacyTogetherSkinSuppressor.TrySuppress(harmony);
            Patches.TogetherHooksBridge.TryBind();
            return true;
        }

        private bool _worldReadySentForScene;
        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            if (mode == UnityEngine.SceneManagement.LoadSceneMode.Additive) return;
            _activeSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            bool preserveSession = TogetherInstalled && SessionConsumer.IsRunning && scene.name != "PreGen";
            _worldReadySentForScene = preserveSession && !SessionConsumer.IsServer;
            _window?.ClosePanel();
            _persistentApplied = null;
            _appliedTargetObject = null;
            _appliedCharType = -1;
            _localSkinReportedNetId = 0;
            _reportedLocalChara = null;
            _reportedLocalCharType = -1;
            localPlayerObject = null;
            _localBody = null;
            _nextPlayerSearchTime = 0f;
            localNetBodyBox = null;
            localChara = null;
            if (TogetherInstalled)
            {
                RosterConsumer.ResetLocalCache();
                if (!preserveSession) Network.MultiplayerSender.ClearOutboundQueue();
                SkinMessageHandlers.OnSceneChanged(preserveSession);
                if (_worldReadySentForScene) Network.MultiplayerSender.SendClientWorldReady();
            }
            SkinApplier.OnSceneChanged(preserveSession);
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
            InvalidateAccessoryRowsCache();
            SkinPathResolver.Invalidate();
            BaseSizesLoader.Invalidate(null);
            ZonesConfigLoader.Invalidate(null);
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
                .Where(n => !string.IsNullOrEmpty(n) && n[0] != '_' && n[0] != '.'
                    && !Patches.LegacyTogetherSkinSuppressor.IsLegacyContainerDir(customSpritesPath, n))
                .ToList();
            dirs.Sort(NaturalCompare);
            availableSkins.AddRange(dirs);

            if (availableSkins.Count == 0) ModLog.Warning("No skin folders found in CustomSprites.");
            else ModLog.Info($"Available skins: {string.Join(", ", availableSkins)}");
            SkinApplier.ReloadAllCachedSkins();
        }

        /// <summary>当远端皮肤包持久化落盘后将其增量纳入可用皮肤列表并保持自然排序。</summary>
        internal static void NotifyDiskSkinsChanged(string skinID)
        {
            if (_instance == null || string.IsNullOrEmpty(skinID) || _instance.availableSkins.Contains(skinID)) return;
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
            if (_quitting || Settings == null || _window == null) return;
            if (TogetherInstalled) Network.MultiplayerSender.TickOutboundChunks();
            UiBlocker.EnforceBlocked();

            bool suppressHotkeys = _window.IsCapturingHotkey || _window.WasHotkeyCapturedThisFrame;
            if (!suppressHotkeys && !_embeddedInSaveManager && SkinSyncSettings.TriggeredThisFrame(Settings.TogglePanelHotkey))
            {
                if (_window.Open) _window.ClosePanel();
                else _window.OpenPanel();
                suppressHotkeys = true;
            }

            bool allowActionHotkeys = !suppressHotkeys && !_window.Open;
            if (allowActionHotkeys && SkinSyncSettings.TriggeredThisFrame(Settings.RescanSkinsHotkey))
            {
                ScanAvailableSkins();
                ModLog.Info("Rescanned skins.");
            }

            if (availableSkins.Count == 0 || !TryGetLocalPlayer()) return;

            ApplyPersistentSkinIfNeeded();

            if (!allowActionHotkeys) return;
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
        }

        private void LateUpdate()
        {
            if (_quitting || _embeddedInSaveManager || _window == null) return;
            // 在 LateUpdate 处理 Esc 关闭，避免同帧提前解除阻塞触发游戏原生暂停菜单。
            if (_window.Open && !_window.IsCapturingHotkey && !_window.WasHotkeyCapturedThisFrame && Input.GetKeyDown(KeyCode.Escape))
                _window.ClosePanel();
        }

        private void OnGUI()
        {
            if (_quitting || _embeddedInSaveManager || _overlay == null || _window == null) return;
            try
            {
                _overlay.Draw(_openWindowAction);
                _window.Draw();
            }
            catch (ExitGUIException) { throw; }
            catch (System.Exception ex) { ModLog.Warning($"SkinSync.OnGUI 异常：{ex.Message}"); }
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

            GameObject activeTarget = localNetBodyBox != null ? localChara : localPlayerObject;
            int curCharType = ReadLocalCharType(activeTarget);
            if (_persistentApplied == cached && activeTarget != null && _appliedTargetObject == activeTarget && _appliedCharType == curCharType)
                return;

            int idx = availableSkins.IndexOf(cached);
            if (idx < 0) return;
            currentSkinIndex = idx;
            _switchAppliedToScene = false;
            SwitchToSkin(cached);
        }

        private int ReadLocalCharType(GameObject chara)
        {
            if (chara == null) return 0;
            if ((chara == localChara || chara == localPlayerObject) && _localBody != null) return _localBody.charType;
            Body body = chara.GetComponentInChildren<Body>(true);
            if (chara == localChara || chara == localPlayerObject) _localBody = body;
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

        internal static string ResolveLocalEffectiveSkinForServer()
            => _instance == null || Settings == null || Settings.VanillaSkinForSelf.Value ? "" : _instance.ResolveCurrentSkin();

        private string ResolveCurrentSkin()
        {
            string s = Settings != null ? Settings.CurrentSkin.Value : null;
            if (string.IsNullOrEmpty(s)) return "";
            if (availableSkins.Contains(s)) return s;
            string dir = SkinPathResolver.GetSkinDir(s);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return s;
            return SkinPackCodec.HasInMemory(s) ? s : "";
        }

        /// <summary>使配件行缓存与配件基础定义缓存失效；skinID 为空时清除全部，否则仅清除匹配皮肤。</summary>
        internal static void InvalidateAccessoryRowsCache(string skinID = null)
        {
            if (_instance == null) return;
            if (skinID == null || _instance._cachedAccRowsSkin == skinID) { _instance._cachedAccRows = null; _instance._cachedAccRowsSkin = null; }
            if (skinID == null || _instance._cachedAccEntriesSkin == skinID) { _instance._cachedAccEntries = null; _instance._cachedAccEntriesSkin = null; }
        }

        internal static List<AccessoryConfigLoader.Entry> LoadAccessoryEntriesForSkin(string skin)
        {
            if (_instance != null && _instance._cachedAccEntries != null && _instance._cachedAccEntriesSkin == skin)
                return _instance._cachedAccEntries;
            string skinDir = SkinPathResolver.GetSkinDir(skin);
            List<AccessoryConfigLoader.Entry> loaded = null;
            if (!string.IsNullOrEmpty(skinDir))
            {
                string path = Path.Combine(skinDir, "accessories.json");
                if (File.Exists(path)) loaded = AccessoryConfigLoader.Load(path);
            }
            loaded ??= SkinApplier.LoadJsonFromMemoryPack(skin, "accessories.json", AccessoryConfigLoader.Parse)
                ?? new List<AccessoryConfigLoader.Entry>();
            if (_instance != null && !string.IsNullOrEmpty(skin)) { _instance._cachedAccEntries = loaded; _instance._cachedAccEntriesSkin = skin; }
            return loaded;
        }

        internal IReadOnlyList<SkinSyncWindow.AccessoryRow> GetCurrentAccessoryRows()
        {
            string skin = ResolveCurrentSkin();
            if (string.IsNullOrEmpty(skin)) return System.Array.Empty<SkinSyncWindow.AccessoryRow>();
            if (_cachedAccRows != null && _cachedAccRowsSkin == skin) return _cachedAccRows;

            var entries = LoadAccessoryEntriesForSkin(skin);
            var rows = new List<SkinSyncWindow.AccessoryRow>(entries.Count);
            foreach (var e in entries)
            {
                var ov = Settings.GetAccessoryOverride(skin, e.Id);
                rows.Add(new SkinSyncWindow.AccessoryRow
                {
                    Id = e.Id, Enabled = ov?.Enabled ?? e.Enabled,
                    OffX = ov?.OffX ?? e.OffX, OffY = ov?.OffY ?? e.OffY,
                    Rotation = ov?.Rotation ?? e.Rotation, ZOrder = ov?.ZOrder ?? e.ZOrder,
                    RequireSlot = e.RequireWornSlot,
                });
            }
            _cachedAccRows = rows;
            _cachedAccRowsSkin = skin;
            return rows;
        }

        internal void ToggleAccessory(string accId, bool enabled)
        {
            string skin = ResolveCurrentSkin();
            if (string.IsNullOrEmpty(skin)) return;
            Settings.SetAccessoryEnabled(skin, accId, enabled);
            _cachedAccRows = null;
            _cachedAccRowsSkin = null;
            AccessoryAttacher.SetEnabled(accId, enabled);
            SkinPreviewRenderer.Invalidate(skin);
            BroadcastAccessoryDelta(skin, accId);
        }

        internal void SetAccessoryTransform(string accId, int offX, int offY, float rot, int z)
        {
            string skin = ResolveCurrentSkin();
            if (string.IsNullOrEmpty(skin)) return;
            Settings.SetAccessoryTransform(skin, accId, offX, offY, rot, z);
            _cachedAccRows = null;
            _cachedAccRowsSkin = null;
            AccessoryAttacher.SetTransform(accId, offX, offY, rot, z);
            SkinPreviewRenderer.Invalidate(skin);
            BroadcastAccessoryDelta(skin, accId);
        }

        private void BroadcastAccessoryDelta(string skin, string accId)
        {
            if (Settings == null || !Settings.SyncAccessories.Value || _activeSceneName == "PreGen" || localNetBodyBox == null) return;
            var entries = LoadAccessoryEntriesForSkin(skin);
            AccessoryConfigLoader.Entry baseEntry = null;
            foreach (var e in entries) { if (e.Id == accId) { baseEntry = e; break; } }
            if (baseEntry == null || !TryGetLocalNetId(out uint netId)) return;
            var ov = Settings.GetAccessoryOverride(skin, accId);
            MultiplayerSender.SendAccessory(netId, skin, accId, ov?.Enabled ?? baseEntry.Enabled,
                ov?.OffX ?? baseEntry.OffX, ov?.OffY ?? baseEntry.OffY, ov?.Rotation ?? baseEntry.Rotation, ov?.ZOrder ?? baseEntry.ZOrder);
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
            if (_activeSceneName == "PreGen" || localNetBodyBox == null) return;
            if (TryGetLocalNetId(out uint netId)) MultiplayerSender.SendTailFromConfig(netId, skin);
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
                    _localBody = null;
                }
            }

            if (localNetBodyBox == null && IsMultiplayerSession)
            {
                if (RosterConsumer.TryGetLocalNetBody(out object box, out uint nid, out GameObject chara)
                    && box != null && nid != 0 && chara != null)
                {
                    localNetBodyBox = box;
                    localChara = chara;
                    _localBody = chara.GetComponentInChildren<Body>(true);
                    ModLog.Info($"多人模式：找到本地 NetBody (netId {nid})");
                }
            }

            if (localNetBodyBox != null)
            {
                localPlayerObject = null;
                ReportLocalSkinIfNeeded();
                return true;
            }

            if (TogetherInstalled && SessionConsumer.IsRunning)
            {
                localPlayerObject = null;
                _localBody = null;
                return false;
            }

            if (localPlayerObject != null)
            {
                if (_localBody == null) _localBody = localPlayerObject.GetComponentInChildren<Body>(true);
                return true;
            }

            if (Time.unscaledTime < _nextPlayerSearchTime) return false;
            _nextPlayerSearchTime = Time.unscaledTime + LocalPlayerSearchCooldown;

            var cam = PlayerCamera.main;
            Body playerBody = cam != null && cam.body != null ? cam.body : FindObjectOfType<Body>();
            if (playerBody != null)
            {
                _localBody = playerBody;
                localPlayerObject = playerBody.transform.parent?.gameObject ?? playerBody.gameObject;
                ModLog.Info($"单机模式：通过 Body 找到本地玩家：{localPlayerObject.name}");
            }
            else
            {
                localPlayerObject = GameObject.FindGameObjectWithTag("Player");
                if (localPlayerObject != null)
                {
                    _localBody = localPlayerObject.GetComponentInChildren<Body>(true);
                    ModLog.Info($"单机模式：通过 Tag 找到本地玩家：{localPlayerObject.name}");
                }
            }
            return localPlayerObject != null;
        }

        private static bool TryGetLocalNetId(out uint netId)
        {
            if (TogetherInstalled && RosterConsumer.TryGetLocalNetBody(out _, out netId, out _) && netId != 0) return true;
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
            InvalidateAccessoryRowsCache();
            bool mpRunning = TogetherInstalled && SessionConsumer.IsRunning;

            if (localNetBodyBox != null)
            {
                SkinApplier.ApplySkinToPlayer(localChara, skinID);
                _appliedTargetObject = localChara;
                _appliedCharType = ReadLocalCharType(localChara);
                if (_activeSceneName != "PreGen")
                {
                    if (TryGetLocalNetId(out uint netId))
                    {
                        MultiplayerSender.SendSkinChange(netId, skinID);
                        SyncSavedOverridesOnEnter(skinID);
                        _localSkinReportedNetId = netId;
                        _reportedLocalChara = localChara;
                        _reportedLocalCharType = _appliedCharType;
                        ModLog.Info($"角色加载模式：多人 - 切到 {skinID} 并已广播 (netId {netId})");
                    }
                    else
                    {
                        _localSkinReportedNetId = 0;
                        _reportedLocalChara = null;
                        ModLog.Info($"角色加载模式：多人 - 切到 {skinID}，等本地 netId 分配后补报");
                    }
                }
                else
                {
                    ModLog.Info($"角色加载模式：多人主菜单 - 仅本地应用 {skinID}，不广播");
                }
            }
            else if (localPlayerObject != null && !mpRunning)
            {
                SkinApplier.ApplySkinToPlayer(localPlayerObject, skinID);
                _appliedTargetObject = localPlayerObject;
                _appliedCharType = ReadLocalCharType(localPlayerObject);
                ModLog.Info($"角色加载模式：单机 - 切到 {skinID}（基于本地玩家）");
            }
            else if (!mpRunning)
            {
                int n = SkinApplier.ApplyToScene(skinID);
                _switchAppliedToScene = n > 0;
                if (n > 0) ModLog.Info($"角色加载模式：单机兜底 - 切到 {skinID}（场景扫到 {n} 个 Body）");
            }
            else
            {
                _switchAppliedToScene = false;
            }

            if (localNetBodyBox != null || (localPlayerObject != null && !mpRunning) || _switchAppliedToScene)
                _persistentApplied = skinID;
        }

        private bool _switchAppliedToScene;
        private uint _localSkinReportedNetId;
        private GameObject _reportedLocalChara;
        private int _reportedLocalCharType = -1;

        private void ReportLocalSkinIfNeeded()
        {
            if (localNetBodyBox == null || localChara == null || _activeSceneName == "PreGen") return;
            if (SessionConsumer.IsRunning && !SessionConsumer.IsServer && !_worldReadySentForScene)
            {
                Network.MultiplayerSender.SendClientWorldReady();
                _worldReadySentForScene = true;
            }
            int curCharType = ReadLocalCharType(localChara);
            if (_reportedLocalChara == localChara && _reportedLocalCharType == curCharType && _localSkinReportedNetId != 0) return;
            string cached = Settings != null ? Settings.CurrentSkin.Value : null;
            if (string.IsNullOrEmpty(cached) || !TryGetLocalNetId(out uint netId)) return;
            if (_localSkinReportedNetId == netId && _reportedLocalCharType == curCharType && _reportedLocalChara == localChara) return;

            MultiplayerSender.SendSkinChange(netId, cached);
            SyncSavedOverridesOnEnter(cached);
            _localSkinReportedNetId = netId;
            _reportedLocalChara = localChara;
            _reportedLocalCharType = curCharType;
            ModLog.Info($"多人模式：本地皮肤已上报 (netId {netId}, charType {curCharType} -> {cached})");
        }

        /// <summary>响应联机角色生成或同 netId 换物种重建网格通知，为新躯体恢复对应皮肤与覆盖参数。</summary>
        internal static void OnExternalBodyReadyOrRebuilt(Body body, uint netId, int charType, bool isRebuild)
        {
            if (body == null) return;

            GameObject chara = null;
            bool isLocal = false;
            if (netId != 0) RosterConsumer.TryGetNetBodyFromId(netId, out _, out chara, out isLocal);
            else RosterConsumer.TryResolveBodyIdentity(body, out netId, out chara, out isLocal);

            GameObject targetGo = chara != null ? chara : body.gameObject;
            int resolvedCharType = charType >= 0 ? charType : body.charType;

            if (isLocal)
            {
                if (_instance == null) return;
                if (RosterConsumer.TryGetLocalNetBody(out object freshBox, out _, out _) && freshBox != null)
                    _instance.localNetBodyBox = freshBox;
                _instance.localChara = targetGo;
                _instance._localBody = body;

                string localSkin = _instance.ResolveCurrentSkin();
                if (string.IsNullOrEmpty(localSkin)) return;

                if (!isRebuild && _instance._appliedTargetObject == targetGo && _instance._appliedCharType == resolvedCharType
                    && _instance._persistentApplied == localSkin && SkinApplier.GetAssignedSkin(targetGo) == localSkin)
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

            if (netId == 0 || !SkinMessageHandlers.TryGetRecordedRemoteSkin(netId, out string remoteSkin))
            {
                SkinApplier.EnsureUnassignedRemoteVanilla(targetGo, resolvedCharType);
                return;
            }
            if (!isRebuild && SkinApplier.GetAssignedSkin(targetGo) == remoteSkin) return;

            AccessoryAttacher.BindPendingOverrides(netId, targetGo);
            TailDeformConfig.BindPendingOverrides(netId, targetGo);
            SkinApplier.ApplySkinToPlayer(targetGo, remoteSkin, resolvedCharType);
            ModLog.Info($"同 netId 躯体重建恢复：netId {netId} (charType {resolvedCharType}) -> {remoteSkin}");
        }

        private void SyncSavedOverridesOnEnter(string skin)
        {
            if (Settings == null || Settings.IsPassiveSyncMode) return;
            var accOverrides = Settings.GetAllAccessoryOverridesForSkin(skin);
            if (Settings.SyncAccessories.Value && accOverrides != null && accOverrides.Count > 0)
            {
                foreach (var kv in accOverrides) BroadcastAccessoryDelta(skin, kv.Key);
            }
            if (Settings.SyncTailDeform.Value && (Settings.GetTailOverride(skin) != null || !Settings.TailDeformEnabled.Value))
                BroadcastTailDelta(skin);
        }
    }
}
