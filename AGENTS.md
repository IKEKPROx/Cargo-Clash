# Cargo Clash 工程规则

给在这个仓库里工作的 agent 和人看。玩法规则以设计文档（`Docs/设计文档_v0.4.2.md`）为准，这里只写做工程时要遵守的事。

接手前先读 `Docs/修改记录与交接说明.md`：里面有做过什么、现在到哪一步、还差什么。

## 技术栈

- Unity **6000.6.1f1**，URP，Input System。不要升级版本。
- 联网用 **Netcode for GameObjects 2.13.x**。NGO 3.x 要 Unity 6.7，不能用。
- 开房用 Multiplayer Services 的 Sessions 和 Relay。工程没连 Unity Cloud 时退回局域网房间，房间码是主机 IP。
- 不新增包，除非先和大家确认。包版本以 `Packages/packages-lock.json` 为准。

## 谁说了算

| 内容 | 由谁控制 | 在哪里 |
|---|---|---|
| 角色移动 | 角色的拥有者（NetworkTransform 设为 Owner 权限） | `PlayerAvatar.TickOwner` |
| 机器人 | 主机拥有，走和玩家一样的代码 | `BotBrain` 实现 `ICharacterInput` |
| 冲撞判定、倒地、掉货 | 主机 | `PlayerAvatar.ServerCheckRamHit`、`ServerKnockDown` |
| 地上的货物物理 | 主机（NetworkRigidbody） | `CargoItem` |
| 一人搬的货 | 主机定谁在搬，各电脑在本地把货贴到搬运者头上 | `CargoItem.LateUpdate` |
| 两人合搬的大件 | 主机取两人输入的平均方向并移动大件，角色跟着抓取点走 | `CargoItem.MoveWithCarriers`、`PlayerAvatar.TickCoCarry` |
| 砸货耐久、倒计时、比分、称号 | 主机 | `CargoItem`、`MatchDirector` |
| 直升机吊走货箱的结尾 | 主机只同步到达时间，各电脑自己播放动画 | `MatchDirector.HelicopterArrivesAt`、`HelicopterExtraction` |
| 角色动作 | 各电脑按同步下来的状态自己播放，不影响规则 | `CharacterVisual` |

改联网逻辑时，先想清楚这段代码在哪台电脑上跑。写 `NetworkVariable` 和判定规则都只在主机上做。

## 写代码的规则

- 角色不要直接读键盘，统一通过 `ICharacterInput`。这样机器人和自动测试才能用。
- 数值放 `GameConfig` 和 `CargoDefinition`，不要写死在代码里。玩家看到的文字放 `GameText`。
- 特效和音效不能影响规则，它们只在收到状态变化或 RPC 后播放。
- 所有联网物体都是从预制体动态生成的，场景里不放 NetworkObject。新增的联网预制体要在 `RoomService.Awake` 里注册。
- 不要在 Finder 里移动 `Assets` 下的文件，要带上 `.meta` 或在 Unity 里移动。

## 生成脚本

`Tools > Cargo Clash > Build Game Content`（`Assets/Game/Editor/CargoClashBuilder.cs` 和 `CargoClashBuilder.Title.cs`）生成了初版的预制体、数据、教学关场景、开始场景和界面。**手工改过这些资源后再运行它，改动会被覆盖。** 改了资源之后，就直接在 Unity 里继续改，不要再运行它。

## 模型和动作

角色、货物、货车、直升机和场景都是用脚本在 Blender 里做的，比用 Unity 的方块和球拼出来的好看。脚本写出 `Assets/Game/Models` 里的 FBX，生成脚本再按材质名换成 `Assets/Game/Materials` 里的材质，对照表在 `CargoClashBuilder.ModelMaterials`。在仓库根目录运行：

```bash
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python Tools/blender/make_character.py
```

其他模型把脚本换成 `make_cargo.py`、`make_truck.py`、`make_helicopter.py`、`make_scenery.py`，开始页面的 Logo、按钮和卡片图用 `make_ui.py`。末尾加 `-- --sheet 文件夹` 可以同时渲染检查图（直升机是 `-- --preview 图片.png`）。运行之后，还要运行上面的生成脚本才会进入游戏。

场景的每个模型是一个 FBX，`make_scenery.py` 加 `-- --only Rock_A,Barn` 只重做这几个。生成脚本把它们做成 `Assets/Game/Prefabs/Scenery` 里的预制体，再摆进关卡（`CargoClashBuilder` 的 `AddTrees` 到 `AddOutskirts`）。模型本身没有碰撞，挡人的是单独的 `Blocker` 子物体，所以换模型不影响玩法和寻路。放进可玩区域的新东西，要么加 `Blocker`，要么矮到走过去也不奇怪（比如草和花）。

