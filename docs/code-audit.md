# SkinSync 代码审计

- 审计对象：`mods/SkinSync`（当前版本 `1.2.4`；审计基线 HEAD `bb0cc24` 时 `SkinSync.csproj` 版本为 `1.2.3`，程序集 `SkinSyncMod`，netstandard2.1，后续修复与 UI/发版升级见 `docs/audit-and-changes.md`）。
- 审计范围：全部 77 个 `.cs` 源文件，共 16,400 行：根目录 `SkinSync.cs` 768 行，`Character/` 4 个文件 437 行，`Config/` 9 个文件 1,430 行，`Interop/` 11 个文件 3,114 行，`Network/` 7 个文件 480 行，`Patches/` 11 个文件 1,787 行，`Resolve/` 4 个文件 123 行，`UI/` 8 个文件 2,133 行，`Utils/` 22 个文件 6,128 行。另外看了 `SkinSync.csproj`、`.gitignore`、`.github/workflows/build.yml` 和 `README.md`。
- 阅读方式：74 个文件逐行读完。另外 3 个文件的处理方式不同，在这里说明：`Interop/InteropContract.cs`（856 行）和 `Interop/InteropReflection.cs`（421 行），我用脚本和 CuSaveManager、CuStats、hwAssistive 里的拷贝比对，去掉注释行、`using` 行和命名空间行之后逐行相同，内容在前面几个模组的审计里已经读过，这里没有重读；`UI/SkinSyncI18n.cs` 的逻辑部分逐行读了，两张文案表没有逐条读文字，只用脚本核对了键。
- 审计方法：只读代码。游戏侧的类型、字段和调用关系对照反编译目录 `.tmp_decomp_game_current` 核对；联机侧对照 `mods/casualties-together/ScavPrototypeMP` 源码核对；和其他模组共用的部分对照 CuSaveManager、CuHotbar、CuStats、hwAssistive 的对应文件。
- 没有做的事：没有编译，没有进游戏运行，没有做联机实测。所有结论都来自读代码，需要进游戏才能定论的放在「待验证清单」。
- 和已有文档的关系：`docs/audit-and-changes.md`（之前的审查与修改记录）和 `docs/character-mapping.md` 我没有改动。本文是另一次独立审计。已有文档或 README 已经写明的事项（例如 `NormalizeSpriteSize` 预留未启用、Interop 契约冻结），我在对应条目里注明，并给出我自己的看法。
- 审计期间没有修改本模组任何源文件，本文档是唯一新增的文件。

标签说明：【高 / 中 / 低】是影响程度。Bug＝行为和意图不符；风险＝特定条件下会出问题；口径＝界面或文档的说法和实际行为不一致；性能；过度设计；待验证＝只读代码无法定论；已核对＝怀疑过、查证后没有问题。

## 一、总体结论

这是五个模组里代码量最大的一个，功能也最多：三个角色的肢体和表情替换、翅膀、配件、自定义血液、区块灯光和粒子、尾巴物理、伤势面板贴图、皮肤预览图，以及联机下的皮肤、配件、尾巴参数同步和按需传皮肤包。

做得好的部分很明确。皮肤包的校验是五个模组里对远端数据最认真的一处（名字、路径、扩展名白名单、PNG 文件头、大小、数量、CRC 都有检查，本地同名皮肤不会被覆盖）。主机会核对消息里的 `netId` 是不是发送者自己的。贴图、材质、网格的创建和销毁基本成对。中英文案各 109 条，键完全对应，没有缺失也没有多余。调试用的跟踪日志在 Release 构建里会被整段编译掉。

问题集中在六处：

1. **没装联机模组时有固定开销。** 探测「有没有 Together」的结果不缓存，每帧 2 到 4 次遍历全部已加载的程序集（IN-1）。只玩单机的用户也会一直付这个开销。hwAssistive 有同样的问题。
2. **两个玩家能直接碰到的 Bug。** 打开「隐藏游戏装备」后，脱下或丢掉的装备会一直看不见（AC-1）；按住移动键时打开面板，角色会一直走（UI-1）。
3. **本该由事件触发或做缓存的工作放在了每帧或每次界面事件里。** 「当前角色设置」页每次界面事件都重新读一遍 `accessories.json`（UI-2）；每取一条文案都重新判断一次语言（UI-3）；第一次打开角色页时一次性同步生成全部皮肤的预览图（PV-1）；伤势面板打开时每帧重新解析皮肤（WV-1）；主机每帧用反射遍历玩家名册（NW-7）；本机每帧重新找自己的角色（ST-3）。
4. **对远端数据的限制还缺几项。** 皮肤包里的 PNG 没有限制宽高（PK-1），区块特效和血液配置里的数值没有上限（ZN-1、BL-3），配件消息没有限频、条目数量没有上限（NW-1），分片发送的流控按帧计而不是按时间计（NW-2）。其中 PK-1 和 ZN-1 的远端部分只在打开实验性的「按需拉取缺失皮肤包」时成立，这个开关默认关闭。
5. **启动环节脆弱。** 11 个补丁类连续安装，没有逐个隔离，任何一个失败都会让整个 `Awake` 中断，之后每帧报空引用（ST-2）。
6. **重复和死代码。** 尾巴的 17 个参数在 11 处以上各手写一遍（TL-1）；贴图尺寸归一化这条链路约 380 行目前不起任何作用（AP-1）；预览图为了一张 100×100 的静态缩略图，用软件重新实现了一遍灯光、粒子和序列帧（PV-2）；Interop 通用层 1,577 行只服务一个联机模组。

## 二、最重要的问题

细节和行号在第八节对应文件下面，这里只列结论。

| 编号 | 标签 | 位置 | 问题 | 建议 |
| --- | --- | --- | --- | --- |
| IN-1 | 高·性能 | `Interop/Providers/TogetherProvider.cs:326-362, 494-505` | 没装 Together 时，「找不到程序集」不缓存，每帧 2 到 4 次全量扫描已加载程序集 | 启动时查一次 BepInEx 插件表，没有就永久记为不可用 |
| AC-1 | 中·Bug | `Utils/EquipmentHider.cs:26-64` | 开着「隐藏游戏装备」时脱下或丢掉装备，装备的贴图不会恢复显示 | 每次轮询把已不在肢体下面的渲染器恢复并移出记录 |
| UI-1 | 中·Bug | `UI/PreRunGuardPatch.cs:55-62` | 面板打开时整个跳过 `PlayerCamera.HandleInput`，移动方向不清零，角色保持开面板前的移动 | 跳过前把移动方向清零 |
| ST-2 | 中·风险 | `SkinSync.cs:65-66, 130-146, 278-329` | 11 个补丁类安装没有逐个隔离；一个失败则 `Awake` 中断，`Settings` 和窗口为空，之后每帧空引用 | 逐个 try/catch 并用 Warning 记录；先建 `Settings`；`Update` 和 `OnGUI` 加空判断 |
| PK-1 | 中·风险 | `Utils/SkinPackCodec.cs`，5 处 `LoadImage` | 远端皮肤包里的 PNG 只限文件大小，不限宽高，解码后可以非常大（仅实验开关打开时） | 校验时读 PNG 文件头里的宽高，超限拒收 |
| ZN-1 | 中·风险 | `Utils/ZonesAttacher.cs:71-200`、`Config/ZonesConfig.cs` | 区块的数量、粒子速率、寿命、灯光半径和强度都没有上限，远端皮肤包可以带这些配置 | 统一夹取数值，限制每个角色的区块数；加「只对自己的皮肤启用灯光和粒子」开关 |
| NW-1 | 中·风险 | `Patches/SkinMessageHandler.cs` | 主机对任何消息都不限频；配件消息的条目标识长度和数量没有上限，偏移和层级不夹取 | 每个客户端限频；限制标识长度和每人条目数；夹取偏移和层级 |
| NW-2 | 中·风险 | `Network/MultiplayerSender.cs:224-270` | 分片流控是「每帧 8 片」，和帧率挂钩；2 MB 的包 8 帧内全部入队；转发给多人时只算 1 片 | 改成按目标、按每秒字节数的预算 |
| NW-3 | 中·性能 | `Patches/SkinMessageHandler.cs:377-381` | 主机第一次收到某个皮肤包就整包转发给其他所有客户端，不管对方有没有请求 | 主机只通知「有这个包」，客户端缺了再请求 |
| NW-7 | 中·性能 | `Patches/SkinCacheBroadcaster.cs:99-155` | 主机每帧取场景名并用反射遍历玩家名册 | 降到 0.5 秒一次，或改成玩家加入事件触发 |
| ST-3 | 中·性能 | `SkinSync.cs:341-367, 568-583, 675-689` | 每帧 `GetComponentInChildren<Body>`；主菜单里每帧 `FindObjectOfType<Body>` 加按标签查找；联机时每帧反射取本机 `netId` | 缓存引用，低频轮询或用已有的事件 |
| UI-2 | 中·性能 | `UI/SkinSyncWindowTabs.cs:461` → `SkinSync.cs:389-422` | 「当前角色设置」页每次界面事件都读盘并解析 `accessories.json` | 缓存行数据，换皮肤、改配件、重新扫描时失效 |
| UI-3 | 中·性能 | `UI/SkinSyncI18n.cs:260-349` | 每取一条文案都重新判断语言（字符串处理加读游戏语言名），一次界面事件几十到上百次 | 语言判断结果按帧缓存，或在设置变化时更新 |
| PV-1 | 中·性能 | `UI/SkinSyncWindowTabs.cs:340`、`Utils/SkinPreviewRenderer.cs:98-486` | 第一次打开角色页，在一次界面事件里同步生成所有皮肤的预览图，每张约 20 次读盘和解码 | 每帧最多生成一张，没生成的先画占位；只生成可见格子 |
| WV-1 | 中·性能 | `Utils/WoundViewAttacher.cs:168-209` | 伤势面板打开期间每帧重新解析当前皮肤并逐个肢体取组件（原版每帧调用 `UpdateView`） | 每帧只比较目标对象和物种，皮肤变化走已有的刷新入口；缓存图片引用 |
| AP-2 | 中·性能 | `Utils/SkinApplier.cs:270-295`、`Patches/SkinMessageHandler.cs:189-191` | 远端改一个配件，接收方重读 `accessories.json`、销毁并重建全部配件、重做血液、全局搜索伤势面板 | 增加只改单个配件的路径；按皮肤缓存解析结果 |
| AP-3 | 中·性能 | `Utils/SkinApplier.cs:471-489` | 角色每次转身都重新解析全部肢体、尾巴、表情贴图，伴随对象分配和字符串拼接 | 按皮肤预先算好有没有分朝向的贴图，没有就整个跳过；有则缓存成对的贴图 |
| ZN-2 | 中·性能 | `Utils/ZonesAttacher.cs:35-69` | 每个肢体做两次递归目录搜索，再解码遮罩图并逐像素计算，没有缓存 | 每个皮肤建一次文件名索引；按路径缓存解析结果 |
| TL-1 | 中·过度设计 | 尾巴参数相关的 11 处以上 | 17 个参数在结构体、配置、序列化、网络、夹取、界面里各手写一遍，默认值写了 3 份 | 用一张参数描述表驱动 |
| AP-1 | 中·过度设计 | `Utils/SkinApplierParts.cs:104-224`、`Character/PartContentSizes.cs` | 尺寸归一化链路恒等返回，约 380 行不起作用，对应的配置项对用户可见但无效 | 删除或移到分支；至少隐藏配置项 |

