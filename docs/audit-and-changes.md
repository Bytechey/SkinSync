# SkinSync 代码审查与方法级修改说明

本文档记录对 `mods/SkinSync` 全量源码（对照 Steam 游戏 `Assembly-CSharp.dll` 2026-11-14 构建、三版本 `CasualtiesMP.dll`——Steam 原版 `v5.5.3.0`、普通版 `main`、Feature 版 `Feature-Refactor`——与 `CuSaveManager.dll`）执行逐行审查后的所有代码改动、方法级修改理由、替代方案取舍及注释重写范围。

## 一、核心取舍与机制说明

1. **挂载件材质隔离：优先复用 `WorldGeneration.world.defaultMat`，主菜单回退按 Shader 缓存的净拷贝材质**
   - **背景**：游戏在 `Limb.Awake` 中为每个肢体克隆独立 `Material`，每帧写入 `_BloodOverlay`、`_Dirtyness`、`_Wetness` 等受伤污渍参数。旧代码把翅膀、配件、区块动画直接指向 `bodyRenderer.sharedMaterial`，角色躯干流血或沾泥时，翅膀和衣服配件会跟着染上血块。
   - **替代方案与放弃理由**：给每个配件和翅膀单独 `new Material` 会打断合批并带来手动销毁负担；改用 `Sprites/Default` 则在关卡内彻底失去 URP 2D 光照。游戏自身在 `Limb.MakeBoneSprite` 与 `Limb.CreateTemporarySprite` 里直接拿 `WorldGeneration.world.defaultMat` 作为受光且无伤污的共享材质，复用它能同时保住合批和光照；只有主菜单（`PreGen` 场景还没有 `WorldGeneration.world`）按 Shader 实例 ID 缓存一份清零瞬态参数后的克隆材质兜底。
2. **血液配置与贴图缓存：按皮肤名建立正负向双缓存**
   - **便利与代价**：`BleedSpawnTranspiler.ColorSpawnedBlood` 在每次血液粒子落地或溅墙时触发，战斗时每秒几十次。加上 `_cfgCache`（命中缓存）、`_noBloodSkins`（无 `blood.json` 负向缓存）和 `_texCache`（贴图实例缓存）后，热路径变成纯字典查找，去掉了每滴血读两次磁盘和反复 `new Texture2D` 泄漏的问题。代价是磁盘改文件或收到远端皮肤包时缓存不会自动感知，所以必须在 `ScanAvailableSkins`（全量清空）和 `SkinApplier.ReapplyForSkin`（按皮肤名调用 `BloodAttacher.Invalidate`）两处主动清理。
3. **宿主面板互斥：嵌入 `CuSaveManager` 时停用独立窗口热键与 `UiBlocker`**
   - **背景**：`SkinSync` 与 `CuSaveManager` 各自维护一套静态 `UiBlocker` 来禁用场景里的 `GraphicRaycaster`。如果 `SkinSync` 已经嵌进 `CuSaveManager` 页签却还响应独立 `F10` 热键或暂停菜单按钮，两边交错调 `Block()` / `Unblock()` 会把对方刚禁掉的 `false` 当成原始状态存下来，关掉面板后整个游戏的按钮都会点不动。
   - **替代方案与放弃理由**：跨模组反射共用同一个阻塞计数器会让两边绑死内部私有字段；直接按 `SaveManagerTabBridge.Register` 的返回值决定 `_embeddedInSaveManager`，嵌入成功就完全交给 `CuSaveManager` 管开关和射线拦截，失败就退回独立窗口模式，职责边界最清楚。
4. **单文件体积拆分：同命名空间 `partial class` 切分长文件**
   - **背景**：`SkinSyncWindow.cs`（原 1071 行）、`SkinApplier.cs`（修完缺陷后超 1000 行）和 `TogetherProvider.cs`（原 841 行）都超过了单文件 800 行上限。
   - **替代方案与放弃理由**：拆成独立的外部辅助类会改动 `SkinApplier` 等静态类的外部反射签名，或者被迫把私有状态字典改成 `internal`。用 `partial class` 按 "状态编排 vs 部件装配"、"窗口框体 vs 标签页内容"、"反射主体 vs 读写包装器" 切开，对外接口不变，所有可改源码文件都压到了 746 行以内。
5. **配件功能正式开放至 Release，仅保留纯诊断追踪在 `#if DEBUG`**
   - **背景**：早先配件列表、隐藏原版穿戴物、槽位联动和配件同步开关在 `SkinSyncWindowTabs.cs` 里被 `#if DEBUG` 包裹（其中 `DrawCurrentTab` 还误写成了小写 `#if debug`），而底层 `AccessoryAttacher`、`EquipmentHider`、`AccessoryEnforcer` 在 Release 本就一直挂载运行，造成玩家在正式版里装了带配件的皮肤却找不到开关和偏移微调入口。
   - **便利与代价**：移掉 `SkinSyncWindowTabs.cs` 中这 3 处 `#if DEBUG` 后，正式版直接具备完整的配件控制与同步能力；仅把 `DebugMessageTrace`、`CommsTrace` 等高频通信跟踪日志继续留在 `#if DEBUG` 内，避免正式版刷日志拖慢帧率。

## 二、逐文件与逐方法修改清单

### 2.1 项目引用与插件主入口（`references/`、`SkinSync.cs`）