地面是画出来的贴图：`make_ground.py` 用同样的方式运行，写到 `Assets/Game/Textures`（末尾加 `-- --title` 只画开始场景用的那几张）。刹车痕和货车周围翻开的土画在贴图上，货车挪了位置要改脚本开头的 `TRUCK` 和 `TRUCK_YAW` 再运行一次。

直升机结尾的镜头从地图东南边往西看。高的东西不要放进 `OutskirtsIsFree` 排除的区域，否则会挡住这个镜头，或者挡住地图南端。

**场景物件不要勾 Static（Batching Static）。** 这台 Mac 上试过：静态合批和 SRP Batcher 一起用时，镜头转到某些位置，地面贴图和货车旁的黄色区域会整片变黑或消失。去掉静态合批就好了，原因没有查清。

货物的尺寸要和 `CargoClashBuilder.BuildCargo` 里的碰撞盒一致。货车的 `CargoBed` 是单独的物体，直升机吊走的就是它。

开始场景用的 `TitleTruck` 是同一辆车没出事的样子（`make_truck.py` 同时输出），由 `CargoClashBuilder.BuildTitleTruck` 组装，`Assets/CargoRover` 里的脚本带动它。轮子是单独的物体，轴距和轮距要和那些脚本一致。

角色的每个动作是 `make_character.py` 里的一个函数，只写手脚放在哪里，手肘和膝盖由脚本算出来。新增一个动作要改三处：

1. `make_character.py`：写动作函数，加进 `CLIPS`。
2. `Assets/Game/Editor/CargoClashCharacterAnimator.cs`：加状态和过渡。循环的动作还要加进 `LoopingClips`。
3. `Assets/Game/Scripts/Player/CharacterVisual.cs`：从联网状态决定什么时候播。

Animator 有两层：`Base` 管全身，`UpperBody` 只管上半身，这样搬货和砸货时腿还能继续跑。

描边是 URP 渲染器上的 `ModelOutline` 功能，会描 Player、Cargo 和 Outlined 三个层上的模型。要描别的东西，把它放到 Outlined 层。方块这类有硬边的模型描出来会断开，要先换成圆角模型。场景模型里名字以 `Plain` 开头的部分（电线、广告牌文字）不描边，草、花和云也不描。

## 开始页面和关卡列表

开始场景是 `Assets/CargoRover/Scenes/CargoRover.unity`，整个由 `CargoClashBuilder.BuildTitleScene` 生成。页面的跳转在 `TitleFlow`：标题 → 主菜单 → 选关 → 练习或大厅。加入别人的房间不选关，由主机决定。

**关卡列表**在 `GameConfig.levels`，由 `CargoClashBuilder.BuildLevelList` 写入。第 1 个是教学关（`Level01`），其余是占位：`scene` 为空的关卡，卡片是锁着的。开放一关要做三件事：

1. 在 `Assets/Game/Scenes` 做好场景（可以复制 `Level01.unity`，`LevelLayout` 和 `MatchHud` 都要有）。
2. 在 `BuildLevelList` 填上它的 `scene`，运行生成脚本，它会自动进 Build Settings。如果不想再运行生成脚本，就直接改 `GameConfig` 资源，并手动把场景加进 Build Settings。
3. 换掉卡片图。`Assets/Game/UI/LevelNN_Card.png`，928×500。占位图是 `make_ui.py` 画的，教学关那张是实机截图：窗口运行时加 `-ccHideUi -ccShotDir 目录` 拍下来，再裁成这个比例。

开局时主机把选的关卡放进 `SessionRoster.Level`，加载场景都从这里取，不要再写死场景名。

界面的颜色和做法集中在 `CargoClashBuilder.Title.cs` 的 `UiButton`、`UiPanel`、`UiText`。一个控件放在 `PopSlot` 里，弹入的动画缩放外层，鼠标悬停的动画缩放里层，这两个不能放在同一个物体上。图片要先在 `UiSprite` 里设好导入方式。所有字用 `Assets/Game/UI` 里的 Lilita One，它只有拉丁字母，以后要显示中文得另外配字体。比赛里的界面（`BuildHud`）还是旧样式，没有用这套控件。

背景的公路、田地、树是一条无限循环的带子：`TitleScenery` 把落到车后面的东西挪到前面。一组东西的循环长度要比镜头能看到的范围长，否则会当着玩家的面跳过去；越远的一组看到的范围越宽，所以循环越长。电线杆都一样高，并埋进地里一点，否则电线接不上。

## 怎么验证

改完代码，至少编译通过并跑一局机器人对局。先退出打开这个工程的 Unity，再运行：