## 三、过度设计的地方

1. **尾巴参数的重复（TL-1）。** 17 个字段在以下位置各出现一次：`TailDeformParams` 结构体、`TailDeformConfig` 的静态字段、`Defaults`、`LocalSnapshot`、`SkinSyncSettings.TailDeformOverride`、`AssignTailField`、`SerializeTailOverrides`、`SkinSync.ApplyTailOverrideToConfig`（468-487）、`SaveCurrentTailToSettings`（489-515）、`TailSyncMessage`、`SkinMessageCodec.WriteTail/ReadTail`、`MultiplayerSender.BuildLocalTailMessage`、`SkinMessageHandlers.ApplyTailOverride` 里的夹取、界面上的 17 个控件（1 个开关、16 个滑块）。加一个参数要改十多处，漏一处不会编译报错。
2. **尺寸归一化的死链路（AP-1）。** `GlobalFitScale` 恒返回 1，`FitToTarget` 因此恒返回传入的贴图，后面的重采样、缓存、沿六个函数传递的 `scale` 参数、126 行的 `PartContentSizes` 数据表都不起作用。README 和已有文档都写了「预留未启用」，这一点没有隐瞒。我的看法是预留代码放在主干上有维护成本，而且配置文件里的 `NormalizeSpriteSize` 对用户可见、改了没有任何效果。
3. **预览图里的区块特效（PV-2）。** `SkinPreviewZones.cs` 用 320 行在 CPU 上重新实现了点光、定向光、双色渐变、呼吸、五种粒子模式、四种淡出曲线和序列帧，目的是往一张 100×100 的静态缩略图上叠效果。时间参数在两个调用点都固定传 0，和时间相关的分支实际只算了一个时刻。
4. **Interop 通用层。** `InteropContract`（856）、`InteropReflection`（421）、`InteropRegistry`（121）、`InteropDiagnostics`（118）、`InteropBootstrap`（61）合计 1,577 行通用设施，只为一个提供方（Together）服务。已有文档说明这是几个模组共用的冻结契约。问题在于它是以拷贝的形式共用的，每个模组各带一份，享受不到「改一处、处处生效」的好处（见 X-2）。
5. **手写的序列化。** 配件微调和尾巴参数各自存在一个字符串配置项里，为此写了两套解析、两套序列化和一对转义函数（`Config/SkinSyncSettings.cs:203-417`）；`BaseSizesLoader.ParseContent` 手工扫描 JSON 的引号和括号（28-65）。工程已经引用了 Newtonsoft.Json，其他配置文件也在用它。
6. **成对出现的重复机制。** 停用 Together 旧皮肤系统有两处（`LegacyTogetherSkinSuppressor` 和 `SkinApplierParts.DisableConflictingReplacers`）；判断 16 位读写原语是否可用有两处（`TogetherProvider._ushortDegraded` 和 `ChannelConsumer.SupportsUShortPrimitive`）；往主菜单注入按钮有两条路径（场景加载回调和 `PreRunScript.Start` 后置补丁）；配件登记有两套表（`AccessoryAttacher` 的 `Mounted`/`Meta` 和 `_byBody`）。
7. **发送代码的复制。** `MultiplayerSender` 有 6 个结构几乎相同的发送方法，「小包直发、大包分片」的判断写了 3 份（274-337）。
8. **血液功能的选项面。** 自定义粒子贴图、随机颜色、四种淡出曲线、发光及强度、序列帧动画、肢体血迹贴图重着色、鼻血、呕血，合计 773 行。如果现有皮肤确实用到了这些，可以保留；如果只有一两套皮肤用到其中一部分，值得按实际使用情况收窄。这一条我没有数据，只是提出问题。

## 四、优化建议（按收益排序）

1. **把「Together 不存在」记下来（IN-1）。** 本模组已经声明了对 `CasualtiesMP` 的软依赖（`SkinSync.cs:16`），BepInEx 会保证 Together 如果装了就先加载。所以 `Awake` 里查一次 `Chainloader.PluginInfos.ContainsKey("CasualtiesMP")`，没有就永久记为不可用，之后所有 `IsAvailable`、`IsRunning` 直接返回 false。同时可以跳过 60 秒的登记重试（ST-4）。
2. **轮询改事件。** 本机角色和物种（ST-3）可以用 Together 已经提供的 `OnBodyReadyOrRebuilt`，单机下用低频轮询加引用缓存。主机名册（NW-7）用玩家加入和离开事件。伤势面板（WV-1）已经有 `RefreshForBody` 这个入口，每帧只需要比较对象和物种。装备隐藏（AC-2）可以在 `Body.WearWearable` 后面挂一个后置补丁。
3. **界面缓存。** 配件行（UI-2）、语言判断（UI-3）、皮肤列表拷贝（UI-4）都是「数据很少变、每次事件都重算」。统一做法是加一个版本号，数据变化时加一，界面发现版本号变了再重算。
4. **每个皮肤建一次文件索引。** `LoadCharacterSprites`、区块特效、伤势面板、预览图各自遍历同一个皮肤目录。加载皮肤时建一张「文件名 → 路径」的表，四处共用，可以去掉 ZN-2、WV-3 和 PV-1 里的大部分磁盘访问。
5. **远端数据的限制集中到一处。** PNG 宽高（PK-1）、区块和血液数值的夹取（ZN-1、BL-3）都放进 `SkinPackCodec` 的校验和各配置加载器的解析函数里。本地皮肤也走同一套夹取，行为一致。
6. **贴图内存。** 现在 `LoadCharacterSprites` 加载的皮肤贴图都保持可读，CPU 和显存各存一份。只有尾巴贴图需要读像素（`TailFlowDeform.BuildProfileFromPixels`）。其余贴图在 `LoadImage` 时标记为不可读，内存占用大约减半。
7. **反射调用换成委托。** Interop 里每次调用都是 `MethodInfo.Invoke` 加 `object[]` 分配和装箱（IN-4）。解析成功时用 `Delegate.CreateDelegate` 建好委托，后续调用没有分配。
8. **尾巴网格只更新顶点（TL-2）。** UV 和三角形在贴图不变时是固定的，建一次即可。
9. **单个配件的增量更新（AP-2）。** 本机改配件已经有 `AccessoryAttacher.SetTransform` 这条轻量路径，远端改配件也应该走同样的路径，不需要整套重建。
10. **尾巴参数表（TL-1）。** 一张表列出每个参数的名字、默认值、上下限和读写方法，配置、序列化、网络、夹取、界面都从表生成。

## 五、待验证清单

这些条目只读代码无法定论，需要进游戏确认。

1. AC-1：打开「隐藏游戏装备」，穿上一件装备再脱下或丢到地上，看它是否不可见；关掉开关后是否恢复。
2. UI-1：按住移动键的同时按 F10 打开面板，松开按键，看角色是否继续移动。
3. ST-1：管理对象被设成隐藏后，其他用 `FindObjectOfType` 找插件组件的模组是否受影响；删掉这两行后本模组在切换场景时是否仍然存活。
4. IN-1：单机下用性能分析器看 `AppDomain.GetAssemblies` 每帧的调用次数和耗时。
5. NW-2：高帧率下给多个客户端发 2 MB 皮肤包，看是否有分片丢失或连接被断开。
6. PK-1：构造一张文件很小但宽高很大的 PNG 放进皮肤包，看接收方的内存和表现。只在自己的测试房间里做。
7. PV-1：`CustomSprites` 里放 30 套以上皮肤，第一次打开角色页的卡顿时间。
8. X-1：本模组（未嵌入存档管理器时）和另一个模组的面板同时打开，按「先开的先关」的顺序关闭，看游戏自带界面的点击是否失效。
9. NW-8：切换场景的过程中收到的换肤消息是否丢失，进入新场景后其他玩家的皮肤是否完整。
10. NW-5：重新加入房间时，主机缓存的旧皮肤名是否会覆盖本机刚选的皮肤。
11. ST-5：没有解析到本机角色时在面板里点「应用」，场景里其他 `Body`（联机下是其他玩家）是否被套上本机的皮肤。
12. TL-3：30 帧和 144 帧下同一套尾巴参数的摆动幅度是否明显不同。
13. PT-3：`AccessTools.TypeByName` 找不到类型时 HarmonyX 是否输出警告。
14. AC-4：每个配件一个 `ShadowCaster2D` 对帧率的影响。

## 六、建议的修复顺序

1. IN-1 和 ST-2。前者是所有单机用户都在付的固定开销，改动很小；后者决定游戏更新后模组是「少一个功能」还是「整体失效并刷屏报错」。
2. AC-1 和 UI-1。两个玩家能直接碰到的 Bug，改动都只有几行。
3. PK-1、ZN-1、NW-1。给远端数据补上限制。实验开关转正之前应该完成。
4. UI-2、UI-3、PV-1、WV-1。面板和伤势面板打开时的卡顿。
5. NW-2、NW-3、NW-7。传包的流控和主机侧的开销。
6. ST-1 和第九节的跨模组事项。需要和其他模组的作者协调。
7. AP-1、TL-1 和第三节的其他重复。不影响功能，但会持续增加维护成本。

## 七、做得好的地方