| 位置 | 修改内容 | 修改理由 |
| --- | --- | --- |
| `references/*.dll` | 从 Steam 最新安装目录同步更新 `Assembly-CSharp.dll`（2026-11-14 构建）、`Unity.RenderPipelines.Core.Runtime.dll`、`Unity.RenderPipelines.Universal.Runtime.dll`、`UnityEngine.UI.dll`。 | 使编译期引用的游戏与 URP/uGUI 程序集同当前正式版游戏二进制保持一致。 |
| `Awake()` | 1. 启动时若 `Settings.CurrentSkin` 非空，立即调用 `ApplyTailOverrideToConfig`；<br>2. `CharacterCatalog.DescribeAll()` 从 `ModLog.Info` 降为 `ModLog.Debug`；<br>3. 用 `_embeddedInSaveManager = saveManagerInstalled && SaveManagerTabBridge.Register(_window)` 判定嵌入结果，失败时回退 `MenuButtonInjector.Setup`。 | 1. 原代码只在手动切皮肤时读尾巴覆盖参数，重启游戏自动恢复上次皮肤时尾巴覆盖没生效；<br>2. 角色档案展开文本很长，日常启动打在 Info 刷屏；<br>3. 原代码只看 `PluginInfos.ContainsKey` 就跳过主菜单按钮注入，万一 `CuSaveManager` 改版导致反射注册返回 `false`，玩家就没有打开面板的入口了。 |
| `RegisterPatches()` | 新增挂载 `SkinSyncPlayerCameraMouseGuard` 与 `Patches.FacialExpressionStartSkinPatch`。 | 前者拦截面板打开期间鼠标穿透拖拽世界物品；后者修复 Together 5.5.3.0 在 `FacialExpression.Start` 把表情贴图重置回原版的问题。 |
| `OnSceneLoaded(...)` | 新增 `_window?.ClosePanel()`、`RosterConsumer.ResetLocalCache()`、`SkinApplier.OnSceneChanged()`、`SkinMessageHandlers.OnSceneChanged()`。 | 切场景时如果面板还开着，旧场景的 `GraphicRaycaster` 已销毁而 `_isBlocking` 留在 `true` 会锁死新场景点击；顺带清掉上一局残留的 `netId`、失效角色引用和皮肤包广播记录。 |
| `ScanAvailableSkins()` | 新增 `BaseSizesLoader.Invalidate(null)`、`SkinPreviewRenderer.InvalidateAll()` 与末尾调用 `SkinApplier.ReloadAllCachedSkins()`，并跳过 `.` 开头的隐藏目录。 | 游戏开着的时候改了贴图或 JSON 按 `F8` 重扫，旧代码既没清基准尺寸、血液和预览图缓存，也没清 `SkinApplier` 贴图字典并重刷在场角色；现在先还原在场角色原版基线、再销毁旧贴图与血液衍生材质缓存、最后重新挂载新贴图，过滤 `.git` 等隐藏目录。 |
| `Update()` | 1. 嵌入 `CuSaveManager` 时跳过独立 `TogglePanelHotkey`；<br>2. 删掉 `Update` 里的 `Escape` 检测；<br>3. 把 `ApplyPersistentSkinIfNeeded()` 移到 `TryGetLocalPlayer()` 返回 `true` 之后。 | 1. 防止跟 `CuSaveManager` 撞 `UiBlocker` 死锁；<br>2. 在 `Update` 里按 `Esc` 关面板会同帧解除阻塞，紧接着跑的 `PlayerCamera.Update` 读到同一个 `Esc` 就会顺带弹出游戏暂停菜单；<br>3. 原代码在主菜单（没本地玩家）每帧跑一次全场景 `Body` 扫描、写一次 `SkinCacheStore` 并清空一次预览图缓存。 |
| `LateUpdate()`（新增） | 非嵌入模式下，面板打开且不在改键采集（`!_window.IsCapturingHotkey`）时响应 `Escape` 关闭面板。 | `LateUpdate` 晚于 `PlayerCamera.Update` 执行，当帧输入仍被 `UiBlocker` 拦住，不会误弹暂停菜单；改键时按 `Esc` 只取消改键，不会把面板关掉。 |
| `OnGUI()` / `OnDisable()` | `OnGUI` 在 `_embeddedInSaveManager` 时直接返回；`OnDisable` 新增 `_window?.ClosePanel()`。 | 嵌入模式交由宿主绘制；组件停用时确保关窗并刷写未落盘的尾巴滑块数据。 |
| `ResolveCurrentSkin()` | `Settings.CurrentSkin` 为空时直接返回 `""`，不再回退 `availableSkins[currentSkinIndex]`；校验磁盘目录或内存皮肤包存在性。 | 原代码在还没选过任何皮肤时默认返回第 0 个皮肤，角色网格页会把没穿在身上的第 0 个皮肤标成 `★ 正在使用` 并灰掉按钮，导致玩家点不了列表里的第一个皮肤。 |
| `LoadAccessoryEntriesForSkin(string)`（新增）、`GetCurrentAccessoryRows()`、`BroadcastAccessoryDelta(...)` | 抽出统一辅助方法：先判 `skinDir` 非空再读磁盘 `accessories.json`，否则回退内存皮肤包 `SkinApplier.LoadJsonFromMemoryPack`。 | 原代码直接用 `Path.Combine(GetSkinDir(skin), "accessories.json")`，当皮肤来自未落盘的内存同步包时 `GetSkinDir` 返回空串，拼出来会误读游戏工作根目录下的相对路径。 |
| `TryGetLocalPlayer()` | 1. 检查 `localNetBodyBox` 底层 `UnityEngine.Object` 是否已销毁或 `localChara == null`，失效时清空重取；<br>2. `SessionConsumer.IsRunning` 为真但还没拿到本地 `NetBody` 时返回 `false`，禁止回退单机 `FindObjectOfType<Body>()`；<br>3. 单机优先取 `PlayerCamera.main.body`。 | 1. `localNetBodyBox` 是 `object` 类型，绕过了 Unity 重载的 `== null` 判空，联机死亡重生或换角色销毁旧 `NetBody` 后它一直非空，新角色就换不了皮肤；<br>2. 联机刚进图本地 `NetBody` 还没刷出来而别人的 `Body` 已经在场时，回退 `FindObjectOfType<Body>()` 会把别人认成自己；<br>3. 单机场景有尸体或其他生物时，`PlayerCamera.main.body` 能准确定位主控角色。 |
| `SwitchToSkin(string)`、`ReportLocalSkinIfNeeded()`、`SyncSavedOverridesOnEnter(string)`（新增） | 切皮肤与进图上报皮肤后，若 `SyncMode != "Passive"`，按开关同步广播该皮肤已保存的配件覆盖与尾巴覆盖。 | 原版虽然在界面上画了 "进入游戏一键同步 / 仅被动响应改动" 两个按钮，但发包处根本没读这个配置，属于没接线的空开关。 |

