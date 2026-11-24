# SkinSync

《Casualties: Unknown》（未知伤亡）的自定义皮肤与联机同步模组，当前版本 `1.2.7`（兼容 `Casualties: Together` 5.5.x 各分支，面板与存档管理器等模组统一为半透明黑白粗框风格）。

最初参考 Nexus Mods 上的 `SpriteReplacer` 起步，目前支持三个可玩角色（Experiment / Milky / Dune）的肢体与表情替换、背部翅膀、肢体配件、自定义血液、区块灯光粒子与序列帧动画，外加一套基于 Verlet 积分的尾巴物理摆动。单机装上就能用；如果装了 `Casualties: Together`（`CasualtiesMP` 5.5.x），房间里同样装了本模组的玩家会自动看到彼此的皮肤、配件开关和尾巴摆动参数。

## 安装

先给游戏装好 BepInEx 5，再把发布页的压缩包直接解压到游戏根目录（内含 `BepInEx/plugins/SkinSync/SkinSyncMod.dll`），或者单独把 `SkinSyncMod.dll` 放进 `BepInEx/plugins/`（直接放根目录，或者建个 `BepInEx/plugins/SkinSync/` 子目录放进去都行）。皮肤包统一放在 `BepInEx/plugins/CustomSprites/` 下，一套皮肤一个文件夹：

```text
Casualties Unknown/
└── BepInEx/
    └── plugins/
        ├── SkinSyncMod.dll
        └── CustomSprites/
            ├── MySkinA/
            │   ├── Body/
            │   ├── Head/
            │   ├── Wings/
            │   ├── Accessories/
            │   ├── Blood/
            │   ├── wings.json
            │   ├── accessories.json
            │   ├── blood.json
            │   └── baseSizes.json
            └── MySkinB/
                ├── Body/
                └── Head/
```

平时玩家直接解压压缩包时，经常多套一层同名目录（变成 `CustomSprites/MySkinA/MySkinA/Body/`），模组会自动往里剥一层，不用手动搬文件。目录名如果用 `_` 或 `.` 开头会被跳过，想临时关掉某套皮肤改个前缀就行。

## 界面与按键

面板采用半透明纯黑白粗框界面，皮肤卡片网格会按窗口宽度自动排成 1 至 3 列，尾巴参数 "重置默认" 带有 4 秒二次确认以防误触。如果同时装了 `CuSaveManager`，SkinSync 会直接挂进它的侧边栏页签，跟它共用一套窗口开关和鼠标拦截，省得两个模组各自锁界面把游戏按钮搞失灵。

没装 `CuSaveManager` 时，主菜单右上角和局内按 `Esc` 暂停后的右下角都会多出一个 "皮肤同步" 按钮，也可以直接按快捷键。下面四个按键都能在面板 "设置" 页重新绑定或清除：

| 默认按键 | 作用 |
| --- | --- |
| `F10` | 打开或关闭面板（未嵌入 `CuSaveManager` 时生效） |
| `F6` | 切到下一个皮肤 |
| `F7` | 切到上一个皮肤 |
| `F8` | 重新扫描 `CustomSprites`，改了贴图或 JSON 后按一下就能热刷 |

启动游戏时如果开着更新提示（默认开启），模组会访问一次 GitHub Releases 检查有没有新版本；发现更新后只在主菜单左上侧给出跳转提示，点旁边的 `[×]` 可以关掉当次提示，不想联网检查也可以去面板 "设置" 页把更新提示关掉。

## 皮肤包里能放什么

自己画皮肤推荐直接用配套的桌面工具 `CasualtiesUnknown-SkinEditor`，导出时目录和 JSON 都会自动配好。如果想手改文件夹，各个文件的作用如下：

| 目录或文件 | 用途 |
| --- | --- |
| `Body/`、`Head/` | 放躯干四肢、尾巴和脸部表情 `.png`。实际加载是递归扫整个皮肤目录并按无后缀文件名匹配（不区分大小写），分目录主要是方便管理。 |
| `Wings/`、`wings.json` | 放 `wingUL`、`wingDL`、`wingUR`、`wingDR` 四片翅膀图，`wings.json` 调偏移、角度和层级；没放图就不挂翅膀。 |
| `WoundView/` | 放健康检查面板（`H` 键）的头像轮廓与肌肉伤势贴图（如 `headoutline`、`headMiddle`、`thoraxOutline` 等），自动按原版框体比例缩放并保留彩色肌肉立绘不被底色染黑；联机查看队友伤势时自动显示对应队友的皮肤。 |
| `Accessories/`、`accessories.json` | 往肢体上挂衣服或饰品，支持 `secondaryLimbs` 把一件衣服拆到大小臂等多个关节跟着动。 |
| `Blood/`、`blood.json` | 改肢体血迹、喷血粒子、流鼻血和吐血的配色、贴图、发光或序列帧。默认关闭以省帧数，需要时去面板 "设置 -> 视觉" 里打开。 |
| `*.zones.png`、`*.zones.json` | 跟部件贴图同名的遮罩图和特效配置，用来在身体指定像素区域挂 URP 2D 灯光（含呼吸闪烁）、粒子发射器或序列帧。 |
| `baseSizes.json` | 高清画布的基准尺寸表，用来换算贴图像素密度。 |