- **皮肤包校验。** 皮肤名限制 64 个字符，不允许路径字符、`..`、控制字符和 Windows 保留设备名。包内相对路径不允许 `..`、反斜杠和冒号，扩展名只允许 `.png`、`.zones.json` 和四个固定名字的根目录 JSON。PNG 检查文件头签名。整包上限 2 MB，单文件 512 KB，文件数 256。分片重组时先看第一片的包头再分配内存，每个发送者同时最多 16 个传输，30 秒超时，CRC32 校验。本地已有的同名皮肤不会被远端包覆盖。
- **主机侧的身份核对。** 换肤、配件、尾巴三类消息，主机都核对消息里的 `netId` 是否属于发送者。尾巴的 17 个参数全部夹取并处理了 NaN，配件的旋转角也做了处理。主机只接受「发送者正穿着」或「有人请求过」的皮肤包。
- **实验功能默认关闭，并且收发两端都检查开关。** 皮肤包的每个收发入口都先看 `EnableSkinPackSync`（`Patches/SkinMessageHandler.cs:273, 313, 332, 340, 404, 412`）。写盘需要发送方和接收方同时同意。
- **资源生命周期。** `ReapplyForSkin` 和 `ReloadAllCachedSkins` 会销毁退役的贴图和精灵。`BloodAttacher.ClearCaches` 会避开还被场上粒子引用的材质。`TailFlowDeform.OnDestroy` 清理网格、材质和子对象。`WoundViewAttacher` 在销毁贴图前先把面板还原成原版。
- **日志分级。** `ModLog.Debug`、`CommsTrace`、`DebugMessageTrace` 都带 `[Conditional("DEBUG")]`，Release 构建里调用和参数求值都被去掉。`DebugMessageTrace` 按 0.5 秒窗口合并输出。
- **文案完整。** 中文 109 条，英文 109 条，键完全一致，没有重复键，全部被用到（84 条经 `T()`，6 条经 `F()`，19 条装备槽位名经 `LocalizeWearSlot`）。
- **加载顺序有保证。** 对存档管理器和 Together 都声明了软依赖。对 Together 的新接口（`TogetherPanelApi` 的事件和 `RegisterExternalInputBlocker`）是可选绑定，旧版本也能工作。我核对了这几个接口在 Together 源码里的签名，和本模组的用法一致。
- **和存档管理器的整合。** 嵌入存档管理器时不再画自己的按钮、不再响应自己的面板热键，共用对方的窗口和输入拦截，避开了 X-1。
- **界面写配置的频率有控制。** 尾巴滑块在松开鼠标时才保存和广播；配件微调用步进按钮，一次点击一次事件。
- **负缓存。** 没有血液配置的皮肤（`_noBloodSkins`）和没有伤势贴图的皮肤（`_emptySkins`）会被记下来，不重复读盘。
- **对原版行为的处理有依据。** 墙面血迹在 `LateUpdate` 里补色，是因为原版 `BleedParticle.Update` 在生成后把颜色写成白色（反编译 `BleedParticle.cs:67`）。`Eye` 是结构体，表情替换里「取出、修改、写回」的写法是对的（反编译 `Eye.cs:5`）。
- **文档和构建。** 有 README、角色贴图映射表和一份 40 KB 的审查修改记录，有 CI 构建脚本，新克隆的仓库可以直接编译。

## 八、逐文件审计记录

每个文件后面括号里的数字是行数。没有列出问题的文件表示读完没有发现需要改的地方。

### 工程文件与文档

**`SkinSync.csproj`（88 行）、`.gitignore`（5 行）、`.github/workflows/build.yml`（37 行）、`README.md`**

- PJ-1【低·风险】`references/` 下的 17 个 DLL 都提交在仓库里，包括游戏本体的 `Assembly-CSharp.dll` 和 Unity 引擎的模块。好处是新克隆的仓库和 CI 都能直接编译。README 里给出了仓库的 GitHub 地址，如果仓库是公开的，把游戏本体的程序集放进去涉及再分发授权。常见做法是只提交去掉方法体、只留签名的引用程序集。
- PJ-2【低】没有自动化测试。皮肤名和路径校验、皮肤包编解码、分片重组、自然排序、覆盖项字符串的解析和序列化，基本都是不依赖 Unity 的纯逻辑，适合加单元测试。校验部分尤其值得，它是对远端数据的主要防线。
- CI 只做 Release 构建并上传 DLL。
- README 和代码核对过的部分是一致的：四个默认热键、传包的四个大小上限、`NormalizeSpriteSize` 预留未启用、血液渲染默认关闭、目录名以 `_` 或 `.` 开头会被跳过。有两处出入：README 说远端参数不会改动本地配置文件，但有一个例外（NW-5）；README 列了联机测试人员，面板「关于」页里对应位置还是占位符（UI-7）。

### 入口：`SkinSync.cs`（768）

- ST-1【低·风险】`Awake` 的 59-60 行对 `gameObject` 设置 `HideFlags.HideAndDontSave` 并调用 `DontDestroyOnLoad`。BepInEx 把所有插件都挂在同一个管理对象上，所以这里改的是全体插件共用的对象。带 `DontSave` 标记的对象不会被 `FindObjectsOfType` 返回（本模组自己在 `Patches/LegacyTogetherSkinSuppressor.cs:206` 的注释里就提到了这一点），也就是说其他模组如果用这类方法找插件组件，装上本模组后就找不到了。`DontDestroyOnLoad` BepInEx 自己已经调用过，这里是重复的。BepInEx 有一个配置项专门控制是否隐藏管理对象，这两行等于替用户强制打开了它。建议删掉；如果确实遇到过管理对象被游戏销毁的情况，在 README 里说明让用户改 BepInEx 的配置。同样的两行在 CuSaveManager、CuHotbar、CuExtraction 里也有，要改需要一起改（X-6）。实际影响已列入待验证清单。
- ST-2【中·风险】`RegisterPatches`（130-146）在 `Settings` 创建（66 行）之前执行（65 行），里面 11 个 `harmony.CreateClassProcessor(...).Patch()` 连续调用，没有逐个 try/catch。游戏更新后任何一个目标方法改名，Harmony 会抛异常，`Awake` 在这里中断：后面的补丁不装，`Settings`、`_window`、`_overlay` 都是空。组件本身仍然启用，`Update`（278-312）和 `OnGUI`（324-329）没有空判断也没有 try/catch，284 行和 327 行会每帧报空引用，日志被刷满。建议：每个补丁类单独 try/catch，失败时用 Warning 写明类名和原因（hwAssistive 的 `PatchAllSafely` 是这个结构，只是它用错了日志级别）；把 `Settings` 的创建挪到补丁之前；`Update` 和 `OnGUI` 开头判断 `Settings` 和 `_window` 是否为空。
- ST-3【中·性能】`Update` 里每帧做的查找：单机下 `ApplyPersistentSkinIfNeeded`（341-360）每帧调用 `availableSkins.Contains` 和 `ReadCharType`，后者每次都 `GetComponentInChildren<Body>(true)`（362-367）；场景里没有角色时（主菜单），每帧 `FindObjectOfType<Body>()` 加 `FindGameObjectWithTag("Player")`（568-583）；联机下 `ReportLocalSkinIfNeeded`（675-689）每帧用反射取本机 `netId`、再取一次 `Body`、再用 `SceneManager.GetActiveScene().name` 和字符串比较。建议：找到本机角色时把 `Body` 引用一起缓存，物种变化直接读缓存的 `Body.charType`；没有角色时每 0.25 秒找一次；场景名在 `sceneLoaded` 回调里记下来。
- ST-4【低·口径】只玩单机时，每次启动日志里会出现：一条 Info「Together 不可用，等待重试」（`Patches/TogetherReceiverRegistrar.cs:24`），然后协程每秒重试一次、共 60 次（149-174），最后一条 Warning「皮肤同步包在 60 秒内未登记成功……本次会话皮肤同步不可用」（173）。没装 Together 是正常情况，Warning 会让用户以为出了故障。建议先查插件表，没装就不启动协程，只记一条 Info。
- ST-5【低·风险】`SwitchToSkin` 的第三个分支（651-663）在没有解析到本机角色时调用 `SkinApplier.ApplyToScene`（`Utils/SkinApplier.cs:517-529`），给场景里每一个 `Body` 都套上这套皮肤。热键切换在 `TryGetLocalPlayer` 成功之后才会执行，不会走到这里；但面板上的「应用」按钮（`ApplySkinByName`）随时可以点。联机时如果本机角色还没解析出来而场景里已经有其他玩家，他们在本机画面里会被套上本机的皮肤。建议联机会话进行中不走这个兜底分支。
- ST-6【低·性能】`ScanAvailableSkins`（206-237）每次都调用 `SkinApplier.ReloadAllCachedSkins` 和 `SkinPreviewRenderer.InvalidateAll`，不管磁盘上有没有变化。按 F8 或点「重新扫描」会销毁并重新加载所有已缓存的皮肤，预览图也全部重画。这是用户主动触发的操作，可以接受；皮肤多的时候可以按目录修改时间只重载有变化的。
- ST-7【低·口径】几个标识写在多处：插件标识 `"com.Bytechey.skinsync"` 有 4 处（14 行、61 行、`TogetherHooksBridge.ModId`、`UpdateChecker.PluginGuid`）；版本号有 2 处（14 行的特性和 `SkinSync.csproj:9`），并且 `SkinSync.Version` 从特性读、`UpdateChecker` 从 BepInEx 插件表读；`"CasualtiesMP"` 有 4 处（20 行、`InteropContract.cs:393`、`TogetherProvider.cs:15`、`LegacyTogetherSkinSuppressor.cs:16`）。建议各留一个常量。
- ST-8【低·风险】每次启动递归删除 `SkinPathResolver.SyncCacheRoot`（62 行）。路径是固定的 `plugins/SkinSync_SyncCache`，不会删到别处（已核对）；每次清空是 README 写明的设计。可以改进的只有位置：这个目录在 BepInEx 会扫描的 `plugins` 下面，放到 BepInEx 的 `cache` 目录更合适。
- `NaturalCompare`（251-276）在数字段超出 `long` 范围时按相等处理，影响极小。
- `ToggleAccessory` 和 `SetAccessoryTransform`（424-442）每次点击会写配置、让这套皮肤的预览图失效、广播增量。预览图里画了配件，失效是必要的。
- `OnApplicationQuitting`（194-204）释放顺序合理。退出时没有卸载 Harmony 补丁，进程即将结束，没有影响（已核对）。
- 热键相关的问题见 UI-6，`OnGUI` 里的闭包分配见 UI-4，配件行的读盘见 UI-2。

### `Config/`（9 个文件，1,430 行）

**`Config/SkinSyncSettings.cs`（429）**

- CF-1【低·过度设计】配件微调和尾巴参数各存在一个 `ConfigEntry<string>` 里，格式是自己定的，为此有两套解析、两套序列化和一对转义函数（203-417）。每次修改都重新拼整个字符串并赋给 `.Value`，BepInEx 随即重写整个 cfg 文件。点一下步进按钮写一次盘，频率不高，可以接受。建议改成一个单独的 JSON 文件，用 Newtonsoft 读写，按皮肤分组；cfg 里也不再出现一长串不可读的文本。
- CF-2【低·风险】`SyncMode` 是字符串（125 行，默认 `"OnEnter"`），代码里有 4 处和 `"Passive"` 做比较。用户手改 cfg 写错大小写或拼写时，会被当成另一种模式，没有提示。建议改成枚举类型的配置项。
- CF-6【低·口径】配置项的说明文字只有中文，而界面是中英双语。`NormalizeSpriteSize`（109 行，默认 true）改了没有任何效果（见 AP-1），建议在说明文字里写明，或者先不绑定这个配置项。
- 四个默认热键在 81-92 行：F10 开关面板，F6 下一个皮肤，F7 上一个，F8 重新扫描。F6 和 hwAssistive 写死的面板热键冲突，见 X-5。

**`Config/TailDeformConfig.cs`（202）**

- 属于 TL-1 的一部分：静态字段、`Defaults`、`LocalSnapshot` 各列了一遍 17 个参数。
- `ResolveFor`（110-137）每条尾巴每帧调用一次。本机角色只做一次引用比较，远端角色是两次字典查找，开销很小（已核对）。远端覆盖表在写入时会清理已销毁的对象。

**`Config/ZonesConfig.cs`（287）**

