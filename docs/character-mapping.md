# 三个角色的部位与贴图对照

数据来源：直接读取安装版 `CasualtiesUnknown_Data` 里三个角色 prefab 的 `SpriteRenderer` 引用（`Experiment / Milky / Dune` 各 15 个肢体节点，层级结构一致，贴图命名与尺寸不同）。

## 肢体（Body 下 15 个同名 GameObject）

| 肢体 GameObject | Experiment | Milky | Dune |
| --- | --- | --- | --- |
| `Head` | experimentHeadBack | milkyheadback | duneHeadBack |
| `UpTorso` | experimentUpTorso | **milkytorso** | duneUpTorso |
| `DownTorso` | experimentDownTorso | milkydowntorso | duneDownTorso |
| `UpArmF` | experimentUpArm | milkyuparm | duneUpArm |
| `UpArmB` | experimentUpArm | milkyuparm | **duneUpArmAlt** |
| `DownArmF` | experimentDownArm | **mildownarm** | duneDownArm |
| `DownArmB` | experimentDownArm | **mildownarm** | **duneDownArmAlt** |
| `HandF` | experimentHandF | milkyhandf | duneHand |
| `HandB` | experimentHandB | milkyhandb | **duneHandAlt** |
| `ThighF` | experimentThigh | milkythigh | duneThigh |
| `ThighB` | experimentThigh | milkythigh | **duneThighAlt** |
| `CrusF` / `CrusB` | experimentCrus | milkycrus | duneCrus |
| `FootF` / `FootB` | experimentFoot | milkyfoot | duneFoot |

命名细节：

- Experiment 与 Milky 的前后侧四肢共用同一张贴图（靠 `sortingOrder` 区分前后），只有手部拆成前后两张。
- Dune 的后侧（`B`）肢体绑定了独立的 `Alt` 贴图：`duneUpArmAlt`、`duneDownArmAlt`、`duneHandAlt`、`duneThighAlt`。这些贴图在反编译 C# 里没有直接字符串引用，由 prefab 直接挂载。
- Milky 的前臂贴图名是 `mildownarm`（拼写里少了 `ky`，不是 `milkydownarm`），按前缀机械拼接会漏掉。
- Milky 与 Dune 都没有单独的正面头贴图（不存在 `milkyhead` 或 `duneHead`），默认头统一叫 `...HeadBack`。

## 原图尺寸（像素，`pixelsPerUnit` 全部为 8）

| 部位 | Experiment | Milky | Dune |
| --- | --- | --- | --- |
| 上躯干 | 8x10 | 16x10 | 8x12 |
| 下躯干 | 8x10 | 10x8 | 8x12 |
| 上臂 | 4x12 | 4x10 | 10x14 |
| 下臂 | 4x14 | 4x14 (`mildownarm`) | 6x16 |
| 手 | 6x4 / 5x4 | 6x4 / 5x4 | 6x6 |
| 大腿 | 6x11 | 6x9 | 12x15 |
| 小腿 | 4x8 | 4x8 | 4x8 |
| 脚 | 6x11 | 6x11 | 8x15 |
| 头 | 26x12 | 14x14 | 20x26 |
| 尾巴 | 48x20 | 48x20 | 58x32 |

## 表情（`FacialExpression` 组件）

Experiment 的字段绑定（实测）：

| 字段 | 贴图 |
| --- | --- |
| `defaultHead` | experimentHeadBack |
| `defaultHeadMouth` | experimentHeadBackMouth |
| `defaultHeadMouthHalf` | experimentHeadBackMouthMini |
| `eyesGone` / `eyesGoneHealed` | experimentEyeGone / experimentEyeGoneHealed |
| `disfiguredHead[0..2]` | experimentHeadDisfigured1 / 2 / 3 |
| `disfiguredHeadHeal[0..2]` | experimentHeadDisfigured1Healed / 2Healed / 3Healed |
| `eyeList[i].front` / `back` | experimentEyeOpen / LookBack、HalfClosed / HalfClosedBack、Closed、Scared / ScaredBack、Panic、Sad / SadBack、Happy |

Milky 与 Dune 的差异：

- Milky 没有 `...MouthMini`，也没有 `milkynosebleed`；眼睛共 7 组，部分状态缺少 `back` 变体。
- Dune 只有 3 张眼睛贴图（`duneEyes`、`duneEyesClosed`、`duneEyesHalfClosed`），没有复杂表情切换。

## 运行时查找顺序与作画建议

当前 `SkinResolver` 对每个部位按三步查找：

1. 先按上表查**当前角色该部位的原生贴图名**（如 Milky 上躯干先找 `milkytorso.png`）。
2. 未命中时查**通用部位键名**（如 `UpTorso.png`）。
3. 仍未命中时，从同一皮肤目录里借用**其他角色同部位的贴图**（如借用 `experimentUpTorso.png`）。每一步都支持加 `R_` 前缀匹配朝右贴图，以及加 `F` / `B` 后缀匹配前后侧。

给皮肤作者的建议：

1. 想让一套皮肤在三个角色上都贴合体型，把三套角色命名的 PNG 放进同一个皮肤目录即可（比如同时放 `experimentUpTorso.png`、`milkytorso.png`、`duneUpTorso.png`），切换角色时会自动命中各自的原生图。
2. 只给单个角色画了图也能直接用，切到另外两个角色时会走第 3 步借图兜底，不会直接退回原版皮肤；但 Milky 躯干宽扁、Dune 四肢偏粗、三者头型长宽比完全不同，借图显示在几何比例上对不齐，正式发布多角色皮肤时建议按上面的原图尺寸表分开绘制。
