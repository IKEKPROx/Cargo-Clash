# Cargo Clash 工程规则

给在这个仓库里工作的 agent 和人看。玩法规则以设计文档（`Docs/设计文档_v0.6.0.md`）为准，这里只写做工程时要遵守的事。

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
| 打人、喷气、重锤 | 拥有者把准星对着的敌人的 ID 发给主机；主机查距离、数这是第几下，再判定推开或倒地 | `PlayerAvatar.UpdateTools`、`ServerSwing`、`ServerSlam`、`ServerTakeHit` |
| 血量、击败、重生 | 主机。血量是 `NetworkVariable`，击败时通知所有人出战报，到时间把人传回出生点 | `PlayerAvatar.ServerKnockOut`、`ServerRespawn`、`MatchDirector.ServerOnKnockedOut` |
| 吸货、吸人 | 拥有者只说按着右键。主机用身体朝向算前方：货由主机拉动，人由主机通知他的拥有者（`PullRpc`），因为移动归拥有者管 | `PlayerAvatar.ServerSuck`、`CargoItem.ServerPullToward` |
| 地上的货物物理 | 主机（NetworkRigidbody） | `CargoItem` |
| 瞄准 | 拥有者算出准星对着哪件货或哪个人，把 ID 发给主机；主机只查距离和状态 | `PlayerAvatar.UpdateAim`、`InteractRpc`、`SetSwingingRpc` |
| 一人搬的货 | 主机定谁在搬。各电脑在本地摆放：自己搬的在手里（`FirstPersonRig`），别人搬的抱在胸前（`CharacterVisual.CarryPoint`） | `CargoItem.LateUpdate` |
| 两人合搬的大件 | 主机取两人输入的平均方向并移动大件，角色跟着抓取点走 | `CargoItem.MoveWithCarriers`、`PlayerAvatar.TickCoCarry` |
| 砸货耐久、倒计时、比分、称号 | 主机。耐久按锤子落下扣，每挥一下扣一次（`PlayerAvatar.ServerSwing`），总时间仍是 `CargoDefinition.smashSeconds` | `CargoItem`、`MatchDirector` |
| 直升机吊走货箱的结尾 | 主机只同步到达时间，各电脑自己播放动画 | `MatchDirector.HelicopterArrivesAt`、`HelicopterExtraction` |
| 角色动作 | 各电脑按同步下来的状态自己播放，不影响规则 | `CharacterVisual` |
| 第一人称的手、货物裂开和碎块 | 只在本地播放，不影响规则，也不走网络 | `FirstPersonRig`、`CargoItem`、`CargoPieces` |

改联网逻辑时，先想清楚这段代码在哪台电脑上跑。写 `NetworkVariable` 和判定规则都只在主机上做。

**客户端的时钟比主机略快。** 客户端觉得自己已经站起来了，请求到主机时主机可能还认为它没起来，就会拒绝。所以按住不放这种持续的状态，不能只在变化时发一次：`UpdateSwing` 发现主机的状态和自己想要的不一样，就隔 0.2 秒再发。这类问题在主机上跑测试查不出来，要看客户端的 `-ccDriveInput`。

**客户端看到的别人是 0.2 到 0.3 秒之前的位置。** 主机判定客户端的攻击时（锤子、喷气、重锤），按双方的移动速度多放宽一些距离（`PlayerAvatar.LagAllowance`，`GameConfig.lagCompensationSeconds`），否则客户端打正在跑的人总是打不中。主机和机器人看到的就是真实位置，不放宽。新加会打到人的东西，也要走这里。主机拒绝的攻击会记 `MISS` 日志。

**系统的键盘配置一变（比如切换输入法），Input System 会重新绑定所有动作**，按着的键会被取消，要松开再按才有反应。按住才有效的动作（`Attack`、`Secondary`）要打开 `initialStateCheck`，这样重新绑定后会重新读按键。只看按下那一下的动作（`Interact`、`Ram`）不要开，否则会多触发一次。

## 写代码的规则

- 角色不要直接读键盘，统一通过 `ICharacterInput`。这样机器人和自动测试才能用。人的 `Move` 是相对视线的，机器人的 `Move` 是世界方向（看 `LooksAround`）。
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