- `ComputeGeometry`（90-150）逐像素统计每个区块的中心和半径，用了 4 个 `Dictionary<byte,int>`，每个像素做多次字典读写。区块编号是一个字节，换成 4 个长度 256 的数组即可。`Load` 没有缓存，调用方也没有，见 ZN-2。
- 区块配置里的数值在这里解析，没有夹取，见 ZN-1。102 行的 `LoadImage` 同样没有宽高限制，见 PK-1。

**`Config/BaseSizesLoader.cs`（75）**

- CF-3【低·过度设计】`ParseContent`（28-65）手工扫描引号和括号来解析 JSON，外面包了一个空的 catch，格式稍有变化（比如嵌套、转义字符）就会静默得到空结果。工程已经引用了 Newtonsoft.Json。另外，皮肤目录解析不到时 `Load` 返回一个没有缓存的空表，每次调用都重新解析一遍路径。

**`Config/SkinCacheStore.cs`（78）**

- CF-4【低·性能】每改一条记录就把 `persistentDataPath/SkinSync/skinCache.txt` 整个重写一遍。主机上每个客户端每换一次皮肤都会触发（见 NW-6）。建议标记为脏，延迟几秒合并写一次。另外，皮肤名里含 `=` 的记录会被静默丢弃，而皮肤名校验并不禁止 `=`。

**`Config/AccessoryConfig.cs`（131）、`Config/WingsConfig.cs`（103）、`Config/BloodConfig.cs`（117）**

- 解析逻辑没有发现问题。三个文件读出来的数值都没有夹取，这些文件可以随远端皮肤包到达，见 BL-3 和 ZN-1 的建议。

**`Config/BloodRenderConfig.cs`（8）**

- 整个文件只有一个静态布尔字段，可以并进 `BloodAttacher` 或直接读 `Settings`。

### `Character/`（4 个文件，437 行）和 `Resolve/`（4 个文件，123 行）

**`Character/CharacterCatalog.cs`（248）**

- CF-5【低·风险】`EnsureLoaded`（43-108）遇到异常时直接返回，没有把 `_profiles` 设成任何值。之后每次调用都会重试一遍（包括资源加载）并再记一条 Warning。建议失败后记为「已尝试」，用空表继续，只警告一次。

**`Character/PartContentSizes.cs`（126）**

- 整张数据表没有任何引用（用搜索确认过），属于 AP-1。

**`Character/CharacterProfile.cs`（40）、`Character/PartKeyNames.cs`（23）**

- 没有发现问题。

**`Resolve/SkinResolver.cs`（60）、`Resolve/SidedSpriteLookup.cs`（32）、`Resolve/ResolveOutcome.cs`（20）、`Resolve/ResolveOrigin.cs`（11）**

- 三步查找（角色原生名、通用部位名、借用其他角色）和 README 的描述一致。
- `ResolveOutcome` 是类，每次解析分配一个对象；`SidedSpriteLookup.Find` 每次查找都拼接字符串（`"R_"` 前缀、`F`/`B` 后缀）。只在套皮肤时调用的话没有问题，但它们也在每次转身的路径上，见 AP-3。

### `Interop/`（11 个文件，3,114 行）

**`Interop/Providers/TogetherProvider.cs`（719）**

- IN-1【高·性能】`EnsureResolved`（326-362）在找不到 Together 程序集时不记录结果，注释说这是有意的，为了以后能重试。于是每次调用都会执行 `FindTargetAssembly`（494-505）：取 `AppDomain` 里全部已加载的程序集，逐个调用 `GetName()`（每次都分配一个对象）。没装 Together 时，每帧的调用点有：`SkinSync.Update` → `MultiplayerSender.TickOutboundChunks`（`Network/MultiplayerSender.cs:227`）；`TryGetLocalPlayer`（`SkinSync.cs:547` 和 563，有皮肤时）；`SkinCacheBroadcaster.Update`（`Patches/SkinCacheBroadcaster.cs:101`）。也就是每帧 2 到 4 次全量扫描。设置页打开时，每次界面事件还要再加一次（`UI/SkinSyncWindowTabs.cs:141`）。装了较多模组的游戏里已加载程序集有上百个，这是每帧几百次小对象分配。「留着重试」的理由在本模组不成立：软依赖已经保证 Together 先加载。修法见第四节第 1 条。
- IN-2【低·风险】`PutBytes`（697-700）遇到长度超过 65535 的数组，会静默地换成空数组发出去。目前单包上限 48 KB、分片 32 KB，都在这个限制以内，不会触发。但以后有人调大常量时，表现会是「对方收到空包」，很难查。建议直接抛异常或记 Warning 并返回失败。
- IN-3【低·口径】当对方缺少 16 位读写原语时，`PutUShort` 退回到写 4 个字节，`GetUShort` 返回 0，读写两侧不对称。`ChannelConsumer` 在这种情况下不登记接收器，但发送没有被拦住。另外这件事有两个标志在记（`_ushortDegraded` 和 `ChannelConsumer.SupportsUShortPrimitive`）。建议原语缺失时整个通道停用，收发一起关，只留一个标志。
- IN-4【低·性能】每次调用都走 `MethodInfo.Invoke`：`GetPlayers` 每次新建一个列表；`TryGetPlayerByActor` 对名册里每个条目做一次反射调用；`Put` 每写一个字段分配一个 `object[]` 并装箱；`RosterConsumer.EnumeratePlayersWithBody` 每个玩家大约 4 次反射调用。配合 NW-7 和 ST-3 的每帧调用，开销会叠加。解析成功后建立委托可以去掉这部分。
- IN-6【低·口径】`TrySubscribePlayerJoined` 恒返回 false，但提供方声明自己支持事件能力。NW-7 里主机靠每帧轮询来发现新玩家；把这个事件接上以后，那里可以改成事件触发。`TryRefresh`（366）没有调用方。

**`Interop/Providers/TogetherProviderAdapters.cs`（133）**

- 没有发现问题。

**`Interop/Consumers/RosterConsumer.cs`（375）**

- IN-5【低·风险】`EnsureResolved`（331-372）先把 `_resolved` 设成 true 再去解析。如果第一次调用时解析失败，之后不会重试，名册功能在整个会话里失效。目前调用方都先判断 `SessionConsumer.IsAvailable`，实际不会触发。建议解析成功后再置标志。
- `TryGetLocalNetBody` 每次调用一次反射加一次 `object[]` 分配，属于 IN-4。

**`Interop/Consumers/ChannelConsumer.cs`（128）、`Interop/Consumers/SessionConsumer.cs`（29）、`Interop/Consumers/SkinMessageCodec.cs`（153）**

- `ChannelConsumer.SupportsUShortPrimitive` 见 IN-3。
- `SkinMessageCodec` 的 `WriteTail`/`ReadTail` 是 TL-1 的一部分。读出来的皮肤名由调用方用 `IsSafeSkinName` 校验；配件标识字符串没有长度限制，见 NW-1。

**`Interop/InteropBootstrap.cs`（61）、`Interop/InteropRegistry.cs`（121）、`Interop/InteropDiagnostics.cs`（118）、`Interop/InteropContract.cs`（856）、`Interop/InteropReflection.cs`（421）**

- IN-7【低·口径】`InteropDiagnostics.Report` 用的是 `UnityEngine.Debug.Log`，不走插件自己的日志。只玩单机时每次启动都输出一行 `[Interop] providers=[Together:off] … missing=[…]`，读起来像报错。建议改用 `ModLog`，没装 Together 时措辞改成「未安装联机模组，联机同步停用」。
- 这五个文件合计 1,577 行，是第三节第 4 条说的通用层，跨模组的问题见 X-2。`InteropBootstrap`、`InteropRegistry`、`InteropDiagnostics` 我逐行读了；后两个和 CuStats、CuSaveManager 的拷贝相比，代码行相同，只差注释。`InteropRegistry` 每次取能力接口都会问一遍提供方是否可用（91-119），这就是 IN-1 里每次调用都触发扫描的路径。
- 已有文档说契约文件在各模组里「完全相同的 SHA-256」。准确的说法是：校验脚本去掉前两行和命名空间行之后哈希相同，原始文件的哈希并不相同。这只是措辞问题。

### `Network/`（7 个文件，480 行）

**`Network/MultiplayerSender.cs`（386）**

- NW-2【中·风险】`TickOutboundChunks`（224-270）每帧最多发 8 片，每片 32 KB，即每帧 256 KB。注释写的目的是防止击穿 Steamworks 的 1 MB 发送缓冲区。预算按帧计而不是按时间计，所以发送速率和帧率成正比：一个 2 MB 的包（64 片）在 8 帧内全部入队，60 帧时是 0.13 秒，144 帧时更短；前 4 帧就已经往同一个连接写入了 1 MB。缓冲区会不会被写满取决于它排空的速度，也就是对方的带宽，这正是按帧计数保证不了的。另外，一个「转发给除某人以外所有人」的任务每片只计 1 次，实际发给了多个客户端，主机的总上行流量是计数的若干倍。建议改成按连接、按每秒字节数的预算（例如每个连接每秒 256 KB）。已列入待验证清单。
- NW-9【低·过度设计】有 6 个结构几乎相同的发送方法，「小包直发、大包分片」的判断写了 3 份（274-337）。可以收成一个「发送目标」参数加一个公共函数。`ServerBroadcastSkinChange` 没有检查 `netId` 是否为 0，其他发送方法有类似检查。
- `BuildLocalTailMessage` 是 TL-1 的一部分。每发一片分配一个 32 KB 的临时数组（248 行），次数有限，可以接受。

**`Network/3.3_SkinChangeMessage.cs`（21）、`Network/3.4_AccessorySyncMessage.cs`（15）、`Network/3.5_TailSyncMessage.cs`（27）、`Network/3.6_SkinPackRequestMessage.cs`（8）、`Network/3.7_SkinPackDataMessage.cs`（9）、`Network/3.8_SkinPackChunkMessage.cs`（14）**

- 文件名带 `3.3_` 到 `3.8_` 这样的数字前缀，和类名不一致，搜索和跳转都不方便。消息编号常量 `SkinNetworkIDs`（50000 到 50005）放在 `3.3_SkinChangeMessage.cs` 里，建议单独一个文件。`TailSyncMessage` 的 17 个字段属于 TL-1。

### `Patches/`（11 个文件，1,787 行）

**`Patches/SkinMessageHandler.cs`（436）**