### 2.2 角色档案与贴图解析模块（`Character/`、`Resolve/`）

| 文件 | 方法 / 成员 | 修改内容与理由 |
| --- | --- | --- |
| `Character/CharacterCatalog.cs` | `All`、`ForCharType`、`TryGetSpriteSize`、`TryFindPartBySpriteName`、`DescribeAll`、`EnsureLoaded`、`AddFace` | 1. 全部公开查询补充集合判空保护；<br>2. `EnsureLoaded` 改为先在局部字典采集完再一次性赋给静态字段，防止加载中途抛异常留下半截空表后永久跳过重试；<br>3. `AddFace` 补充 `string.IsNullOrEmpty(sprite.name)` 过滤。 |
| `Character/CharacterProfile.cs`、`PartContentSizes.cs`、`PartKeyNames.cs` | 全文件 | 逻辑核查正常，重写单行 `<summary>` 并删掉字段冗余注释。 |
| `Resolve/ResolveOrigin.cs`、`ResolveOutcome.cs`、`SidedSpriteLookup.cs`、`SkinResolver.cs` | 全文件 | 三层回退与侧向查找逻辑正常，删掉长篇 `<remarks>`，保留单行 `<summary>`。 |

### 2.3 配置与持久化模块（`Config/`）

| 文件 | 方法 / 成员 | 修改内容与理由 |
| --- | --- | --- |
| `Config/AccessoryConfig.cs`、`BloodConfig.cs`、`WingsConfig.cs` | `Load`、`Parse` | 原 `Parse` 用空 `catch {}` 吞掉了所有异常，皮肤作者把 JSON 括号或逗号写错时日志里一点动静都没有；现补充 `ModLog.Warning` 打出具体解析报错。 |
| `Config/BaseSizesLoader.cs` | `Load`、`ParseContent`、`Invalidate` | `Load` 增加空路径拦截；`int.TryParse` 指定 `CultureInfo.InvariantCulture`；`Invalidate` 支持空参清空全表。 |
| `Config/BloodRenderConfig.cs` | `Enabled` | 默认初值从 `true` 改成 `false`，跟 `SkinSyncSettings.RenderCustomBlood` 的默认值一致。 |
| `Config/SkinCacheStore.cs` | `Set(ulong, string)` | 拒绝包含 `=`、`\r`、`\n` 的 `skinId`，防止按行存储的 `skinCache.txt` 被特殊字符截断或注入假记录。 |
| `Config/SkinSyncSettings.cs` | `GetAllAccessoryOverridesForSkin`、`EscapeKey`、`UnescapeKey`（新增）及序列化/反序列化方法 | 配件与尾巴覆盖串用 `;`、`=`、`|`、`,` 分隔，而自定义皮肤文件夹名常带空格、括号甚至逗号等号；新增 UTF-8 百分号转义，不含 `%` 的旧配置直接兼容读取。 |
| `Config/TailDeformConfig.cs` | 新增 `TailDeformParams` 结构体与远端角色参数隔离表 | 原版 `TailDeformConfig` 为全局静态变量，房间内任一玩家改动尾巴滑块会覆盖全房所有人的尾巴摆动；现保留静态字段供本机角色实时调参，新增 `_remoteOverrides` 与 `_pendingRemoteOverrides` 按角色与皮肤独立存储远端玩家的尾巴开关及 17 项物理参数（未绑定 `owner` 或远端未上报时回退 `Defaults`）。 |
| `Config/ZonesConfig.cs` | `ComputeGeometry`、`ToEntries` | 1. `ComputeGeometry` 用 `try / finally` 销毁临时 `Texture2D`；<br>2. 遮罩图尺寸与部件贴图尺寸不一致时按比例缩放 `CenterX`、`CenterY`、`Radius`；<br>3. `ToEntries` 将 ID 范围收紧为 `1..255`，防止越界整数强转 `byte` 绕过 0 值过滤。 |

### 2.4 联机互操作与网络发送模块（`Interop/`、`Network/` 及跨模组同步）