其他模型把脚本换成 `make_cargo.py`、`make_hands.py`、`make_truck.py`、`make_helicopter.py`、`make_scenery.py`，开始页面的 Logo、按钮、卡片图和比赛界面的图标用 `make_ui.py`。末尾加 `-- --sheet 文件夹` 可以同时渲染检查图（直升机是 `-- --preview 图片.png`）。运行之后，还要运行上面的生成脚本才会进入游戏。

场景的每个模型是一个 FBX，`make_scenery.py` 加 `-- --only Rock_A,Barn` 只重做这几个。生成脚本把它们做成 `Assets/Game/Prefabs/Scenery` 里的预制体，再摆进关卡（`CargoClashBuilder` 的 `AddTrees` 到 `AddOutskirts`）。模型本身没有碰撞，挡人的是单独的 `Blocker` 子物体，所以换模型不影响玩法和寻路。放进可玩区域的新东西，要么加 `Blocker`，要么矮到走过去也不奇怪（比如草和花）。

地面是画出来的贴图：`make_ground.py` 用同样的方式运行，写到 `Assets/Game/Textures`（末尾加 `-- --title` 只画开始场景用的那几张）。刹车痕和货车周围翻开的土画在贴图上，货车挪了位置要改脚本开头的 `TRUCK` 和 `TRUCK_YAW` 再运行一次。

直升机结尾的镜头从地图东南边往西看。高的东西不要放进 `OutskirtsIsFree` 排除的区域，否则会挡住这个镜头，或者挡住地图南端。

**场景物件不要勾 Static（Batching Static）。** 这台 Mac 上试过：静态合批和 SRP Batcher 一起用时，镜头转到某些位置，地面贴图和货车旁的黄色区域会整片变黑或消失。去掉静态合批就好了，原因没有查清。

货物的尺寸要和 `CargoClashBuilder.BuildCargo` 里的碰撞盒一致。货车的 `CargoBed` 是单独的物体，直升机吊走的就是它。

开始场景用的 `TitleTruck` 是同一辆车没出事的样子（`make_truck.py` 同时输出），由 `CargoClashBuilder.BuildTitleTruck` 组装，`Assets/CargoRover` 里的脚本带动它。轮子是单独的物体，轴距和轮距要和那些脚本一致。

别人看到的工具也在 `Character.fbx` 里（`Hammer`、`Vacuum`，绑在 `Hand.R` 上）。手里有工具时上半身层播 `ReadyHammer` 或 `ReadyVacuum`。

角色的每个动作是 `make_character.py` 里的一个函数，只写手脚放在哪里，手肘和膝盖由脚本算出来。新增一个动作要改三处：

1. `make_character.py`：写动作函数，加进 `CLIPS`。
2. `Assets/Game/Editor/CargoClashCharacterAnimator.cs`：加状态和过渡。循环的动作还要加进 `LoopingClips`。
3. `Assets/Game/Scripts/Player/CharacterVisual.cs`：从联网状态决定什么时候播。

Animator 有两层：`Base` 管全身，`UpperBody` 只管上半身，这样搬货和砸货时腿还能继续跑。

描边是 URP 渲染器上的 `ModelOutline` 功能，会描 Player、Cargo、Outlined、ViewModel 和 Debris 这些层上的模型。线条的屏幕宽度不变，太远的东西会变成一团黑线，所以 `Outline.shader` 让它随距离淡出（`_FadeStart`、`_FadeEnd`）。要描别的东西，把它放到 Outlined 层。方块这类有硬边的模型描出来会断开，要先换成圆角模型。场景模型里名字以 `Plain` 开头的部分（电线、广告牌文字）不描边，草、花和云也不描。

## 第一人称

默认是第一人称，按 V 切到肩后第三人称（`FollowCamera`，选择存在 `GameSettings`）。比赛时鼠标被锁住，打开暂停菜单或结算才放开（`MatchHud.UpdateCursor`）。自动测试不锁鼠标，也不保存设置。

**两台相机。** 主相机不画 `ViewModel` 层。它上面叠了一台 Overlay 相机，只画 `ViewModel` 层：手、锤子和自己手里的货。这台相机会先清深度，所以手和货不会插进墙里。自己手里的货由 `CargoItem.SetOnViewLayer` 换层。两台相机的视野一样，货从地上进到手里时才不会跳。