- NW-1【中·风险】主机对换肤、配件、尾巴、请求皮肤包这几类消息都没有限频。每条通过校验的配件消息会转发给所有人，接收方对这个角色执行一次完整的 `ApplySkinToPlayer`（189-191，代价见 AP-2）。配件消息里的条目标识没有长度限制，主机把它作为键存进 `_serverAccessoryTable[netId]`，这张表随任意标识增长，并且会在新玩家加入时整体重放。`offX`、`offY`、`zOrder` 没有夹取（旋转角和尾巴参数是夹取了的）。一个有问题的客户端可以让所有人反复重建角色外观，或让主机的表不断变大。建议：每个客户端每类消息用令牌桶限频（例如每秒 10 条），超出的丢弃；标识长度限制在 64 以内，每个 `netId` 的条目数限制在几十个；偏移和层级夹到合理范围。
- NW-3【中·性能】主机第一次收到某个皮肤包时，把整包转发给除发送者外的所有客户端（377-381），不管对方有没有请求、本地是否已经有这套皮肤、是否打开了实验开关。没开开关的客户端收到后直接丢弃，但流量已经花了：每套最大 2 MB，乘以人数。建议主机只广播「谁穿了哪套皮肤」（现有的换肤消息已经在做），客户端发现本地没有再发请求（请求消息也已经有了），主机只回给请求者。
- NW-4【低·口径】主机发现消息里的 `netId` 不属于发送者时（103、162、214），每条消息记一条 Warning，措辞是作弊尝试。角色重生时 `netId` 会变，路上的旧消息会被误判，这条日志会冤枉正常玩家。另外，消息处理出异常时每个包都带完整堆栈记一条 Warning（`Patches/TogetherReceiverRegistrar.cs:76, 114`）。两者都没有限频，远端可以用它们把日志刷满。建议按客户端和类别限频，措辞改成中性的「已拒绝」。
- NW-5【低·风险】`OnClientSkinMessage` 收到的消息如果 `netId` 是本机角色，会把消息里的皮肤名写进本地配置项 `CurrentSkin`（77-80）。这大概是为了让主机记住的皮肤在重新加入时恢复。副作用是主机发来的值能改写客户端的配置文件，和 README「不会改动你本地的 .cfg」的说法有出入（README 那句话说的是配件和尾巴参数）。皮肤名经过了 `IsSafeSkinName` 校验，没有安全问题。要确认的是顺序：主机缓存的旧值如果比本机上报的新值先到，本机刚选的皮肤会不会被改回去。已列入待验证清单。
- NW-6【低·性能】`OnServerSkinMessage` 每次都调用 `SkinCacheStore.Set`，整个缓存文件重写一遍（CF-4）。`OnServerSkinPackRequest` 转发请求有冷却时间（296-301），但主机自己有这套皮肤、直接回包的那条路径没有冷却，每次请求都从磁盘重新打包。建议打好的包按皮肤名缓存，并对同一个客户端的同一个请求加冷却。
- 主机对「发送者正穿着或有人请求过」的检查在整包重组完成之后（347-361），见 PK-5。
- 做得好的部分：身份核对；尾巴参数夹取和 NaN 处理（225-267，属于 TL-1 的重复之一）；皮肤包所有入口都检查实验开关。

**`Patches/SkinCacheBroadcaster.cs`（220）**

- NW-7【中·性能】主机上 `Update`（99-155）每帧取一次 `SceneManager.GetActiveScene().name`，并调用 `RosterConsumer.EnumeratePlayersWithBody()` 遍历所有玩家（每个玩家约 4 次反射调用，外加一个新列表），目的是发现还没收到缓存皮肤的新客户端。新玩家加入是低频事件，建议降到每 0.5 秒一次，或者接上玩家加入事件（见 IN-6）。没装 Together 时这个 `Update` 的第一行就是 IN-1 的调用点之一。
- NW-8【低·风险】`OnSceneLoaded` 在任何场景加载时清空主机侧的表；`PendingSkinApplier` 也在任何场景加载时清空待应用队列。两处都没有区分叠加加载和切换加载。加载过程中到达的换肤消息如果先进了队列、随后被清空，对应玩家的皮肤在新场景里会缺失，直到对方再次上报。已列入待验证清单。

**`Patches/PendingSkinApplier.cs`（115）**

- 0.25 秒轮询一次，30 秒超时，设计合理。清空队列的时机见 NW-8。

**`Patches/TogetherReceiverRegistrar.cs`（118）**

- 单机下的日志见 ST-4，异常日志见 NW-4。收到未知子消息编号时也是每条一个 Warning（69、107），同样建议限频。

**`Patches/LegacyTogetherSkinSuppressor.cs`（338）**

- PT-1【低·风险】这个类只在 Together 的程序集里存在 `SpriteReplacerPlugin` 类型时才动手，也就是 Steam 上旧的 5.5.x 版本自带的那套皮肤子系统。类型不存在就记下 `_noLegacyDetected`，什么都不做（41-46）。本工作区里的 Together 源码已经没有这个类型（已核对），所以对新版本它是空操作。检测到旧版本时，它用 `Harmony.UnpatchID("com.yourname.spritereplacer")` 卸掉对方的全部补丁，销毁对方的组件和对象，清空对方的静态字典，摘掉场景回调，隐藏对方的界面，并给对方 4 个名字的方法加上跳过原方法的前置补丁。成功后用一条 Info 写明还原了多少组件、销毁了多少监听器、短路了多少入口（62），最外层出异常时记一条 Warning（66）。两套皮肤系统同时工作会互相覆盖，停用其中一套是合理的，结果也写进了日志。剩下三点。第一，没有配置开关，还在用旧版 Together 的用户无法选择保留自带的那套。第二，内部各步骤有 9 处空的 catch，装短路补丁失败只用 `ModLog.Debug` 记录（332），这一行在 Release 构建里被编译掉；部分失败时只能从 Info 那行的数字偏小看出来。第三，最外层出异常时 `_suppressed` 保持 false 而函数返回 true，之后的调用点（`SkinSync.cs:151, 155, 163, 167`）会把整套流程再做一遍。建议：加一个默认打开的配置开关；步骤失败改用 Warning 记录；出过异常后不再重复执行。

**`Patches/TogetherHooksBridge.cs`（185）**

- 可选绑定 `OnBodyReadyOrRebuilt`、`OnWoundViewTargetApplied` 和 `RegisterExternalInputBlocker`。三个接口的签名和 Together 源码（`Api/TogetherPanelApi.cs:36, 39, 85`）一致（已核对）。没有发现问题。

**`Patches/UpdateChecker.cs`（138）**

- PT-2【低·口径】启动时访问 GitHub 的接口检查新版本，由 `AcceptUpdateNotice` 控制，默认打开。四点：README 没有提到模组会在启动时联网，建议写明；离线或访问失败时每次启动记一条 Warning，建议降为 Info；有新版本时，红色提示在所有场景（包括局内）一直画着，没有关闭按钮（122-135），建议只在主菜单显示，并提供「这个版本不再提示」；版本号的来源和 `SkinSync.Version` 不是同一个（ST-7）。

**`Patches/SaveManagerTabBridge.cs`（28）**

- PT-3【低·待验证】用 `AccessTools.TypeByName` 找存档管理器的页签注册类型。只在插件表里有存档管理器时才调用（`SkinSync.cs:119-120`），正常情况下类型一定存在。如果对方改了类型名，HarmonyX 可能会输出一条警告，然后本模组退回独立按钮模式，行为是安全的。

**`Patches/BleedSpawnTranspiler.cs`（81）、`Patches/BleedParticleColorPatch.cs`（63）**

- BL-4【低·风险】转译补丁在 `BleedParticle.Update` 里每个 `Object.Instantiate(对象, 位置, 旋转)` 调用后面插入三条指令。按当前反编译，这样的调用有 2 处（`BleedParticle.cs:54, 64`），匹配正常（已核对）。但匹配到 0 处时没有任何日志，游戏更新改了写法后，地面和墙面血迹的染色会静默失效。建议记录匹配数量，不等于预期时记 Warning。`ResolveCharacter` 在两个文件里各有一份（`BleedParticleColorPatch.cs:28-37`、`BleedSpawnTranspiler.cs:70-78`）。
- `BleedParticleColorPatch.cs` 里两个后置补丁（`Limb.Awake` 和 `FacialExpression.Start`）的异常只用 `ModLog.Debug` 记录（24、59）。这两行在 Release 构建里被编译掉，等于空的 catch，见 LG-1 下面的说明。

**`Patches/WoundViewPatches.cs`（65）**

- 三个后置补丁，分别挂在 `WoundView` 的 `Start`、`UpdateView` 和添加图片的方法上。`UpdateView` 在面板打开期间每帧执行，相关开销见 WV-1。

### `UI/`（8 个文件，2,133 行）

**`UI/PreRunGuardPatch.cs`（83）**

- UI-1【中·Bug】`SkinSyncPlayerCameraHandleInputGuard`（55-62）在面板打开时让 `PlayerCamera.HandleInput` 整个不执行。原版在这个方法里先把 `body.moveDir` 清零，再按当前按键重新赋值（反编译 `PlayerCamera.cs:1330-1331, 1375-1376`）。跳过以后，`moveDir` 停在打开面板前最后一帧的值：按着移动键按 F10，角色会沿那个方向一直走，直到面板关闭。只在独立模式下出现，嵌入存档管理器时不走本模组的 `UiBlocker`。修法是在返回 false 之前把 `PlayerCamera.main.body.moveDir` 清零。其他模组的同类补丁见 X-7。已列入待验证清单。
- 文件里另外三个补丁类：`AdaptiveButton.overlayActive` 的后置补丁（9-26）和 `Clicked` 的前置补丁（29-52），在面板打开或鼠标压在本模组的主菜单按钮上时拦住原版按钮；`HandleMouseOver` 和 `HandleDragging` 的前置补丁（66-82），在面板打开时跳过。没有发现问题。这四个类同时管主菜单和局内，文件名只提了 PreRun，建议改名。

**`UI/UiBlocker.cs`（72）**

- `Block`（17-36）找出场景里全部 `GraphicRaycaster`（包括未激活的），记下每个原来是否启用，然后全部禁用。`EnforceBlocked`（38-50）每帧把记录里又被启用的再禁用一次。`Unblock`（52-70）按记录还原。几个模组的面板同时打开时，这种「记录再还原」的做法会互相干扰，见 X-1。
- `EnforceBlocked` 只管打开那一刻记下来的对象，面板打开以后才创建的界面不会被拦。CuSaveManager 和 CuHotbar 的版本会定期重新扫描。影响很小。

**`UI/InGameOverlay.cs`（82）**

- 在暂停菜单右下角画入口按钮，320×84，距右边 24，距底边 124。和其他模组按钮的位置关系见 X-3。
- UI-4【低·性能】界面每次事件里的几处小开销，集中记在这里。本文件用反射读 `PauseHandler.paused`、`PauseHandler.main`、`pauseContainer` 和 `PlayerCamera.brightnessPanel`（14-58），每次界面事件 3 次反射读取；这几个成员都是公开的，可以直接引用。`SkinSync.OnGUI` 每次事件新建一个闭包（`SkinSync.cs:327`）。取皮肤列表每次事件拷贝一份（`SkinSync.cs:100`）。角色页每次事件把所有格子都画一遍，不看格子是否在可见区域内（`UI/SkinSyncWindowTabs.cs:266-302`）。

**`UI/MenuButtonInjector.cs`（188）**

