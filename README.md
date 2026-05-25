# SkinSync RYV1.1.0 for Casualties: Unknown

> 适配游戏《未知伤亡》的皮肤同步模组        
> 作者：RY（与 AI 协作完成）       
> 单独分支，版本V1.1.0     
> 兼容：KrokMP 多人模式3.0.0

## 简介

本模组允许将游戏中的默认 `exp` 皮肤替换为玩家自定义的皮肤。  
参考了 N 网模组 `SpriteReplacer`，同时实现了与 `KrokMP` 的兼容，确保在多人游戏中每个玩家的皮肤更改能够正确同步。


## GUI操作以及指令（RY单独版本，1.1.0内容）
游戏内ESC菜单正下方，点击对应头像切换
超过4个以上的皮肤正下方将会有换页按键
游戏内~打开控制台后输入skin查看指令具体作用

## 安装方法

1. 将以下文件放置于 `BepInEx/plugins` 文件夹下：
   - `SkinSyncMod.dll`
   - `CustomSprites` 文件夹（包含皮肤贴图）

2. 皮肤文件应放置在 `CustomSprites` 文件夹内，RY单独版本是dll文件同目录下的CustomSprites随意命名
   
3. RY单独版本则为body为全身贴图且有特殊设计（_R支持不对称皮肤）(转格式目前应该可以，就是把所有贴图换个位置到body)
   
## 文件结构示例

  RY单独分支:
- Casualties Unknown Demo/
- ├── BepInEx/
- │ └── plugins/
- │ ├── SkinSync.dll
- │ └── CustomSprites/
- │ ├── <皮肤名字>/
- │ │ └── Body/ (皮肤文件)
- │ ├── <皮肤名字>/
- │ │ └── Body/
- │ └── ...

## 注意事项

- 请确保皮肤贴图的格式和尺寸与游戏原文件兼容
- 多人模式下需所有玩家安装本模组才能正确同步皮肤

## 测试人员

| 角色 | 姓名/ID |
|------|---------|
| 单独分支版本自我联机测试 | RY |

---

*本模组由 AI 辅助完成*  
*Cooperated with AI*
        
      
