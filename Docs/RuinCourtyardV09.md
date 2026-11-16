# 藤坠石庭 V09 场景精修

2026-11-16。将已确认的 V09 美术接入 `codex/ruin-courtyard-interactions`。参考本项目雨天河流 V16 的地表过渡、枝叶、石材风化和林下植物，延续 ChenHaoyu 的庭院结构与玩法。

![Unity 中的庭院全景](verification/ruin-v09/unity-overview.png)

## 这版改了什么

| 部分 | 处理 |
| --- | --- |
| 地表与通路 | 按原碰撞布局连接 12 条路线，泥路、草地、树根及墙脚接触色连续过渡 |
| 树木与林下 | 替换场内 58 棵树，增加 19 棵外围树；补充阔叶地被、蕨类、花、落叶和碎石 |
| 建筑与石块 | 石材细纹、缺角、裂隙、局部苔藓、攀藤；自然石在原外包范围内细化 |
| 两处坑洞 | 连续岩层、矿物夹石、露根、坑沿植物和坑底碎石 |
| 外围与公路 | 延展地表、外围岩组、路面修补、裂纹和路肩碎石 |
| Unity 资源 | 共享 4096 × 4096 图集、URP shader、140 组近远景 LOD |

![Unity 中的货仓与通路](verification/ruin-v09/unity-vault.png)

![Unity 中的坑沿](verification/ruin-v09/unity-pit.png)

这三张图是 Unity 6000.6.1f1 独立美术验证场景的渲染；展示模型、材质和比例，完整游戏运行的检查另记于下面的验证记录。

## 如何进入游戏

使用 Unity **6000.6.1f1 / URP**，首次克隆后先执行 `git lfs pull`。打开 `Assets/CargoRover/Scenes/CargoRover.unity`，Play 后选择 **PLAY → PRACTICE WITH BOTS → RUIN COURTYARD**。交互操作见 [关卡接入说明](RuinCourtyard.md)。

环境、破坏点、门和藤蔓仍使用 `Assets/Game/Resources/RuinCourtyard/` 下原来的四个 FBX 路径和资源 GUID。`RuinSurface.mat` 使用同目录的 `RuinV09Surface.shader` 与 `RuinWeatherCommon.hlsl`，无需另行导入河流关卡。

`Assets/Editor/RuinV08ModelImporter.cs` 沿用交付时的文件名，内容已更新至 V09。它只处理本庭院资源，导入可读网格、法线和切线，并为 `RuinChunk_*` 的 `_Near` / `_Far` 子物体创建 LODGroup。该脚本必须保留。旧 `rock stratum` 网格仍作为碰撞源，导入器隐藏其渲染器；新岩层负责显示。

## 与最新分支的合并范围

原美术基于 `1c6bda3` 的 FBX 制作；此次以 **`a7e154ef9b74c6dd43f8ab3760aaeb37a5cac2d9`** 为提交基线，保留其全部关卡代码与 `RuinLayout.json`。特别保留藤梯出口 X=70.90、实际角色占位、合搬支撑和关卡专用导航尺寸修正。

美术保留原坐标框架、门铰/门叶、R1–R12 状态组、货位、拾取点及碰撞源。新增自然装饰不添加碰撞体；可玩区域地面高度保持原样。美术预览中的货车和货物只用于比例展示，环境 FBX 不包含它们，游戏仍按原系统生成。

## 检查与资源规模

- [FBX 接口检查](verification/ruin-v09/fbx-interface.json)：748 项通过，覆盖原节点矩阵、碰撞三角形、UV、有限坐标和零面积面。
- [独立 Unity 美术检查](verification/ruin-v09/unity-art-validation.txt)：744 项通过，覆盖四个模型、140 组 LOD、门叶、货位、共享材质和场景范围。这份检查在接入最新分支之前完成。
- [模型统计](verification/ruin-v09/model-statistics.json)：全部破坏状态合计近景约 413.7 万三角面，连同远景约 486.1 万；近景 206 个网格，所有 LOD 共 346 个。这些数值不是同时可见数量或实测帧率。
- 最新分支的构建与运行结果见 [本次接入验证](verification/ruin-v09/README.md)。

素材来自本项目原庭院、用户现有河流 V16 和本轮程序化制作。没有新增外部现成模型或贴图。仓库提交游戏所需资源及验证资料；可编辑 Blender 原件和生成过程继续保存在本机独立建模交付目录。