- UI-8【低·过度设计】往主菜单放按钮有两条路径：`sceneLoaded` 回调（31-37）和 `PreRunScript.Start` 的后置补丁（180-186），靠按名字查重（56-59）保证只放一次。留补丁那一条就够了。克隆出来的按钮靠类型名里是否含有 `Localiz`、`LocaleText`、`Tooltip`、`TMPro.TextMesh` 来清理多余组件和替换文字（104-177）。游戏改了组件名后这些判断会静默失效，表现是按钮上残留原来的文字或提示。
- UI-7【低·口径】界面文字和日志的几个小问题，集中记在这里。主菜单按钮的文字只在放进去时设置一次（80），之后切换语言不会更新。「关于」页的测试人员一栏是占位符 `.....`（`UI/SkinSyncWindowTabs.cs:393`），而 README 第 91 行列出了三位联机测试人员。每次打开面板记两条 Info 日志（`UI/SkinSyncWindow.cs:68, 126`），属于调试信息，建议改成 `ModLog.Debug`。`SkinSyncWindow.WindowRect`（61）和 `IsCursorOver`（82）没有调用方。`NormalizeLanguageMode` 有两份（`UI/SkinSyncWindowTabs.cs:732`、`UI/SkinSyncI18n.cs:284`）。
- 按钮尺寸 280×96，锚在右上角，偏移 (-24, -240)。CuSaveManager、CuStats、CuHotbar 的主菜单按钮排成一行，位置是 (0, -20)、(-300, -20)、(300, -20)。本模组的按钮和它们不重叠，但不在同一行。

**`UI/SkinSyncI18n.cs`（366）**

- UI-3【中·性能】`T()`（347）每次调用都经过 `Current`（258）、`UseChinese`（260）、`NormalizeLanguageMode`（284，里面做 `Trim` 和 `ToLowerInvariant`）。语言设置是「自动」时，还要读一次游戏的语言名（273）、去掉富文本标签（316）、逐个关键词匹配（291-314）。面板一次界面事件要取几十到上百条文案，每帧至少两次事件。建议把判断结果缓存下来，在语言配置项变化时、或者每帧第一次调用时重新算一次。
- 两张文案表各 109 条，键完全一致，没有重复，全部被用到（已核对）。判断中文的规则里有一个 `WC` 前缀（300），没有注释说明它对应什么语言包，建议补一句。

**`UI/SkinSyncWindow.cs`（336）**

- UI-6【低·Bug】热键相关的三个小问题。第一，`SkinSync.Update` 里的热键（`SkinSync.cs:284-311`）在面板打开时和正在录入新热键时照常响应：录入时按下的键如果正好是某个已经绑定的热键，对应的动作会同时触发。建议面板打开时只响应「开关面板」这一个，录入期间全部不响应。第二，录入只接受单个按键（316-333），而配置项的类型是支持组合键的 `KeyboardShortcut`；在面板里重新录入，会把配置文件里手写的组合键换成单键。第三，「重新扫描」热键排在两个提前返回的后面（`SkinSync.cs:290-293`）：皮肤列表为空，或者找不到本机角色时，按了没有反应，而列表为空正是最需要重新扫描的时候。把这个热键的判断挪到提前返回之前即可。面板里的「重新扫描」按钮不受影响。
- 窗口固定 1440×900，按屏幕缩放。日志和没有调用方的成员见 UI-7。

**`UI/SkinSyncWindowTabs.cs`（739）**

- UI-2【中·性能】「当前角色设置」页每次界面事件都调用 `_getAccessories()`（461），它走到 `SkinSync.GetCurrentAccessoryRows`（`SkinSync.cs:401-422`），再到 `LoadAccessoryEntriesForSkin`（`SkinSync.cs:389-399`）：一次 `File.Exists`、一次读文件、一次 JSON 解析，再新建一个行列表。页面开着时每帧至少两次。建议把行数据缓存在窗口里，换皮肤、改配件、重新扫描时作废。
- UI-5【低·口径】设置页的开关和配置项之间的接线不一致（98-134，对照 `SkinSync.cs:69-70, 84-95`）。「尾巴形变」和「自定义血液」两项既订阅了配置变化事件，界面里又直接写了一遍运行时状态；「更新提示」订阅了配置变化事件；「隐藏游戏装备」和「配件需要装备」两项只在启动时读一次、在界面里直接写，没有订阅。结果是用别的工具（例如配置管理器插件）改后两项时不会立即生效，前几项会。建议统一成只订阅配置变化事件，界面只改配置项。
- 角色页的预览图在界面事件里同步生成（340），见 PV-1。格子不做可见性裁剪，见 UI-4。
- 尾巴的 17 个参数（1 个开关、16 个滑块）逐个手写（499-536），是 TL-1 的一部分；松开鼠标时才保存和广播（552），这一点做得对。配件微调用步进按钮（594-611），点一次走一遍 `SkinSync.SetAccessoryTransform`（`SkinSync.cs:434-442`）：写配置、更新挂件、作废预览图、广播一条配件消息；广播前还会再读一次 `accessories.json`（`SkinSync.cs:450`）。
- 设置页每次界面事件读一次 `SkinSync.IsMultiplayerSession`（141），没装 Together 时这是 IN-1 的又一个调用点。

**`UI/BlackWhiteSkin.cs`（267）**

- 黑白风格的界面皮肤。CuHotbar、CuSaveManager、CuStats、hwAssistive 里各有一份同名文件，五份互不相同，见 X-4。没有发现本文件特有的问题。

### `Utils/`（22 个文件，6,128 行）

按功能分成九组。

#### 皮肤应用

**`Utils/SkinApplier.cs`（744）**

- AP-2【中·性能】同一套皮肤再次应用时走 270-295 这条路径：重新读 `accessories.json`，调用 `AccessoryAttacher.Apply` 销毁并重建全部配件，对血液做一次 `Apply` 或 `Restore`，调用 `WoundViewAttacher.RefreshForBody`，最后触发 `_skinApplied` 事件。区块特效只在换了皮肤时重做（283-286）。远端玩家每改一次配件，接收方就走一遍这条路径（`Patches/SkinMessageHandler.cs:189-191`）。本机改配件走的是 `AccessoryAttacher.SetTransform` 这条轻量路径，但它只认本机的登记表（AC-3）。建议让远端的配件消息也只改对应的那一个配件，并按皮肤缓存 `accessories.json` 的解析结果。
- AP-3【中·性能】`RefreshSidedSprites`（471-489）在角色每次转身时由 `LimbDirWatcher` 触发：三次 `GetComponentsInChildren`，再把肢体、尾巴、表情的贴图逐个重新解析一遍，最后刷新配件（488）。没有分朝向贴图的皮肤也照做一遍。建议加载皮肤时算好这套皮肤有没有分朝向的贴图，没有就直接返回；有的话把左右两套贴图成对缓存。
- AP-5【低·性能】`LoadCharacterSprites`（534-557）的几个小问题。读盘和解码在主线程同步完成。贴图保持可读（见第四节第 6 条）。字典的键只用文件名、不含子目录，重名时先遇到的生效（554），不提示。目录里一张图都没有时结果不进缓存（210-217），每次应用都会重新扫描目录。目录不存在时每次记一条 Info 日志（541-544）。
- AP-7【低·过度设计】`TryGetCharacterByPosition`（72-87）没有调用方。容器对象的名字写了多份：`HwWings` 以字面量出现在 `SkinApplier.cs:143, 426` 和 `SkinApplierParts.cs:380, 389`；`HwAccessories` 和 `HwZones_` 在各自的文件里有私有常量（`AccessoryAttacher.cs:11`、`ZonesAttacher.cs:11`），到了 `SkinApplier.cs:427-429` 又以字面量写了一遍。建议集中成一组内部常量。
- AP-8【低·风险】`_skinApplied?.Invoke`（295、416）直接调用全部订阅者。一个订阅者抛异常，后面的订阅者收不到通知，异常还会传回调用应用皮肤的地方。建议逐个调用并各自 catch。
- `ApplyToScene`（517-529）是 ST-5 里说的那条路径。贴图在 570 行用 `LoadImage` 解码，宽高的限制见 PK-1。

**`Utils/SkinApplierParts.cs`（451）**

- AP-1【中·过度设计】`GlobalFitScale`（148-152）恒返回 1。`FitToTarget`（104-133）在 `NormalizeSizeEnabled` 关闭（107）或缩放约等于 1（108）时直接返回传入的贴图，所以后面的代码现在永远走不到。不起作用的部分有：`ResampleRegion`（179-224），`_fittedSprites` 和 `ClearFittedSprites`（135-146），各函数之间传递的 `scale` 参数，没有调用方的 `ComputeFitScaleByHeight`（155-177），没有任何引用的 `Character/PartContentSizes.cs`（126 行），以及配置项 `NormalizeSpriteSize`（`Config/SkinSyncSettings.cs:109`，默认开启，改了没有效果）。README 和已有文档都写了这是预留功能。建议删除或移到分支上；至少不要让这个配置项出现在配置文件里。
- AP-4【低·风险】`_originalLimbSprites`、`_originalTailSprites`、`_originalFaces`（10-12）以对象的 InstanceID 为键，从不清理。`BloodAttacher` 的四张表（`Utils/BloodAttacher.cs:30-36`）也是这样。`WoundViewAttacher._origSrSprites` 只在切换场景时清理。长时间联机、角色反复重建时，这些表只增不减。每条记录很小，实际影响有限。建议改成以对象引用为键，定期去掉已经销毁的对象。
- AP-6【低·风险】`GetUnderlyingEyeList`（304-333）按类型名 `FacialExpressionPause` 和字段名 `originalEyeList`、`pausedEyeList`、`lastAlive` 用反射读 Together 的组件。对照 Together 源码，这些名字目前都对得上（已核对）。Together 改名以后这里会静默失效，没有日志。建议第一次找不到时记一条 Warning。
- `DisableConflictingReplacers`（342-361）每次应用皮肤都用 `GetComponentsInChildren<MonoBehaviour>(true)` 找名叫 `SkinSpriteReplacer` 的组件。它和 PT-1 做的是同一件事。PT-1 已经记下了当前的 Together 有没有旧皮肤系统（`_noLegacyDetected`），没有的时候这里可以直接跳过。
- 眼睛数据 `Eye` 是结构体，代码按「取出、修改、写回」处理，写法是对的（已核对）。

**`Utils/LimbDirWatcher.cs`（44）**

- 每帧比较一次 `Body.isRight`（25-42），变化时调用 `SkinApplier.RefreshSidedSprites`（41）。组件本身开销很小，开销在被调用的函数里，见 AP-3。

**`Utils/SkinPathResolver.cs`（133）**

- PK-6【低·性能】`HasLocalCustomSkin`（82-87）不缓存结果：一次 `Directory.Exists`，再递归枚举文件，直到遇到第一个 png 或 json。它有 5 个调用点，都在收发皮肤包的路径上（`Patches/SkinMessageHandler.cs:364, 425`、`Utils/SkinPackChunkAssembler.cs:90`、`Utils/SkinPackCodec.cs:62, 233`），调用频率不高。建议按皮肤名缓存，重新扫描时作废。

#### 皮肤包

**`Utils/SkinPackCodec.cs`（339）**

