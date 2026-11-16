# 藤坠石庭：代码与比例验证

2026-11-16。在用户尚未安装 Unity 的情况下，使用本机 Blender 5.3.0、交付 FBX、Unity 的 `.meta` 导入设置及预制体碰撞尺寸检查。**没有运行 Unity 编译、PhysX、导航烘焙、Play Mode 或多人联机。**

## 人物与地图

三种人物保持仓库原来的 0.84 导入缩放。下表高度是静止姿势中最高可选帽饰的顶部相对脚底原点，不包含眩晕星星。

| 项目 | 实测尺寸 |
| --- | --- |
| Short / Medium / Tall 连最高帽饰 | 1.388 / 1.661 / 1.895 米 |
| 共用 CharacterController | 高 1.70 米，半径 0.45 米，skin 0.05 米，台阶 0.30 米 |
| 可玩区域 | 124 × 69 米，8556 平方米；南侧另有 18 米公路布景 |
| 两座 100 物资货仓门洞 | 设计净宽 3.70 米、净高 2.72 米；最高角色头顶余量约 0.825 米 |
| 高台 | 高 2.50 米；攀爬出口的完整角色足迹加 skin 后仍在台面内 |
| 100 大箱碰撞盒 | 2.32 × 1.06 × 1.12 米；原模型连把手等细节约 2.47 × 1.097 × 1.238 米 |
| 双人合搬队形 | 两人中心距 3.42 米，加角色半径及 skin 后纵深约 4.42 米 |

两座货仓各按 10 厘米间隔采样 71 个出仓位置，分别检查大箱和前后两名搬运者，包含门扇实际网格及运行时门扇碰撞盒的包围范围。大箱长轴沿门洞进出方向摆放。两个房间均能容纳合搬队形；这里检查的是空间余量，游戏未新增箱子旋转操作。

另检查了 6 个出生位、15 个货位、坑洞开口和约 3.5 米坑底、12 处破坏状态模型、藤蔓组件、FBX 坐标标记、小地图像素长宽比。详细数值、检查分组及输入文件 SHA-256 见 [ruin-scale.json](ruin-scale.json)。数万条几何断言大部分来自路线与障碍物的逐项比较，不代表实机测试次数。

下图直接使用交付 FBX 和原角色生成，人物为静止姿势。它们是 Blender 比例示意，材质光照与 Unity 实机画面有区别。

![货仓门洞与三种人物比例](ruin-scale-vault.png)

![高台与人物比例](ruin-scale-terrace.png)

## 本轮修正

- HUD 新交互提示中的局部变量改名，消除 `CS0136` 作用域冲突。
- 合搬抓取轴由当前遗迹关卡和箱子朝向决定，避免客户端依赖仅主机设置的货物复位标记。
- 门、墙复原和攀爬落点的角色占位按实际 CharacterController 尺寸计算；大箱合搬扫掠也使用同一尺寸。
- 合搬同时检查箱子和两名玩家脚下的支撑，包含静止时地板被破坏的情况。失去支撑时由主机解除合搬并恢复货物物理。
- 藤梯出口向台内移动 0.22 米至 X=70.90，增加完整落脚余量。
- 仅本关构建导航时将 `agentClimb` 限制到角色 `stepOffset`，避免原默认 0.75 米跨阶能力把机器人引上 0.74 米遗迹基座。半径至少为角色半径加 skin，高度保留原有 2 米余量。项目全局导航设置不变。

Roslyn 对全部 46 个现有 Assets C# 文件检查了语法、重复声明和局部变量作用域，相关错误为 0；检查器自测能检出原 HUD 对应的 `CS0136` 错误模式。由于缺少 Unity 程序集，这不包含 Unity API 类型绑定或 NGO 代码生成，不能替代 Unity 编译。

场景 YAML 的 658 个序列化对象、653 个本地引用及 Assets GUID 唯一性检查通过；新资源均有 `.meta`，LFS 文件已实体化。原教学关、CargoRover 场景、货车预制体、Unity 版本和包依赖保持原文件内容。

## 复现

在仓库根目录运行。Blender 验证只读 Assets，写入本目录；`--python-exit-code 1` 确保断言失败时进程返回错误。

```bash
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python-exit-code 1 --python Tools/blender/verify_ruin_scale.py
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python-exit-code 1 --python Tools/blender/render_ruin_scale.py
dotnet run --project Tools/verification/RuinScopeCheck -- "$PWD"
```

最后一项使用独立的 .NET 8 SDK，只引用其自带 Roslyn，不添加 Unity 包。生成的 `bin`、`obj` 已在工具目录忽略。

Unity 安装后应使用工程的 **6000.6.1f1**，先编译，再按 [关卡接入说明](../RuinCourtyard.md) 的实机清单检查。优先验证主机与客户端分别合搬过门、静止时脚下破坏、坑沿掉落、藤梯出口被占及到期、双方轮换和下一半场复位。
