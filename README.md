# SkinSync V1.2.0 for Casualties: Unknown

> 适配游戏《未知伤亡》的皮肤同步模组  
> 作者：Bytechey HuanXin（与 AI 协作完成）  
> 兼容：Together 多人模式（CasualtiesMP 5.4.0）

## 简介

本模组允许将游戏中的默认 `exp` 皮肤替换为玩家自定义的皮肤。  
参考了 N 网模组 `SpriteReplacer`，同时实现了与 `Together`（CasualtiesMP 5.4.0）的兼容，确保在多人游戏中每个玩家的皮肤更改能够正确同步。   
另外实现血液粒子颜色更改以及尾巴动效优化。

## 按键操作

| 按键 | 功能                 |
|------|----------------------|
| F6   | 切换至下一皮肤 |
| F7   | 切换至上一皮肤 |
| F8   | 切换至默认皮肤 (exp) |

## 安装方法

1. 将以下文件放置于 `BepInEx/plugins` 文件夹下：
   - `SkinSyncMod.dll`
   - `CustomSprites` 文件夹（包含皮肤贴图）

2. 皮肤文件应放置在 `CustomSprites` 文件夹内，并按照以下规则命名：
   - 文件夹名称格式现已无限制

3. 每个皮肤文件夹下通常包含两个子文件夹（或更多）：
   - `Body/` — 身体部位贴图
   - `Head/` — 头部部位贴图
   - `Wings/` — 翅膀部位贴图（如果有）
   - ...
## 文件结构示例

- Casualties Unknown Demo/
- ├── BepInEx/
- │ └── plugins/
- │ ├── SkinSync.dll
- │ └── CustomSprites/
- │ ├── skin1/
- │ │ ├── Head/ (头部皮肤文件)
- │ │ └── Body/ (身体皮肤文件)
- │ ├── skin2/
- │ │ ├── Head/
- │ │ └── Body/
- │ └── ...


## 注意事项

- 请确保皮肤贴图的格式和尺寸与游戏原文件兼容
- 多人模式下需所有玩家安装本模组才能正确同步皮肤

## 尺寸归一（Visual → NormalizeSpriteSize）

皮肤 PNG 的像素尺寸普遍是游戏原图的 1.5-2 倍（作者按编辑器画布尺寸作画），而游戏渲染大小只由
`sprite.pixelsPerUnit` 决定，直接换图会让角色整体放大、肢体互相重叠。

本版本新增 `NormalizeSpriteSize`（默认开启）：每次换图时按**目标部位原贴图的真实世界尺寸**等比缩放，
使皮肤与游戏原生大小一致，且自动适配三个角色的不同体型。关闭后恢复旧行为（皮肤像素尺寸原样渲染）。

## 多角色支持

游戏有三个角色（Experiment / Milky / Dune），各自一套贴图命名。贴图替换是按**当前角色该部位的
贴图名**精确匹配的，因此：

- 皮肤目录里只放 `experiment*` 命名的图 → 只在 Experiment 上生效
- 想让 Milky / Dune 也有皮肤，把对应命名的图（如 `milkytorso`、`duneUpTorso`）放进**同一个皮肤目录**即可
- 当前角色一张图都匹配不上时，日志会输出 warning，不再静默失败

三个角色「部位 → 贴图名」的完整对照见 `docs/character-mapping.md`。

## 测试人员

| 角色 | 姓名/ID |
|------|---------|
| 主测 | Bytechey |
| 联机测试 | Cross  Meteor Rdby ... |

---

*本模组由 AI 辅助完成*  
*Cooperated with AI*
        
      
