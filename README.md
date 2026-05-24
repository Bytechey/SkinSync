# SkinSync V1.0.9 for Casualties: Unknown

> 适配游戏《未知伤亡》的皮肤同步模组  
> 作者：Bytechey（与 AI 协作完成）  
> 兼容：KrokMP 多人模式

## 简介

本模组允许将游戏中的默认 `exp` 皮肤替换为玩家自定义的皮肤。  
参考了 N 网模组 `SpriteReplacer`，同时实现了与 `KrokMP` 的兼容，确保在多人游戏中每个玩家的皮肤更改能够正确同步。

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
   - 文件夹名称格式：`st` + 数字（例如 `st1`、`st2`、`st3`……）
   - **注意**：请勿跳过数字，可能引发未知问题

3. 每个皮肤文件夹（如 `st1`）下必须包含两个子文件夹：
   - `Body/` — 身体部位贴图
   - `Head/` — 头部部位贴图

## 文件结构示例

- Casualties Unknown Demo/
- ├── BepInEx/
- │ └── plugins/
- │ ├── SkinSync.dll
- │ └── CustomSprites/
- │ ├── st1/
- │ │ ├── Head/ (头部皮肤文件)
- │ │ └── Body/ (身体皮肤文件)
- │ ├── st2/
- │ │ ├── Head/
- │ │ └── Body/
- │ └── ...


## 注意事项

- 请确保皮肤贴图的格式和尺寸与游戏原文件兼容
- 多人模式下需所有玩家安装本模组才能正确同步皮肤

## 测试人员

| 角色 | 姓名/ID |
|------|---------|
| 主测 | Bytechey |
| 联机测试 | Cross  Meteor Rdby ... |

---

*本模组由 AI 辅助完成*  
*Cooperated with AI*
        
      