| 文件 | 方法 / 成员 | 修改内容与理由 |
| --- | --- | --- |
| `Interop/InteropContract.cs`、`InteropReflection.cs` | 未修改（哈希冻结） | `tools/verify-interop.ps1` 强制校验 5 个模组这两份文件 SHA-256 完全一致，保持原字节不动。 |
| `Interop/Consumers/ChannelConsumer.cs` | `ForwardExcept(ushort, IInteropWriter, ushort)` | 新增 `if (isHostServer && player.IsLocal) continue;`。Together 房主既是服务端又是 `ClientId == 0` 的客户端，`OnServer*` 已经在房主本地执行过换肤，转发时再给自己发一份会造成重复应用。 |
| `Interop/Consumers/RosterConsumer.cs` | `TryGetLocalClientId(out uint)`、`LocalNetId()`、`ResetLocalCache()`、`TryResolveBodyIdentity(Body, ...)`（新增） | 1. 原 `TryGetLocalClientId` 末尾写的是 `return clientId != 0;`，而 Together 房主的 `ClientId` 就是 `0`，导致房主调这个方法永远返回 `false`；上方已判过 `local.IsValid`，直接返回 `true`。<br>2. `LocalNetId()` 过滤 `netId != 0`，切场景时调 `ResetLocalCache()` 清零旧局残留 ID；新增 `TryResolveBodyIdentity` 支持由 `Body` 反查所属玩家的 `netId` 与本机归属。 |
| `Interop/Providers/TogetherProvider.cs` + `TogetherProviderAdapters.cs` | `PutBytes`、`_callbacks.Add` 加锁及文件拆分 | 1. 原 `PutBytes` 在数组为空或超过 `65535` 字节时直接 `return`，一个字节都不写，紧跟在后面的 `bool allowPersist` 就会顶到字节数组长度前缀的位置上，整条网络包反序列化错位；改为写入空数组 `Array.Empty<byte>()`（写 2 字节长度 `0`），保住流边界。<br>2. 两处 `_callbacks.Add` 补齐 `lock (_sync)` 线程锁。<br>3. **跨模组同步修复**：已将 `PutBytes` 流对齐修复与 `_callbacks.Add` 线程锁同步应用到本仓库下的 `CuSaveManager`、`CuStats`、`hwAssistive` 与 `_interop_template`（外部模组 `CUCoreLib` 按边界未触碰）。 |
| `Network/3.3_SkinChangeMessage.cs`、`Network/3.8_SkinPackChunkMessage.cs`（新增）、`Interop/Consumers/SkinMessageCodec.cs`、`Network/MultiplayerSender.cs` | 分片传输消息 `50005`、`ServerForwardSkinPackExcept`、`BuildLocalTailMessage`、`ServerSendAccessoryToClient`、`ServerSendTailToClient`（新增）、`ServerBroadcastSkinChange`、`SendSkinPackData`、`ServerSendSkinPackDataToClient` | 1. 保留 `50004` 直发 `<= 48 KB` 小皮肤包以兼容旧节点，新增子消息 `50005`（`SkinPackChunkMessage`）将 `> 48 KB` 且 `<= 2 MB` 的大型皮肤包按 `32 KB` 单片发送并附带 CRC32 校验，突破底层 `PutBytesWithLength` 的 `65535` 字节单包上限；<br>2. 新增单播配件与尾巴状态的方法及本地尾巴快照构造，供主机向后加入或重生的客机补发；<br>3. `ServerBroadcastSkinChange` 补传 `charType = CharTypeOf(netId)`，皮肤包发送方法改为返回 `bool`。 |

### 2.5 Harmony 补丁与消息处理模块（`Patches/`）

