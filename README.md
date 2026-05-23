SkinSync V1.0.9 for Casualities:Unknown 《未知伤亡》
By Bytechey
本mod由AI辅助完成 
Cooperated with AI

本模组实现将游戏中exp皮肤替换为玩家自定义皮肤，参考了N网mod SpriteReplacer，同时实现了与mod KrokMP的兼容，确保在多人模式中每个玩家各自皮肤更改的同步

按键操作：
F5 切换至默认皮肤(exp)
F6 切换至下一皮肤
F7 切换至上一皮肤

使用方法：
将模组SkinSync.dll与贴图文件夹CustomSprites置于BepInEx的plugins文件夹下
将皮肤文件放置于CustomSprites文件夹下且命名为st\d 其中\d为数字
如st1 st2 st3...（请勿跳过数字，暂不知是否有BUG）
皮肤文件夹下含Body与Head两个文件夹，内含各自部位贴图
正确文件格式应如下
//游戏根目录\
  Casualties Unknown Demo\
    ...
    BepInEx\
      ...
      plugins\
        ...
        SkinSync.dll
        CustomSprites\
          Head\
            //头部皮肤文件
          Body\
            //身体皮肤文件
        
      
