# 雨天河流 V16 · 地表与岸线精修

在 V15 柔和枝叶基础上完善整个环境：草泥地表分色、原创地面纹理、贴地刹车痕、河岸露根与沉积碎石、木屋石基/落水管、栅栏外成组岩石与地被。玩法布局与车斗回收/吊运结构沿用现有设计。

## 交付位置

- 母版 `River_16_DetailedLandscape.blend`，三张材质纹理已打包，可直接打开。
- `CargoClash_RiverV16.unitypackage`：独立 URP 预览及实际依赖。
- Unity 根目录 `Assets/CargoClash_RiverV16`，场景 `Scenes/RiverV16_Preview.unity`，整体 prefab `Prefabs/RiverV16_Level.prefab`。
- `模型/River16_Environment`、`River16_Water`、`River16_PickupPlank`：各有 FBX 与 GLB。雨、水流、货物及卡车的运行组件和引用以 Unity 包为准，单独 FBX/GLB 不包含完整玩法。
- `预览`：12 个 Blender 角度，以及独立 Unity 渲染。

## 本轮修改

| 内容 | 处理 |
|---|---|
| 地表 | 保留高度与碰撞，重画宏观色块、树根/岩石接触色；加入低对比地表细纹与沥青颗粒 |
| 刹车痕 | 原事故轨迹不变；细化胎纹、边缘渐淡、贴近地面并关闭投影 |
| 河岸 | 8 处碎石沉积、露根和阔叶植物组合，避开两处涉水路线及交互点 |
| 木屋 | 62 块贴地石基、4 根圆弯落水管、固定箍与排水口碎石；门口保持净空 |
| 外围 | 7 组岩石地被，28 块带苔藓/裂隙的岩石，224 株阔叶地被及草簇 |
| 树木 | 沿用 V15 曲面叶片、叶脉纹理和自定义法线；69 棵树、38 丛灌木 |

## 接入步骤与约定

1. 在项目分支导入资源包，检查共用的 V10/V12 依赖差异并保留既有 GUID。只使用生成的 prefab、材质与运行组件；预览 Builder 有独立工程标记检查，不应在实际游戏运行旧 BuildAll。
2. 将 `RiverV16_Level` 放入关卡，复用现有收货、角色、联机与计时系统。42 件货物是预览摆放：主机生成接管后关闭 `PreviewOnly_42Cargo_RemoveBeforeHostSpawn`，避免重复生成。
3. 维持 28 块可拾取木板、10 个拦截槽及每槽独立插入时间。车体仍为 V12 `River12_TruckPlaced`，保留 CargoBed、CargoCenter、吊点和 recoveryBed 引用。
4. 保留 `RiverLevelReferences` 中的两处过河点、货物出生点、木板点、流向采样和拦截点；新装饰不增加碰撞体。现有 `Collision_AuthoredProposals` 仍是接入用碰撞提案，角色运动控制与货物漂流逻辑由现有游戏接管。
5. 所有自然材质保留 `DetailTint` 顶点色。树叶模型 Import Normals，不能重新计算自定义法线；10 棵近景树有两级 LOD，59 棵外围树有远景网格和裁切。
6. `LeafPaint.png`：关闭 sRGB，Clamp、Trilinear、Mipmap；`GroundFinish.png` / `AsphaltGrain.png`：关闭 sRGB，Repeat、Trilinear、Mipmap。两张新纹理是线性 RGB 颜色乘数，使用模型第一套 UV，4 米平铺。Blender 内层名为 `UV_TileMeters`。
7. 环境着色器 `CargoClash/RiverV16/EnvironmentSurface`。树叶 `_LeafSoftness=1`、双面、`_Wetness=0`；其他材质 `_LeafSoftness=0`。`River16_RoadTread` 关闭投影，避免刹车痕看起来像凸起的条带。
8. V16 的 EnvironmentSurface、FlowWater、WetSurface 修复了分级阴影接缝并补全软阴影变体。卡车只在 V16 中复制材质并更换光照着色器，保留原材质参数和几何。雨和水流使用已有 V10 运行组件。将独立天气 prefab 的 targetCamera/precipitationAnchor 接到游戏摄像机，并绑定关卡和水面；半场三分钟和直升机吊运触发仍由现有游戏流程管理。

## 校验范围

母版是含完整外围景观的高精度场景，环境约 683 万三角形；导入预览做材质合批与树木 LOD。模型检查保留对象的顶点、变换和父子关系，检查新增闭合网格朝向，并对三组 FBX/GLB 重新导入核对。

报告：`model_validation.json`、`export_validation.json`、`unity_validation.json`、`tree_lod_validation.json`、`v16_surface_validation.json`。

本轮只修改独立建模与独立 UnityPreview 工程，未修改实际 Cargo-Clash 游戏。实际摄像机视角、目标设备帧率、联机同步、角色碰撞和完整三分钟流程需要接入游戏后确认；这里的渲染检查不代表已完成这些游戏内验证。