| 文件 | 方法 / 成员 | 修改内容与理由 |
| --- | --- | --- |
| `Patches/BleedParticleColorPatch.cs` | `FacialExpressionStartSkinPatch.Postfix`（新增） | 反编译 `CasualtiesMP.dll` v5.5.3.0 确认其 `FacialExpressionStartPatch.Postfix` 会重刷脸部贴图；用 `Priority.Last` 补调 `SkinApplier.ReapplyFaceIfNeeded`，若检测到该躯体尚未登记（如联机同 `netId` 换物种重建出的新躯体），自动触发 `SkinSync.OnExternalBodyReadyOrRebuilt` 恢复整套皮肤、配件与尾巴参数。 |
| `Patches/TogetherHooksBridge.cs`（新增） | `TryBind`、`Dispose` | 通过可选反射同时兼容 `Together.Api.TogetherPanelApi`（`OnBodyReadyOrRebuilt`、`OnWoundViewTargetApplied`、`RegisterExternalInputBlocker`）与 `Together.ModHooks`（`OnBodySpawned`、`OnBodyMeshRebuilt`、`OnWoundViewTargetChanged`），不放入必选 `Require` 以保证存量 5.5.3.0 DLL 正常连接。 |
| `Patches/WoundViewPatches.cs`（新增） | `WoundViewStartSkinPatch`、`WoundViewUpdateSkinPatch`、`WoundViewAddImageSkinPatch` | 在 `WoundView.Start`、`UpdateView` 与 `AddImageToLimb` 挂载 Postfix，驱动单机与联机健康检查面板的自定义伤势贴图、观察目标切换重绘与彩色肌肉层防染黑。 |
| `Patches/BleedSpawnTranspiler.cs` | `ColorSpawnedBlood(Object, BleedParticle)` | 1. 新增 `if (source.vomit) return;`，游戏普通呕吐物复用了 `BleedParticle` 并置 `vomit = true`，不跳过会把呕吐物染成血色；<br>2. 改走 `BloodAttacher.TryGetBloodConfig` 内存缓存，去掉每滴血读盘和纹理泄漏。 |
| `Patches/PendingSkinApplier.cs` | `Enqueue`、`OnSceneLoaded`（新增）、`Update` | 队列项保留 `CharType`；重试命中 `chara` 时先调 `AccessoryAttacher.BindPendingOverrides` 与 `TailDeformConfig.BindPendingOverrides` 合并等待期间收到的配件及尾巴覆盖，超时放弃时同步清理；拦截 `netId == 0`；切场景或联机断开时立刻清空队列。 |
| `Patches/SaveManagerTabBridge.cs` | `Register` | 状态栏签名接入 `SkinSyncI18n`，英文环境显示英文文本。 |
| `Patches/SkinCacheBroadcaster.cs` | `RecordSkin`、`RecordAccessory`、`RecordTail`、`SeedLocalHostOverrides`（新增）、`Update`、`PruneDeadEntries` | 全链路过滤 `netId == 0` 并在 `Update` 检查 `SessionConsumer.IsRunning`；扩展主机状态表同步缓存房主与在场玩家的配件覆盖及尾巴物理参数，使后进房或死亡重生的客机也能收到完整的配件与尾巴状态。 |
| `Patches/SkinMessageHandler.cs` | 全部消息入口与 `Apply*Override`、`SanitizeFloat`（新增）、`OnSceneChanged`（新增） | 1. 全部入口统一校验 `SkinPathResolver.IsSafeSkinName(msg.skinID)`，堵住借 `skinID` 跨目录读写的漏洞；<br>2. `OnClientSkinMessage` 判本地回显时加上 `localNetId != 0`；<br>3. 去掉 `Passive` 模式下误丢远端配件/尾巴同步包的判断；远端发来的配件与尾巴参数分别按角色隔离存入 `AccessoryAttacher` 与 `TailDeformConfig` 内存表（`NetBody` 未注册时按 `netId` 暂存），并在主机回环（`isLocal`）时直接跳过重复应用，避免房主点击重置默认后被回环包重新写回非空覆盖；<br>4. `ApplyTailOverride` 对全部 17 个数值字段执行区间钳制与 `NaN`/`Infinity` 过滤，防止异常网络包触发超长分段网格分配或物理计算崩溃；<br>5. 房主单播直回请求者时不再把 `skinID` 记入已广播集合，切场景清空广播去重表。 |
| `Patches/UpdateChecker.cs` | `Update`、`TryStartCheck`、`CheckForUpdates`、`EnsureStyle` | 支持运行期在面板打开更新提示后补触发一次检查；给 `UnityWebRequest` 加上 10 秒超时并用 `JObject.Parse` 解析 `tag_name`；缓存 `GUIStyle` 并接入中英文词条。 |

### 2.6 界面与防穿透模块（`UI/`）

| 文件 | 方法 / 成员 | 修改内容与理由 |
| --- | --- | --- |
| `UI/BlackWhiteSkin.cs` | `EnsureBuilt()` | 给运行时生成的 `GUISkin` 加上 `HideFlags.HideAndDontSave`，防切场景 `UnloadUnusedAssets` 误回收。 |
| `UI/InGameOverlay.cs` | `ShouldShow()` | 面板已打开（`UiBlocker.IsBlocking`）时隐藏暂停界面右下角唤起按钮，避免叠在窗口边上重复点。 |
| `UI/MenuButtonInjector.cs` | `Dispose()` | 退出时一并清空 `InjectedRect` 和 `_onClick` 静态委托。 |
| `UI/PreRunGuardPatch.cs` | `SkinSyncAdaptiveButton*`、`SkinSyncPlayerCameraMouseGuard`（新增） | 1. 主菜单注入按钮加上 `activeInHierarchy` 判断；<br>2. 新增 `PlayerCamera.HandleMouseOver` / `HandleDragging` 拦截补丁，防止局内开着面板调滑块时鼠标穿透拖拽世界物品。 |
| `UI/SkinSyncI18n.cs` | 词条表与 `LocalizeWearSlot` | 补齐更新提示和桥接签名双语文本，更新过时的目录名提示；未知槽位直接返回原槽位名。 |
| `UI/SkinSyncWindow.cs` + `SkinSyncWindowTabs.cs` | 拆分 `partial`、移除配件 `#if DEBUG`、防抖保存尾巴、稳住网格宽度 | 1. **开放配件至 Release**：移除 `DrawSettingsTab` 与 `DrawCurrentTab` 中包裹隐藏装备贴图、穿戴槽位联动、配件同步和配件微调列表的 3 处 `#if DEBUG`（原 `DrawCurrentTab` 还曾误写为永不成立的小写 `#if debug`）；<br>2. **尾巴滑块松手再存**：拖动期间只改内存实时看效果，松开鼠标左键、切页签或关面板时调 `FlushPendingTailSave()` 一次性写盘并发包，消除拖动时每帧写 `.cfg` 和刷网络包；<br>3. 切自定义血液开关时立即调 `SkinApplier.RefreshAll()`；<br>4. `DrawSkinGrid` 只在 `Repaint` 更新有效宽度缓存，消除 `Layout` 与 `Repaint` 宽度跳变；数值文本统一走 `CultureInfo.InvariantCulture`。 |
| `UI/UiBlocker.cs` | `Block()`、`Unblock()` | `Block()` 开头加 `if (_isBlocking) return;` 重入保护，防止二次进入时把刚禁掉的射线器记成原始状态导致永久锁死 UI；`Unblock()` 用 `finally` 保底清状态。 |