- 常量在 13-17 行：单条消息上限 48 KB，分片负载 32 KB，整包上限 2 MB，单文件上限 512 KB，文件数上限 256。解包时的校验（209-274）很严格，见第七节。
- PK-1【中·风险】解包只检查 PNG 的 8 字节签名（256-262）和文件大小，不看图片的宽高。PNG 的压缩比可以很高，文件很小的图解码后可以是一张非常大的贴图。解码发生在 5 处 `LoadImage`（`Utils/SkinApplier.cs:570`、`Utils/BloodAttacher.cs:387`、`Utils/WoundViewAttacher.cs:558`、`Utils/SkinPreviewRenderer.cs:214`、`Config/ZonesConfig.cs:102`）。只有打开实验开关 `EnableSkinPackSync` 时远端数据才会到这里。建议校验时读 PNG 文件头里的宽和高（文件偏移 16 到 23，两个大端 32 位整数），超过上限就拒收整个包。本地皮肤也可以用同一个上限，超限时记日志并跳过。已列入待验证清单。
- PK-2【低·Bug】皮肤只按名字识别，没有内容哈希或版本。`PersistToDisk`（276-299）写文件前不清空目标目录。同一个名字先后收到两个内容不同的包时，后一个包里没有的文件会留在缓存目录里，两套文件混在一起。另外，写进 `CustomSprites` 的皮肤之后会被当成本地皮肤，同名的新包会被拒收，对方更新了皮肤也收不到。建议写盘前清空该皮肤的缓存目录，并在包头里带一个内容哈希用来判断是否需要更新。
- PK-3【低·性能】`_cache`（19-20）只在 199 行写入，从不删除；同样的内容在磁盘上还有一份。收到的皮肤包越多，占的内存越多，每个最多 2 MB。建议限制条目数，或者在离开房间时清空。
- PK-4【低·口径】打包时 `TryCollectFiles`（151-160）遇到路径不合规、超过单文件上限或读不出来的文件会直接跳过，不记日志。发出去的包是不完整的，发送方不知道。建议跳过时记一条 Warning，写明文件名和原因。

**`Utils/SkinPackChunkAssembler.cs`（230）**

- PK-5【低·风险】主机对「发送者正穿着或有人请求过这个皮肤」的检查放在整包重组完成以后（`Patches/SkinMessageHandler.cs:347-361`），而皮肤名在第一片就能读到（85-88）。在这之前，每个发送者最多可以让主机同时缓冲 16 个传输、每个 2 MB，合计 32 MB，30 秒后超时释放（11-12）。客户端一侧在第一片就会拒绝本地已有的皮肤（90-93）。建议主机也在第一片就做这个检查。
- 分片的各项检查（52-77）和包头前后不一致时的处理（110-118）没有发现问题。

#### 配件与装备

**`Utils/EquipmentHider.cs`（71）**

- AC-1【中·Bug】`ApplyHide`（26-54）把肢体下面的装备贴图隐藏并记进 `_hidden`；`RestoreAll`（56-64）只在关掉选项时把它们全部恢复。原版 `Body.WearWearable` 把装备挂到肢体下面（反编译 `Body.cs:1933-1982`），`Body.DropWearable` 只是把它从肢体上摘下来并恢复物理（`Body.cs:1985-1998`），不会恢复显示。所以开着「隐藏游戏装备」时脱下或丢掉的装备会一直看不见，直到把选项关掉。修法：每次轮询时检查 `_hidden` 里的贴图是否还在某个肢体下面，不在的恢复显示并移出集合。已列入待验证清单。
- AC-2【低·性能】三个 0.25 秒一次的轮询。本文件每次 `FindObjectsOfType<Body>()`（28），再遍历每个肢体的子对象，每个子对象最多 3 次 `GetComponent`。`AccessoryEnforcer` 不管当前皮肤有没有带装备条件的配件都在跑。`BloodVomitWatcher` 每次对躯干做 `GetComponentsInChildren<ParticleSystem>(true)`。`_hidden` 和 `BloodVomitWatcher._processed` 在各自生效期间只增不减。建议在 `Body.WearWearable` 和 `Body.DropWearable` 后面挂后置补丁，替掉前两个轮询。

**`Utils/AccessoryAttacher.cs`（406）**

- AC-3【低·过度设计】配件有两套登记：本机角色用的 `Mounted`（13）和 `Meta`（33），以及按角色登记的 `_byBody`（35）。`SetEnabled` 和 `SetTransform`（348-371）只认前一套，所以轻量更新只对本机有效，远端角色只能整套重建（AP-2）。建议合并成按角色登记的一套。
- `Apply`（111-230）每次都销毁并重建全部配件。配置里写的肢体或贴图找不到时，每次应用都记 Warning（141、147、201、207），远端玩家频繁改配件时会刷屏，建议每个皮肤只提示一次。`ClearExisting`（396-404）按名字前缀清理。

**`Utils/AccessoryEnforcer.cs`（21）**

- 每 0.25 秒调用一次 `AccessoryAttacher.EnforceWearableSlots`（14-18），见 AC-2。

**`Utils/ShadowAttacher.cs`（19）、`Utils/WingScript.cs`（84）**

- AC-4【低·性能】每个配件都加一个 `ShadowCaster2D`（`Utils/AccessoryAttacher.cs:266`）。一个角色有十几个配件、房间里又有多个角色时，投影体的数量会明显增加，对帧率的影响需要实测，已列入待验证清单。`WingScript.Update`（46-74）每帧写一次旋转和 `sortingOrder`，值没有变化时也写，可以先比较再写。

#### 血液

**`Utils/BloodAttacher.cs`（773）**

- BL-1【低·风险】依赖几个会随游戏更新变化的名字：着色器属性 `_SampleTexture2D_722a3646334c45d58299ef3dd9fd21be_Texture_1_Texture2D`（14，看起来是 Shader Graph 自动生成的名字）、`_BloodDark`、`_BloodLight`，以及 `Limb` 的私有字段 `bleedPart`、`waterBleedPart`、`mat`（17-21；对照反编译 `Limb.cs:108, 110, 88`，目前都存在）。找不到时静默跳过，自定义血液不生效也没有日志。建议启动时检查一次，缺了记一条 Warning。
- BL-2【低·性能】关掉自定义血液时，每次应用皮肤都执行一遍 `Restore`（99-122；调用点 `Utils/SkinApplier.cs:291`），不管之前有没有改过。建议只在确实改过的角色上执行。
- BL-3【低·风险】`blood.json` 里的数值没有夹取：粒子大小、寿命、序列帧参数、发光强度。这个文件在皮肤包的白名单里（`Utils/SkinPackCodec.cs:103, 334`），打开实验开关后可以来自远端。建议在加载器里统一夹取，和 ZN-1 一起做。
- `ClearCaches`（125-199）清缓存时会避开还被场上粒子引用的材质，做得对。`_noBloodSkins` 记下没有血液配置的皮肤，避免重复读盘。`RecolorLimbBlood`（501-535）先把原贴图拷到临时渲染目标再读像素，不要求原贴图可读，结果按「原贴图加两个颜色」缓存（480-484）。贴图在 387 行用 `LoadImage` 解码，宽高的限制见 PK-1。

**`Utils/BloodVomitWatcher.cs`（49）**

- 每 0.25 秒在躯干下面找名字同时包含 `vomit` 和 `blood` 的粒子系统并染色（24-46），见 AC-2。名字不匹配的粒子系统不会记进 `_processed`，每次轮询都要重新比较一遍名字。

**`Utils/OneShotBloodTint.cs`（27）**

- 在 `LateUpdate` 里给墙面血迹着色一次，然后销毁自己（18-24）。原版在生成墙面血迹之后紧接着把颜色设成白色加随机透明度（反编译 `BleedParticle.cs:67`），所以着色要推迟到 `LateUpdate`，并且保留透明度。这个做法有依据（已核对）。

#### 区块特效

**`Utils/ZonesAttacher.cs`（369）**

- ZN-1【中·风险】区块配置里的数值基本没有夹取：灯光的强度和内外半径（97-99），粒子的寿命、大小、速度、发射速率（125-134）。只有锥角做了夹取（145）。区块数量只受遮罩编号（每张贴图最多 255 个）和文件大小限制。`.zones.json` 和遮罩图都在皮肤包的白名单里，打开实验开关后可以来自远端：一个发射速率极高的粒子区块，或者一个半径极大的灯光，会影响房间里所有人的帧率和画面。建议在加载器里统一夹取，限制每个角色的区块总数，并加一个「只对自己的皮肤启用灯光和粒子」的开关。本地皮肤走同一套夹取。
- ZN-2【中·性能】`AttachForRenderer`（35-55）对每个肢体调用两次 `FindZonesFile`（57-69），每次都是一次递归的 `Directory.GetFiles`；找到以后 `ZonesConfigLoader.Load` 不缓存，每次都重新解码遮罩图并逐像素处理（`Config/ZonesConfig.cs:102-106`）。换一次皮肤的递归搜索次数是肢体数的两倍。建议每个皮肤建一次文件名索引，并按路径缓存解析结果。
- 同一个文件里还有 `ZoneFrameAnimator`（281）和 `ZoneLightAnimator`（340）两个组件，没有单独要记的问题。

#### 尾巴

**`Utils/TailFlowDeform.cs`（475）**

- TL-1【中·过度设计】尾巴的 17 个参数在十多个位置各手写一遍，默认值写了 3 份。完整清单见第三节第 1 条，建议见第四节第 10 条。本文件是这些参数的使用方。
- TL-2【低·性能】`LateUpdate`（306-459）每帧 `_mesh.Clear()`，然后重新赋值顶点、UV 和三角形（454-458）。贴图不变时 UV 和三角形是固定的，只需要更新顶点。每次重建轮廓都调用一次 `GetPixels32`（142），会分配整张贴图大小的数组。
- TL-3【低·口径】模拟结果和帧率有关。子步数是 `ceil(dt / max(0.005, MaxFixedDt))`（361-362）：帧间隔小于 `MaxFixedDt` 时每帧一个子步，每秒的子步数就等于帧率。阻尼是每个子步乘一次固定系数（368），摆动位移每个子步加一次、不乘时间（373-375），另外几处用的是固定系数的 `Lerp`（352-353、359、432）。结果是同一套参数在不同帧率下手感不同。另一头，卡顿时子步数会很多：帧间隔 0.33 秒、`MaxFixedDt` 取下限 0.005 时是 67 个子步。建议给子步数加上限（例如 8），固定系数的插值改成按时间的指数衰减，摆动位移乘上子步时长。已列入待验证清单。
- TL-4【低·口径】网格的材质在创建时从肢体克隆，并把 8 个状态参数清零（61-78：血迹、皮肤损伤、肌肉损伤、感染、积雪、污渍、潮湿、疼痛）。开启尾巴形变后，尾巴不会跟身体其他部位一起显示这些状态。如果这是有意的取舍，建议在注释或 README 里说明。

#### 伤势面板

**`Utils/WoundViewAttacher.cs`（631）**