**手是代码摆的，不是 Animator。** `make_hands.py` 输出 `FirstPersonHands.fbx`，上臂、前臂、三种手型（放松、张开、握拳）、锤子和收集器都是不变形的物体，长度要和 `FirstPersonRig` 开头的常量一致。`FirstPersonRig` 每帧算它们的位置，因为手要跟着货物的大小走。要让动作看着协调，守住这几条：

- 手臂是两段的 IK（`PlaceArm`、`FindElbow`）：动作只说手腕放哪里，手肘和手的朝向是算出来的。
- 根节点是上半身，绕胸口转（`torso` 弹簧）。一个动作要带上身体，不要只动手。
- 跑步摆臂用 `FollowCamera.Stride`，它和落脚同步。想和步子合拍就用它，不要自己算相位。
- 工具是双手握的刚体：动的是工具（`HammerKeys`、`BlastKeys`、`SlamKeys`），两只手锁在把手上。挥锤和喷气跟着角色动画的进度（`CharacterVisual.SwingCycle`），所以手、声音和特效是同一时刻。改 `Smash` 的长度要同时改 `GameConfig.smashSwingSeconds` 和 `smashFirstHitSeconds`，`Blast` 对应 `blastCycleSeconds` 和 `blastFirstSeconds`，`Slam` 对应 `slamSeconds` 和 `slamLandSeconds`。
- 手里的货：手掌贴在两侧、手指朝前、拇指朝上（`HoldPoses`）。手的朝向不要跟着前臂，前臂从下面上来，手指会竖起来。拿货时手从下面迎上去，不要伸到地上，否则一条手臂会横穿画面。
- 收集器做大了会挡视线。它在 `make_hands.py` 里按 `VACUUM_SCALE` 缩小，`FirstPersonRig` 里的 `VacuumForeGrip`、`VacuumMouth` 是缩小后的值。
- 检视（F）是 `HammerInspect`、`VacuumInspect` 两张关键帧表，只在本地播放。`Flip` 和 `Twist` 可以超过一整圈。锤子在画面平面内转，翻跟头的话锤头会贴到镜头上。左手松开（`leftLetGo`）之后，握回工具的那个键不用写左手位置：手先停在原处，快到时伸回把手（`SampleInspect` 的返回值）。检查检视要看每一张连拍，有的问题只出现一帧。一按左键或右键检视就停，看的是本机的 `PlayerAvatar.WantsToUseTool`，不等主机确认，否则客户端要晚一个来回才停。
- 合搬时人站在箱子一端，走路时箱子总在身侧。看得到把手时手搭在上面，看不到时两只手朝箱子的方向伸，箱子在身后时手放到视野外（`CoCarryPoses`）。

看手的动作要用连拍（`-ccSwingBurst`），单张截图看不出协不协调。自己的身体只投影子（`ShadowsOnly`），别人看到的还是 Animator 动作。

**货物的损坏。** `make_cargo.py` 给每种货多输出两个文件：`_Damaged` 里是两个受损的箱体（`Body_D1`、`Body_D2`，按 `GameConfig.damagedBelow` 和 `wreckedBelow` 切换），`_Pieces` 里是碎掉之后的各个碎块，每块是一个物体，起始位置就在完整模型的对应位置。生成脚本给碎块加碰撞和刚体，放在 `Debris` 层，只和 `Level` 层碰撞（`GameBoot`）。

**界面。** 比赛界面在 `BuildHud`，用的是开始页面那套控件。小地图是 `MiniMap`，地图范围和底图在 `LevelLayout.mapArea`、`mapPicture`，做新关卡要填这两个。`MatchHud` 里的距离和大小都是屏幕上的固定值，改的时候在 1600×900 和别的窗口比例下都看一看。

**别把 SSAO 加回渲染器。** 它在手部相机上会变成一片噪点，而且慢。也不能只把它停用：停用后打包时它的资源会被剔除，游戏每帧报错并且什么都画不出来，这时 `-ccUncapped` 测出来的帧率还特别高，看起来像变快了。要用就先解决手部相机的问题。

## 开始页面和关卡列表