配件系统现在在正式版默认开放。在面板的 "当前角色设置" 页可以单独开关每个配件，也能直接调整 X/Y 像素偏移、旋转角和前后层级。如果配件在 `accessories.json` 里填了 `requireWornSlot`，打开槽位联动后只有穿上对应部位的游戏装备才会显示这件配件；觉得原版装备贴图挡住了皮肤，打开 "隐藏游戏装备 sprite" 就能只藏贴图、保留护甲和保暖属性。

画高清皮肤时要注意尺寸换算。游戏原版贴图一律按 `pixelsPerUnit = 8` 算世界大小，如果你把画布放大两倍去画，直接进游戏人也会大两倍。在 `baseSizes.json` 里写上对应部件的原基准像素宽高，加载时就会自动按比例放大像素密度，把体型压回正常大小。代码里预留的自动缩放逻辑没有在 `.cfg` 配置中暴露（全局缩放固定为 `1.0`），控制高清贴图尺寸统一以 `baseSizes.json` 为准。

## 多角色贴图命名

游戏里有 Experiment、Milky、Dune 三个角色，骨骼节点相同但原图命名和身材比例不一样。模组替换每个部位时按三步往下找：

1. 先找当前角色的原生贴图名，比如 Experiment 找 `experimentUpTorso.png`，Milky 找 `milkytorso.png`，Dune 找 `duneUpTorso.png`。
2. 没找到就查通用部位名，比如 `UpTorso.png`、`Head.png`、`Tail.png`。
3. 还是没有，就去同一皮肤目录里借另外两个角色同部位的图顶上。

原版贴图默认朝左；如果角色左右转身需要不对称图案，多放一张带 `R_` 前缀的图（例如 `R_experimentUpTorso.png`），朝右时会自动换上。四肢需要区分前景侧和背景侧时，在名字末尾加 `F` 或 `B` 后缀即可。

第 3 步跨角色借图只是保底，免得单角色皮肤切到 Milky 或 Dune 时全身退回原版。不过 Milky 躯干宽扁、Dune 整体大一圈，三个角色的头型长宽比也差很多，硬借别的体型贴图会明显变形。想一套皮肤通吃三个角色，最好在同一个皮肤目录里把三套命名的图都画齐。完整的节点名与原图尺寸表见 [docs/character-mapping.md](docs/character-mapping.md)。

## 联机同步与按需传包

联机部分通过反射对接 `Casualties: Together`，同时可选绑定新版 `Together.Api` 与 `Together.ModHooks` 扩展事件，也能无缝回退兼容存量 5.5.x 版本；角色在大厅选人、局内死亡重生或同 `netId` 换物种重建躯体后都会自动恢复对应的皮肤、翅膀、配件与尾巴参数。面板里有两档同步策略："进入游戏一键同步" 会在进图拿到角色后，把当前皮肤连同存好的配件微调、尾巴参数一起广播出去；"仅被动响应改动" 进图只报皮肤名，等你手动改了配件或松开尾巴滑块时才发增量包。这个选项只管你自己什么时候往外发，别人发来的同步消息照常接收。每个人的配件微调与尾巴形变参数都只作用于角色自身，远端玩家的参数独立存在运行期内存中，既不会影响你自己的角色表现，也不会改动你本地的 `.cfg` 配置文件。如果想录屏或排查穿模，面板里还有两个独立的原版外观开关，可以只让自己显示原版，或者只把其他人都看成原版。

按需拉取缺失皮肤包现默认开启（可在面板的多人同步设置里开关）。房间里有人穿了你本地没有的皮肤时，模组会自动向对方或房主请求传输：16 KB 以内的小皮肤包单包直发；超过 16 KB 的高清大图、多帧动画或含 `WoundView/` 全套立绘的皮肤包自动按 16 KB 分片传输并做 CRC32 校验重组（同时适配 Steam 通道与 LiteNetLib 直连窗口），整包体积上限 2 MB，单文件上限 512 KB。就算房主关掉了自己的皮肤包同步或本地已有同名皮肤，服务端也会照常校验并把皮肤包中继给房间里需要的客机；未装本模组或没穿皮肤的远端玩家也会自动剥离联机母版预制体可能残留的贴图，保持原版外观。拉下来的皮肤包默认只留在内存和当次会话缓存目录 `BepInEx/cache/SkinSync_SyncCache/`（下次启动游戏自动清空），只有发送方勾选 "允许对方永久保存我发送的皮肤包" 且接收方也勾选 "拉取到的皮肤包写入 plugins/CustomSprites/"，才会真正存进你的 `CustomSprites` 目录。

## 链接与致谢

- 模组仓库：https://github.com/Bytechey/SkinSync
- 演示视频：https://www.bilibili.com/video/BV1p1GJ64EAG
- 代码审查与修改说明：[docs/audit-and-changes.md](docs/audit-and-changes.md)

开发人员：[Bytechey](https://github.com/Bytechey)、[huanxin886](https://github.com/huanxin886)

联机测试：Cross、Meteor、Rdby
