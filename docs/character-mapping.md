# 三个角色的部位与贴图对照

数据来源：直接读取安装版 `CasualtiesUnknown_Data` 里三个角色 prefab 的 `SpriteRenderer` 引用
（`Experiment / Milky / Dune` 各 15 个肢体节点，结构完全一致，只有贴图名不同）。

## 肢体（Body 下 15 个同名 GameObject）

| 肢体 GameObject | Experiment | Milky | Dune |
|---|---|---|---|
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

要点：

- Experiment 与 Milky 的**前后侧共用同一张贴图**（靠 sortingOrder 区分前后），只有手是前后分开。
- Dune 的**后侧（B）肢体用 Alt 贴图**：`duneUpArmAlt` / `duneDownArmAlt` / `duneHandAlt` / `duneThighAlt`。
  这些 Alt 图在 C# 代码里没有任何引用，是 prefab 直接绑定的。
- Milky 的 `mildownarm` **拼写里缺 `ky`**（不是 `milkydownarm`），按规则拼接会漏。
- Milky 与 Dune 都**没有正面头**（`milkyhead` / `duneHead` 不存在），只有 `…HeadBack`。

## 原图尺寸（像素，`pixelsPerUnit` 全部 = 8）

| 部位 | Experiment | Milky | Dune |
|---|---|---|---|
| 上躯干 | 8×10 | 16×10 | 8×12 |
| 下躯干 | 8×10 | 10×8 | 8×12 |
| 上臂 | 4×12 | 4×10 | 10×14 |
| 下臂 | 4×14 | 4×14 (`mildownarm`) | 6×16 |
| 手 | 6×4 / 5×4 | 6×4 / 5×4 | 6×6 |
| 大腿 | 6×11 | 6×9 | 12×15 |
| 小腿 | 4×8 | 4×8 | 4×8 |
| 脚 | 6×11 | 6×11 | 8×15 |
| 头 | 26×12 | 14×14 | 20×26 |
| 尾巴 | 48×20 | 48×20 | 58×32 |

三套体型的宽高比例差异很大（Milky 躯干宽扁、Dune 整体放大、头型三者完全不同），
所以**跨角色借用同一套美术在几何上不成立**，每个角色应当有各自画的图。

## 表情（`FacialExpression` 组件）

Experiment 的字段绑定（实测）：

| 字段 | 贴图 |
|---|---|
| `defaultHead` | experimentHeadBack |
| `defaultHeadMouth` | experimentHeadBackMouth |
| `defaultHeadMouthHalf` | experimentHeadBackMouthMini |
| `eyesGone` / `eyesGoneHealed` | experimentEyeGone / …GoneHealed |
| `disfiguredHead[0..2]` | experimentHeadDisfigured1/2/3 |
| `disfiguredHeadHeal[0..2]` | …Disfigured1Healed/2Healed/3Healed |
| `eyeList[i].front/back` | experimentEyeOpen/LookBack、HalfClosed/…Back、Closed、Scared/…Back、Panic、Sad/…Back、Happy |

Milky / Dune 的差异：

- Milky 没有 `…MouthMini`、没有 `milkynosebleed`；表情图 7 种但缺部分 `back` 变体
- Dune 只有 3 张眼睛图（`duneEyes` / `duneEyesClosed` / `duneEyesHalfClosed`），没有真正的表情系统

## 做多角色皮肤的建议

贴图替换按**当前角色该部位的真实贴图名**精确匹配，所以：

1. 想让一套皮肤通吃三角色 → 把三个角色命名的文件放进**同一个皮肤目录**（如同时有
   `experimentUpTorso.png`、`milkytorso.png`、`duneUpTorso.png`），运行时按当前角色自动选中。
2. 只画一个角色 → 只放那个角色的命名，切到别的角色时会提示"没有任何肢体贴图能对应当前角色"。
3. 不要靠改名把别的角色的美术硬套过来：头型与躯干比例差异太大，缩放后会明显变形。