```bash
"/Applications/Unity/Hub/Editor/6000.6.1f1/Unity.app/Contents/MacOS/Unity" -batchmode -quit -projectPath . -executeMethod CargoClashPlayerBuild.Mac -logFile /tmp/cargo-build.log
```

```bash
"Builds/Mac/CargoClash.app/Contents/MacOS/Cargo Clash" -batchmode -nographics -ccMode practice -ccBotLocal -ccHalfSeconds 45 -ccTimeout 200 -ccQuit -logFile /tmp/cargo-match.log
```

```bash
grep CCTEST /tmp/cargo-match.log
```

结果要看到 `RESULT` 和 `DONE errors=0 timedOut=False`。无窗口运行时帧率限制在 60，这样测出来的机器人表现才接近真实对局。

`AutoPilot` 的其他参数：

| 参数 | 作用 |
|---|---|
| `-ccMode host` / `client` | 开局域网主机 / 加入，配合 `-ccAddress`、`-ccExpectClients` 测两台机器 |
| `-ccMode menu` | 用真实的点击走一遍开始页面的每一屏并拍照，同时检查锁着的关卡点不进去、加入失败有提示。配合 `-ccShotDir`，窗口运行 |
| `-ccShotDir 目录`、`-ccShotEvery 秒` | 窗口运行时定时截图，用来检查画面 |
| `-ccSmashScale 倍数` | 临时改砸货时间，用来比较平衡 |
| `-ccPort 端口` | 同时跑多局时避开端口冲突 |
| `-ccUseUi` | 通过真实的菜单按钮走流程（练习或建房、选关、加入、准备、开始）。窗口运行时会真的做点击判定，按钮被挡住就报错；无窗口时没有画面可点，直接触发按钮 |
| `-ccHideUi` | 截图时隐藏界面，用来拍关卡卡片图 |
| `-ccAgain` | 结算后点“再来一局”或“回大厅”，测重新进入的流程 |
| `-ccForceCoCarry` | 每个半场强制两名正方合搬大件，第二个半场放在货车附近，测运送得分 |
| `-ccWaitHostLeave` | 客户端等主机退出，测回到主菜单和提示 |
| `-ccCameraZoom 倍数` | 把镜头拉近，比如 0.42，截图时看清角色 |
| `-ccTour "x,z;x,z"` | 镜头依次看这些位置并截图（要配合 `-ccShotDir`），用来检查场景的每个角落 |
| `-ccUncapped` | 窗口运行时不限帧率，每 5 秒记一次帧率，用来比较两个版本哪个更卡 |

agent 只能验证规则和流程。手感和两人联机必须由人来测：在 Multiplayer Play Mode 里开两个玩家，确认两边都看到倒地、货掉在同一个位置、冷却时间一致，再合并。

## 出 Windows 包

老师要在实验室的 Windows 电脑上运行，这是最后要交的东西。在仓库根目录运行：

```bash
"/Applications/Unity/Hub/Editor/6000.6.1f1/Unity.app/Contents/MacOS/Unity" -batchmode -quit -projectPath . -executeMethod CargoClashPlayerBuild.Windows -logFile /tmp/cargo-build-win.log
```

```bash
Tools/package_windows.sh 输出文件夹
```

- Apple 芯片的 Mac 默认打 ARM64 包，普通 Windows 电脑运行不了，而且构建照样显示成功。所以脚本指定了 x64，打完还会检查。
- 压缩包里要有 `Tools/windows_readme.txt`，不能带 `_BackUpThisFolder_ButDontShipItWithYourGame` 文件夹，`package_windows.sh` 已经处理好了。
- Mac 运行不了 Windows 包，打完一定要在 Windows 电脑上试，地图西南角和结算画面要特别看地面有没有变黑。
- Mac 上要先装 Windows Build Support (Mono) 模块。如果 Hub 下载报 404，是 Unity 把下载转到了中国镜像，而镜像里没有这个文件，可以切到中国以外的网络出口手动下载 pkg。pkg 不能双击安装，要解开后放进 Editor 的 `PlaybackEngines/WindowsStandaloneSupport`，详情见交接说明第 8 节。

## 协作

- 每个功能开一个短分支，走 PR 合并到 `main`，合并前自己先跑一遍。
- 模型、贴图、音频走 Git LFS，`.gitattributes` 已经配好。
- `Assets/Game/Scenes/Level01.unity` 同一时间只让一个人改。其他人在自己的测试场景里做功能，东西尽量做成预制体。
- 不要打开 `com.unity.pipeline` 的 `enableInReleaseBuilds`（默认是关的）。这个包只给本机 agent 跑编译和测试用，打开后发布的包会带一个能执行代码的本地 HTTP 服务。
- 报告要列现成素材：用了外部素材就记进 `ASSETS.md`。
