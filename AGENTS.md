# Cargo Clash 工程规则

给在这个仓库里工作的 agent 和人看。玩法规则以设计文档为准，这里只写做工程时要遵守的事。

## 技术栈

- Unity **6000.6.8f1**，URP，Input System。不要升级版本。
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

改联网逻辑时，先想清楚这段代码在哪台电脑上跑。写 `NetworkVariable` 和判定规则都只在主机上做。

## 写代码的规则

- 角色不要直接读键盘，统一通过 `ICharacterInput`。这样机器人和自动测试才能用。
- 数值放 `GameConfig` 和 `CargoDefinition`，不要写死在代码里。玩家看到的文字放 `GameText`。
- 特效和音效不能影响规则，它们只在收到状态变化或 RPC 后播放。
- 所有联网物体都是从预制体动态生成的，场景里不放 NetworkObject。新增的联网预制体要在 `RoomService.Awake` 里注册。
- 不要在 Finder 里移动 `Assets` 下的文件，要带上 `.meta` 或在 Unity 里移动。

## 生成脚本

`Tools > Cargo Clash > Build Game Content`（`Assets/Game/Editor/CargoClashBuilder.cs`）生成了初版的预制体、数据、第 1 关场景和界面。**手工改过这些资源后再运行它，改动会被覆盖。** 改了资源之后，就直接在 Unity 里继续改，不要再运行它。

## 怎么验证

改完代码，至少编译通过并跑一局机器人对局。先退出打开这个工程的 Unity，再运行：

```bash
"/Applications/Unity/Hub/Editor/6000.6.8f1/Unity.app/Contents/MacOS/Unity" -batchmode -quit -projectPath . -executeMethod CargoClashPlayerBuild.Mac -logFile /tmp/cargo-build.log
```

```bash
"Builds/Mac/CargoClash.app/Contents/MacOS/Cargo Clash" -batchmode -nographics -ccMode practice -ccBotLocal -ccHalfSeconds 45 -ccTimeout 200 -ccQuit -logFile /tmp/cargo-match.log
```

```bash
grep CCTEST /tmp/cargo-match.log
```

结果要看到 `RESULT` 和 `DONE errors=0 timedOut=False`。

`AutoPilot` 的其他参数：

| 参数 | 作用 |
|---|---|
| `-ccMode host` / `client` | 开局域网主机 / 加入，配合 `-ccAddress`、`-ccExpectClients` 测两台机器 |
| `-ccShotDir 目录`、`-ccShotEvery 秒` | 窗口运行时定时截图，用来检查画面 |
| `-ccSmashScale 倍数` | 临时改砸货时间，用来比较平衡 |
| `-ccPort 端口` | 同时跑多局时避开端口冲突 |

agent 只能验证规则和流程。手感和两人联机必须由人来测：在 Multiplayer Play Mode 里开两个玩家，确认两边都看到倒地、货掉在同一个位置、冷却时间一致，再合并。

## 协作

- 每个功能开一个短分支，走 PR 合并到 `main`，合并前自己先跑一遍。
- 模型、贴图、音频走 Git LFS，`.gitattributes` 已经配好。
- `Assets/Game/Scenes/Level01.unity` 同一时间只让一个人改。其他人在自己的测试场景里做功能，东西尽量做成预制体。
- 正式包要关掉 `com.unity.pipeline` 的 `enableInReleaseBuilds`。这个包只给本机 agent 跑编译和测试用。
- 报告要列现成素材：用了外部素材就记进 `ASSETS.md`。