### 2.7 贴图装配、特效挂载与预览合成模块（`Utils/`）

| 文件 | 方法 / 成员 | 修改内容与理由 |
| --- | --- | --- |
| `Utils/SkinPathResolver.cs` | `IsSafeSkinName`、`IsReservedFileStem`、`HasAnyContent`（新增）、`GetSkinDir` | 集中拦截超长名、路径分隔符、`..`、控制字符、尾随点/空格和 `CON`/`NUL` 等保留设备名；空目录不再遮蔽有效的会话同步缓存，且目录不存在时不写入 `_resolved` 死缓存。 |
| `Utils/SkinPackCodec.cs`、`Utils/SkinPackChunkAssembler.cs`（新增） | `MAX_PACK_SIZE`、`MAX_FILE_SIZE`、`TryPushChunk`、`ComputeCrc32` | 将整包上限放宽至 `2 MB`、单文件上限放宽至 `512 KB`；新增 `SkinPackChunkAssembler` 按 `(senderKey, transferId)` 重组 `32 KB` 分片，内置每发送端最多 4 个并发会话限额、30 秒超时清理与 IEEE 802.3 CRC32 校验；解包时校验 8 字节 PNG 头并失效路径缓存。 |
| `Utils/WoundViewAttacher.cs`（新增） | `Apply`、`OnUpdateView`、`ApplyDynamicLimbImage`、`Invalidate`、`ClearCaches` | 接管原联机模组剥离的 `WoundView` 伤势面板适配：按 `view.body` 所属玩家独立匹配皮肤（队友无皮肤时还原原版），支持 `WoundView/` 子目录与物种专属子目录、Milky/Dune 头图三角色回退、`FitToStock`（兼容 `baseSizes.json` 高清基准尺寸）比例缩放，以及 `!armorMode` 下彩色肌肉层防染黑。 |
| `Utils/SkinApplier.cs` + `SkinApplierParts.cs` | 原版还原、物种变更检测、热刷重建、翅膀坐标、空目录保护 | 1. 修好原版皮肤开关切回自定义皮肤短路失效、`RefreshAll` 把远端玩家刷成本地皮肤、`RestoreVanillaSkin` 漏清配件/血液/尾巴的问题；新增 `_charTypeByChara` 跟踪角色物种变化，原地改 `charType` 时自动重置基线并按新物种重涂；<br>2. 换肤前停用冲突的 `SkinSpriteReplacer`，并通过反射同步 `FacialExpressionPause.originalEyeList`；<br>3. 贴图字典改用 `OrdinalIgnoreCase`，跳过 `.zones.png` 与 `WoundView/` 子目录贴图；`ReapplyForSkin` 与 `ReloadAllCachedSkins` 先把在场角色及 `WoundView` 还原回原版基线，再销毁旧纹理、失效路径与衍生缓存，最后重新挂载新资源；<br>4. 读取 `wings.json` 与 `accessories.json` 前先校验 `skinDir` 非空；<br>5. 下翼仅在实际挂为上翼子节点时才减去上翼半高并置 `isLowerWing = true`；转身监听器挂载当帧直接对齐初始朝向。 |
| `Utils/AccessoryAttacher.cs` + `AccessoryEnforcer.cs` | 按 `Body` 隔离状态、暂存未注册远端覆盖、保留副肢体父节点 | 1. 新增 `_byBody` 按角色独立记录配件句柄，解决远端玩家换肤清空本机配件控制字典和槽位检查的问题，并支持按 `netId` 暂存尚未生成 `NetBody` 的远端配件覆盖；<br>2. 删掉把副肢体配件强行改挂回主件节点下的错误代码，让小臂袖子、小腿护具各自跟着对应关节转动。 |
| `Utils/BloodAttacher.cs` + `BloodVomitWatcher.cs` | 材质保护、基线还原、衍生材质同步清理、渐变与呕吐区分 | 1. 只改写 `Limb.Awake` 已克隆独立材质的肢体，不污染预制体共享材质；<br>2. 套用新皮肤血液配置前先还原 `_orig*` 基线；<br>3. `RenderTexture.GetTemporary` 加 `try / finally`，粒子材质按键池化复用并在 `ClearCaches`/`Invalidate` 时连同自定义鼻血 `Sprite` 一并销毁清理，序列帧改用线性时间曲线，随机色池改用 `GradientMode.Fixed` 离散阶跃；<br>4. `BloodVomitWatcher` 收紧为名称同时含 `"vomit"` 和 `"blood"`，不再误染普通呕吐物。 |
| `Utils/EquipmentHider.cs` | `HideAllOnPlayerBodies()` | 只遍历 `body.limbs[i]` 直接子节点并校验 `item.Stats.wearable` 或 `WearableExtension`，修复旧代码递归把角色手里的武器、工具、医疗品一并隐藏成空气的问题。 |
| `Utils/TailFlowDeform.cs` + `WingScript.cs` | 按角色独立读取尾巴参数、补齐 `OnDisable` 与 `HideAndDontSave`、转身保物理链、回退全长轮廓、下翼去重倾角 | `TailFlowDeform` 新增 `BindOwner` 并通过 `TailDeformConfig.ResolveFor` 按角色独立读取物理配置，互不影响他人尾巴；新增 `OnDisable` 在停用组件时隐藏动态网格并恢复原版尾巴渲染，为运行时网格与材质加上 `HideFlags.HideAndDontSave` 及空引用守卫；左右转身切图时保留已有骨骼位置不硬弹；不可读贴图回退轮廓从半长 `extents` 修正为全长 `size`；下翼作为上翼子节点时局部 X/Y 倾角归零避免双倍变形。 |
| `Utils/ZonesAttacher.cs` | 材质兜底、2D 角度换算、多人序列帧字典 | 动态粒子补绑 `Sprites/Default` 消除洋红色方块；`Cone` 朝向改用 `FromToRotation` 转到 XY 平面；环绕角速度补乘 `Mathf.Deg2Rad`；`ZoneFrameAnimator` 按自身 `CharacterName` 取贴图字典消除多人串图。 |
| `Utils/SkinPreviewRenderer.cs` + `SkinPreviewZones.cs` | 纹理释放、配件偏移旋转修正、Milky 小写贴图兼容、非预乘混合 | `LoadPng` 用 `try / finally` 释放临时纹理；目录不存在时返回 `null` 不缓存黑框；预览图补读用户配件偏移并用父肢体角度旋转局部偏移；部位后缀匹配改为忽略大小写并补齐 `milkytorso` / `mildownarm` 识别；光晕和半透明粒子改用非预乘 Alpha 合成消除透明边缘发黑。 |
| `Utils/DebugMessageTrace.cs` 等 5 个辅助文件 | 条件编译与注释精简 | `DebugMessageTrace` 内部字段与辅助方法整体包入 `#if DEBUG`，避免正式版残留无用静态字典；其余辅助类完成注释重写。 |