开始场景是 `Assets/CargoRover/Scenes/CargoRover.unity`，整个由 `CargoClashBuilder.BuildTitleScene` 生成。页面的跳转在 `TitleFlow`：标题 → 主菜单 → 选关 → 练习或大厅。加入别人的房间不选关，由主机决定。

**关卡列表**在 `GameConfig.levels`，由 `CargoClashBuilder.BuildLevelList` 写入。第 1 个是教学关（`Level01`），其余是占位：`scene` 为空的关卡，卡片是锁着的。开放一关要做三件事：

1. 在 `Assets/Game/Scenes` 做好场景（可以复制 `Level01.unity`，`LevelLayout` 和 `MatchHud` 都要有）。
2. 在 `BuildLevelList` 填上它的 `scene`，运行生成脚本，它会自动进 Build Settings。如果不想再运行生成脚本，就直接改 `GameConfig` 资源，并手动把场景加进 Build Settings。
3. 换掉卡片图。`Assets/Game/UI/LevelNN_Card.png`，928×500。占位图是 `make_ui.py` 画的，教学关那张是实机截图：窗口运行时加 `-ccHideUi -ccShotDir 目录` 拍下来，再裁成这个比例。

开局时主机把选的关卡放进 `SessionRoster.Level`，加载场景都从这里取，不要再写死场景名。

界面的颜色和做法集中在 `CargoClashBuilder.Title.cs` 的 `UiButton`、`UiPanel`、`UiText`。一个控件放在 `PopSlot` 里，弹入的动画缩放外层，鼠标悬停的动画缩放里层，这两个不能放在同一个物体上。图片要先在 `UiSprite` 里设好导入方式。所有字用 `Assets/Game/UI` 里的 Lilita One，它只有拉丁字母，以后要显示中文得另外配字体。

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

**无窗口运行不渲染，所以渲染的问题查不出来。** 改了渲染器、相机或材质，还要窗口运行一局（加 `-ccShotDir 目录`），`errors` 要是 0，并且看一眼截图。

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
| `-ccDriveInput` | 用真实的键盘和鼠标事件玩一局：转视角、走路、拿货送到货车、抡锤砸货、打人、击败和重生、吸货吸人、蓄力重锤、检视，并检查 V、方向键、Esc、灵敏度滑块，以及按住左键不会挥不动。最后要看到 `CHECK ok`，这是测 `HumanInput` 和按键绑定的唯一办法。两台机器都加这个参数，主机打客户端和客户端打主机就都测到了 |
| `-ccSwingBurst 秒` | 配合 `-ccDriveInput`、`-ccShotDir` 和 `-ccView first`：每半场按这个间隔连拍跑步、站立、挥击、检视、右键，拿货和合搬时也拍，用来看手的动作 |
| `-ccView first` / `third` | 这一局用第一人称或肩后视角，不保存。不加就用上次玩时存下的视角，截图时记得加 |
| `-ccEyeTour "x,z,yaw;x,z,yaw"` | 站在这些位置平视并截图，用来检查场景从地面看是什么样 |
| `-ccTour "x,z;x,z"` | 用老的高空镜头依次看这些位置并截图（要配合 `-ccShotDir`），用来检查场景的每个角落 |
| `-ccCameraZoom 倍数` | 把上面那个高空镜头拉近，比如 0.42 |
| `-ccUncapped` | 窗口运行时不限帧率，每 5 秒记一次帧率，用来比较两个版本哪个更卡。不同时间测的数可以差两成，要两个包同一时间交替跑 |

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
- 推送给组员看的版本，在 `CHANGELOG.md` 最上面加一节：改了什么、怎么试、还缺什么、有哪些提交。
- 模型、贴图、音频走 Git LFS，`.gitattributes` 已经配好。
- `Assets/Game/Scenes/Level01.unity` 同一时间只让一个人改。其他人在自己的测试场景里做功能，东西尽量做成预制体。
- 不要打开 `com.unity.pipeline` 的 `enableInReleaseBuilds`（默认是关的）。这个包只给本机 agent 跑编译和测试用，打开后发布的包会带一个能执行代码的本地 HTTP 服务。
- 报告要列现成素材：用了外部素材就记进 `ASSETS.md`。