- WV-1【中·性能】`OnUpdateView`（168-187）和 `ApplyCustomMuscleColors`（189-209）在伤势面板打开期间每帧执行，因为原版每帧调用 `UpdateView`（反编译 `WoundView.cs:847-853`）。每帧重新解析当前皮肤，并逐个肢体取组件。建议每帧只比较目标对象和物种有没有变，皮肤变化走已有的 `RefreshForBody`，图片引用缓存下来。
- WV-2【低·性能】`Resources.FindObjectsOfTypeAll<WoundView>()` 出现 4 次（99、110、134、599）。其中 `RefreshForBody` 在每次应用皮肤时被调用（`Utils/SkinApplier.cs:294, 415`），远端玩家换皮肤、改配件也会触发。建议在 `WoundView` 的补丁里登记实例，之后直接用登记表。
- WV-3【低·性能】`LoadFromDisk`（448-467）做两次递归的 `Directory.GetFiles`，贴图单独解码一份（558），每张贴图调用一次 `GetPixels32`（585）。可以和第四节第 4 条的文件索引一起处理。
- `_emptySkins`（62-63）记下没有伤势贴图的皮肤，避免重复读盘。`_trackedImages` 的清理阈值是 256（67-70）。`Invalidate`（119-129）先把面板还原成原版再销毁贴图，顺序是对的。

#### 预览图

**`Utils/SkinPreviewRenderer.cs`（652）**

- PV-1【中·性能】`GetOrBuild`（98-127）在界面事件里被同步调用（`UI/SkinSyncWindowTabs.cs:340`），没有缓存时当场执行 `RenderRgba`（322-486）：按默认的 20 个部位逐个读盘、解码、合成。角色页不做可见性裁剪（UI-4），所以第一次打开角色页时，以及每次重新扫描之后（`SkinSync.cs:211` 作废全部预览图），所有皮肤的预览图在一次界面事件里全部生成。皮肤目录不存在时返回空并且不记缓存（324），每次事件都会重试。建议每帧最多生成一张，没生成的先画占位；只生成可见格子；失败的结果也记进缓存。已列入待验证清单。
- PV-3【低·性能】几个小问题。每次换皮肤都作废一次预览图（`SkinSync.cs:617`），贴图并没有变，不需要；改配件时的作废（`SkinSync.cs:430, 440`）是需要的。上臂、前臂、大腿、小腿、脚这五类贴图各被解码两次。`Entry.HeadDefaultOnly` 和 `EyeOpenOnly`（26-27）从来没有被读取。`AccessoryParentByLimb`（80）用数字下标表示肢体，没有注释说明对应关系。
- 贴图在 214 行用 `LoadImage` 解码，宽高的限制见 PK-1。

**`Utils/SkinPreviewZones.cs`（320）**

- PV-2【低·过度设计】用 320 行在 CPU 上重新实现了区块的灯光、粒子和序列帧，往 100×100 的缩略图上叠效果。时间参数在两个调用点都固定传 0（`Utils/SkinPreviewRenderer.cs:483, 649`），和时间有关的分支只算了一个时刻。详见第三节第 3 条。建议预览图只画贴图本身，或者只保留灯光的静态着色。

#### 日志

**`Utils/ModLog.cs`（71）**

- LG-1【低·性能】`WriteToConsole`（47-59）用反射调用游戏的 `ConsoleScript.LogToConsole`（私有方法，反编译 `ConsoleScript.cs` 第 228 行），每行日志一次 `Invoke` 加一个 `object[]`。只在 `ModLog.ShowInConsole` 打开时发生，影响很小。可以换成缓存好的委托。
- `ModLog.Debug`、`CommsTrace`、`DebugMessageTrace` 的方法都带 `[Conditional("DEBUG")]`（分别 1 个、7 个、4 个），Release 构建里调用和参数求值都被去掉，这是好的。副作用是：只用 `ModLog.Debug` 记录的失败，在发布版里完全看不到。目前有三处：`Patches/BleedParticleColorPatch.cs:24, 59` 和 `Patches/LegacyTogetherSkinSuppressor.cs:332`。建议这几处改成 Warning，并且每种失败只记一次。

**`Utils/CommsTrace.cs`（79）、`Utils/DebugMessageTrace.cs`（140）**

- 联机消息的跟踪日志，只在 Debug 构建里存在。`DebugMessageTrace` 按 0.5 秒的窗口合并输出。没有发现问题。

## 九、跨模组事项

这一节的编号和 hwAssistive 审计文档第十节保持一致。X-1 到 X-4 是同一件事在本模组这一侧的情况，X-5 到 X-7 是这次新增的。

- X-1【中·风险】**界面点击拦截互相干扰。** 本模组的 `UI/UiBlocker.cs`（72 行）在面板打开时禁用全部 `GraphicRaycaster` 并记下原状态，关闭时按记录还原。CuSaveManager（`src/UI/UiBlocker.cs`，125 行）和 CuHotbar（`src/UI/UiBlocker.cs`，185 行）各有一份，hwAssistive 把同样的逻辑写在 `src/Plugin.cs:277-302`。两个模组的面板同时开着、并且先开的先关时，后关的那个会按自己记下的「原来是关」把全部 `GraphicRaycaster` 留在禁用状态，游戏自带界面的点击随之失效。本模组只在独立模式下会参与进来，嵌入存档管理器时不调用自己的 `UiBlocker`。建议和 hwAssistive 文档里写的一致：几个模组统一换成不需要还原状态的拦截方式，例如在最上层放一块全屏透明、能接收点击的画布，关面板时销毁。此条是读代码推断的，已列入待验证清单。
- X-2【低·过度设计】**Interop 代码在这次审计的模组里有四份。** CuSaveManager、CuStats、hwAssistive、SkinSync 各带一份，CuHotbar 没有。hwAssistive 的文档写的是三份，那时还没有审到本模组。仓库里还有第五份在 `mods/CUCoreLib/Interop`（它的 `TogetherProvider` 是 1,060 行，里面同样有 `FindTargetAssembly`，370、512 行），以及一个 `mods/_interop_template` 目录。这两处不在这次审计的范围内，我只确认了它们存在，没有审。`InteropContract`（856 行）和 `InteropReflection`（421 行）四份的代码行相同。`InteropRegistry` 和 `InteropDiagnostics`，本模组的版本和 CuStats、CuSaveManager 的代码行相同、只差注释；hwAssistive 的 `InteropRegistry` 改过。`InteropBootstrap` 四份长度各不相同（124、112、81、61 行，本模组最短）。`TogetherProvider` 四份各不相同（CuSaveManager 1,249 行，CuStats 973 行，hwAssistive 761 行，本模组 719 行）。IN-1 的写法（找不到程序集时不缓存，每次调用重新扫描）在四份 `TogetherProvider` 里都有，修的时候要逐个模组改。仓库里有一个比对脚本 `tools/verify-interop.ps1`，它只比对 `InteropContract` 和 `InteropReflection` 这两个文件。我没有运行它，上面的比对是我另外用命令做的。建议抽成一个共用的程序集，或者用同一份源文件链接进各个工程。`TogetherProvider` 不在脚本的比对范围内，修 IN-1 时要逐份检查，包括 CUCoreLib 那一份。
- X-3【低·风险】**暂停菜单按钮的位置靠约定。** 本模组的入口按钮写死在右下角：320×84，距右 24，距底 124（`UI/InGameOverlay.cs:9-12`）。CuSaveManager 的按钮尺寸相同，距底 24（`src/UI/OverlayUI.cs:9-12`），本模组正好排在它上面一格。CuHotbar 的按钮是 240×64，距右 32，距底 32，和本模组的按钮不重叠。hwAssistive 为了推算自己排第几格，用反射读了本模组的私有字段 `_embeddedInSaveManager`（`hwAssistive/src/PauseMenuButtonOverlay.cs:17, 97`）：本模组给这个字段改名，hwAssistive 的排位就会静默出错。嵌入存档管理器时本模组不画这个按钮（`SkinSync.cs:316, 326`）。建议同 hwAssistive 文档：存档管理器在场时一律只用页签；不在场时由一个共用的计数分配格子。在那之前，这个私有字段的名字不要改，或者改为提供一个公开的只读属性。
- X-4【低·过度设计】**界面辅助代码的拷贝。** `BlackWhiteSkin` 五个模组各一份，长度各不相同（CuHotbar 524 行，CuSaveManager 311 行，CuStats 295 行，hwAssistive 284 行，本模组 267 行）。`SaveManagerTabBridge` 四份（CuHotbar 109 行，hwAssistive 37 行，CuStats 35 行，本模组 28 行）。`UiBlocker` 三份（见 X-1）。建议和 X-2 一起处理，放进同一个共用程序集。
- X-5【低·口径】**F6 冲突。** 本模组「下一个皮肤」的默认热键是 F6（`Config/SkinSyncSettings.cs:84-86`），hwAssistive 把 F6 写死为开关面板（`hwAssistive/src/Plugin.cs:467`，在它的文档里是 PL-2）。两个都装时，按一次 F6 会同时打开 hwAssistive 的面板并切换皮肤。本模组的热键可以在配置里改，hwAssistive 的不能。建议 hwAssistive 改成配置项；本模组也可以换一个默认键，或者默认不绑定。
- X-6【低·风险】**四个模组都改了 BepInEx 共用管理对象的标志。** `gameObject.hideFlags = HideFlags.HideAndDontSave` 加 `DontDestroyOnLoad(gameObject)` 这两行出现在本模组（`SkinSync.cs:59-60`）、CuSaveManager（`src/Plugin.cs:49-50`）、CuHotbar（`src/Plugin.cs:42-43`）和 CuExtraction（`src/Plugin.cs:28-29`）。BepInEx 5 默认把所有插件挂在同一个对象上，所以改的是大家共用的对象。这是常见的防销毁写法，实际影响很小，和 CuHotbar、CuSaveManager 两份审计文档里的结论一致。更干净的做法是各模组自己新建一个 `GameObject` 挂自己的组件。要改的话四处一起改。本模组这一处见 ST-1。
- X-7【低·Bug】**四个模组的输入拦截都没有把移动方向清零。** 面板打开时跳过 `PlayerCamera.HandleInput` 的前置补丁有四处：本模组 `UI/PreRunGuardPatch.cs:55-62`（UI-1），hwAssistive `src/HarmonyPatches.cs:589-594`（它的文档里已经记录），CuSaveManager `src/Patches/PreRunGuardPatch.cs:73-90`，CuHotbar `src/UI/UiBlocker.cs:167-183`。后两处会更新视角目标点，但同样没有清零 `body.moveDir`。这两个模组的面板热键默认不绑定（`CuSaveManager/src/Config/HotkeyConfig.cs:40-44`、`CuHotbar/src/Config/HotbarConfig.cs:66`），用户自己绑定以后才能用热键在移动中打开面板。这两处我没有进游戏确认，也没有写进那两个模组的审计文档，在这里补记。修法四处相同：返回 false 之前把 `PlayerCamera.main.body.moveDir` 清零。