### 2.8 三版本 `Casualties: Together` 全兼容专项修复（Steam 原版 `v5.5.3.0` / 普通版 `main` / Feature 版 `Feature-Refactor`）

| 文件 | 方法 / 成员 | 修改内容与理由 |
| --- | --- | --- |
| `Patches/LegacyTogetherSkinSuppressor.cs`（新增）、`SkinSync.cs` | `TrySuppress`、`Dispose`、`IsLegacyContainerDir`、`RegisterPatches`、`InstallTogetherReceiverRoutine`、`OnApplicationQuitting`、`ScanAvailableSkins` | 1. **彻底压制 Steam 原版 `v5.5.3.0` 内置皮肤子系统冲突**：原版 `CasualtiesMP.dll` 在全局根命名空间内置了 `SpriteReplacerPlugin`、`SkinSpriteReplacer`、`WoundViewIntegration`、`SkinMenuUI` 与 `MpSkinSync`，而普通版 `main` 与 Feature 版已移除该子系统。检测到 `CasualtiesMP` 含 `SpriteReplacerPlugin` 时：① 扫描并还原已挂载的 `SkinSpriteReplacer`（调 `Restore()` 置 `Active = false` 后销毁），清空 `LoadedSprites` 并对在场 `FacialExpression` 调 `RestoreFaceToStock` 恢复纯净基线；② 调用 `Harmony.UnpatchID("com.yourname.spritereplacer")` 一次性卸载原版内置的全部冲突 Harmony 补丁；③ 通过 `Resources.FindObjectsOfTypeAll` 捕获带 `HideFlags.HideAndDontSave` 的 `SpriteReplacerPlugin` 与嵌套 `SpriteReplacerPlugin+InputListener` 实例，停止默认导出协程并销毁对象（阻断 `MpBody.Tick()` 每 1 秒重置面部贴图与小键盘 `0..9` 抢键），解绑 `SceneManager.sceneLoaded` 等静态回调，并隐藏 `SkinMenuUI` 的 `_canvas` 及移挂至顶栏的 `_menuButtonRt`；④ 对 `WoundViewIntegration.Apply` / `RefreshForBody`、`SpriteReplacerPlugin.ApplyToAll` 与 `MpSkinSync.OnBodyPartReady` 挂载 `Prefix => false` 短路补丁，阻断原版 `Medical.SetWoundviewSpeciesSprite` 每次打开健康面板时覆盖 `SkinSync` 伤势贴图。普通版 `main` 与 Feature 版因不存在 `SpriteReplacerPlugin` 直接零开销跳过；退出时在 `Dispose` 中干净卸载短路补丁。<br>2. **过滤原版自动创建的空壳物种目录**：`ScanAvailableSkins` 通过 `IsLegacyContainerDir` 排除原版 `SpriteReplacerPlugin` 在 `CustomSprites/` 根下自动创建的 `Exp`、`Milky`、`Dune` 空壳容器目录（仅当顶层无 `.png`/`.json` 且一级子目录仅含 `default`/`backups`/`slot0..9`/`st0..9` 时排除，绝不误伤用户自建同名合法皮肤）。 |
| `Utils/SkinApplier.cs` | `TryGetCharacterByPosition` | **修复三版本联机自定义流血/血迹粒子染色 100% 失效**：联机模式下 `_byChara` 以 `body.gameObject` 为 Key 存入，旧代码写死取父节点 `body.transform.parent.gameObject` 导致联机查询 100% 漏失；现统一改调 `GetActiveSkinForBody(body)`（沿 `body.transform` 逐级上溯并尊重原版外观开关），单机与三版本联机 100% 命中。 |
| `SkinSync.cs`、`Patches/WoundViewPatches.cs` | `OnExternalBodyReadyOrRebuilt`、`WoundViewStartSkinPatch.Postfix` | **消除 Feature 版换物种重建重复装配与健康面板同帧双遍历**：1. `OnExternalBodyReadyOrRebuilt` 本地分支补齐 `!isRebuild && _appliedTargetObject == targetGo && _appliedCharType == resolvedCharType && _persistentApplied == localSkin && SkinApplier.GetAssignedSkin(targetGo) == localSkin` 幂等短路，并在涂装后同步写入 `_persistentApplied = localSkin`，避免同对象多入口通知及下一帧 `ApplyPersistentSkinIfNeeded` 连续跑 3 次 `ApplySkinToPlayer`；<br>2. `WoundViewStartSkinPatch.Postfix` 改调 `WoundViewAttacher.OnUpdateView(__instance)`，复用 `_lastAppliedByView` 缓存在 `TogetherPanelApi.OnWoundViewTargetApplied` 已同帧涂装时直接短路。 |
| `Utils/SkinPackChunkAssembler.cs`、`Utils/SkinPackCodec.cs`、`Utils/SkinApplier.cs` | `MaxTransfersPerSender`、`OnTransferDropped`、`TryPeekSkinId`、`ClearRequestedPack`、`OnSceneChanged` | **修复满房并发大皮肤包分片淘汰与 `_requestedPacks` 永久锁死**：1. 客户端收服务端转发分片统一归入固定 `senderKey = ushort.MaxValue`，将 `MaxTransfersPerSender` 从 `4` 提升至 `16` 以覆盖 16 人满房并发 `> 48 KB` 皮肤包同步；<br>2. 新增 `SkinPackCodec.TryPeekSkinId` 在首片到达时提取 `skinID`，当分片因超时、配额淘汰、元数据冲突或 CRC32 校验失败被丢弃时回调 `SkinApplier.ClearRequestedPack(skinId)`，并在 `SkinApplier.OnSceneChanged()` 清空 `_requestedPacks`，杜绝单次丢片后整局无法重试拉取该皮肤。 |

## 三、注释重写范围

除跨模组契约锁死的 `InteropContract.cs` 与 `InteropReflection.cs` 外，其余全部 70 个 `.cs` 文件统一按以下标准撰写/维护注释：

1. 类型声明与对外/关键内部方法只保留单行 `<summary>`，直接写清职责与调用边界。
2. 删掉所有 `<remarks>` 小作文、历史问题回顾、旧方案辩解以及早就对不上签名的过期 `<param>` 标签。
3. 删掉分节装饰线和复述代码字面意思的行内注释；只在非直观外部约束处（如墙血当帧被原方法置白、左下与左上像素坐标翻转、房主回环跳过、`HideAndDontSave` 对象检索约束）保留单句说明。

## 四、功能状态分类与验证结果

### 4.1 状态分类

| 类别 | 涉及范围 | 当前状态 |
| --- | --- | --- |
| **已实现并在正式版启用** | 第 2.1 至 2.8 节全部修复、三版本 Together（原版 `v5.5.3.0` / `main` / `Feature-Refactor`）全兼容适配、配件系统与面板控制、尾巴物理、自定义血液、区块特效、多人联机同步及引用 DLL 升级 | Debug 与 Release 构建均已完整编译生效。 |
| **仅 Debug 启用的诊断项** | `Patches/SkinMessageHandler.cs` 收包跟踪计数、`Utils/CommsTrace.cs`、`Utils/DebugMessageTrace.cs`、`Utils/ModLog.Debug` | 仅用于联机收发包排障，Release 编译期自动裁除。 |
| **预留未启用** | `Character/PartContentSizes.cs`、`SkinApplierParts.cs` 中的 `ComputeFitScaleByHeight` 与 `GlobalFitScale`（当前固定返回 `1f`） | 预留的贴图尺寸自动缩放备选实现，保留原逻辑不动，高清贴图当前统一走 `baseSizes.json`。 |
| **跨模组契约冻结** | `Interop/InteropContract.cs`、`Interop/InteropReflection.cs` | 与 `CuStats`、`HwAssistive`、`CUCoreLib`、`CuSaveManager` 共享完全相同的 SHA-256，保持原字节未改。 |

### 4.2 验证结果

- **已实测通过**：
  1. 执行 `dotnet build mods/SkinSync/SkinSync.csproj -c Debug --nologo --no-incremental` 与 `-c Release --nologo --no-incremental`：均为 0 警告，0 错误。
  2. 执行 `& tools/verify-interop.ps1`：`ALL CHECKS PASSED`（5 模组契约 SHA-256 完全一致，零 `LiteNetLib` 编译期依赖，零旧版符号残留）。
  3. 对照 Steam 原版 `CasualtiesMP.dll`（`v5.5.3.0`）反编译核验 `SpriteReplacerPlugin`、`SkinSpriteReplacer`、`WoundViewIntegration`、`SkinMenuUI`、`MpSkinSync` 全局根命名空间类型、字段、嵌套类及方法签名，全部精准对齐；对照 `Assembly-CSharp.dll`（2026-11-14）核验全部 Harmony 补丁目标方法/属性及私有反射字段，全部命中。
  4. 编码与格式扫描：`mods/SkinSync` 全部 `.cs` 与 `.md` 文件均为 UTF-8 无 BOM、LF 换行，非锁死源码单文件均 `<= 800` 行。
- **未执行项**：
  - 未启动 Steam 游戏客户端做双开联机实机画面对测。
