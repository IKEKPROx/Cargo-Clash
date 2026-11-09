# Cargo Clash

最多 6 人的联网对抗游戏，COMP4122 课程项目。货车翻车后，正方把散落的货物搬回车上，反方把货砸掉，比谁抢救或毁掉的货物价值更高。每关两个半场，每个半场 1 分钟，中场换边。时间到时直升机赶来，把车上的货箱吊走。

现在的版本只有教学关能玩，可以和机器人练习，也可以联网对战。选关页预留了另外 5 个关卡的位置，它们还是锁着的占位。

## 打开工程

1. 安装 Git LFS（只需一次）：`brew install git-lfs`，然后运行 `git lfs install`。Windows 从 [git-lfs.com](https://git-lfs.com) 安装。
2. 克隆仓库：`git clone https://github.com/IKEKPROx/Cargo-Clash.git`
3. 用 Unity Hub 安装 **6000.6.1f1**：打开 `unityhub://6000.6.1f1/7efac9f6c10e`。Mac 上要勾选 Windows Build Support (Mono)，才能出 Windows 包。
4. 在 Unity Hub 里添加并打开工程，打开场景 `Assets/CargoRover/Scenes/CargoRover.unity`，按 Play。

全组用同一个 Unity 版本，不要各自升级。

## 操作

| 按键 | 作用 |
|---|---|
| WASD 或方向键 | 移动 |
| E | 拿起 / 放下货物 |
| 空格 | 冲撞 |
| 按住 F | 砸货（只有反方能砸） |

手柄也能用：左摇杆移动，A / B / X 对应拿放、冲撞、砸货。

## 怎么测试

- **一个人：** 开始页面点 PLAY，再点 PRACTICE WITH BOTS，再选 TUTORIAL。不需要联网。
- **一台电脑开多个玩家：** Window > Multiplayer > Multiplayer Play Mode，勾选 Player 2。
- **局域网：** 一方点 CREATE ROOM 并选关。工程还没连 Unity Cloud 时，房间码就是主机的 IP，另一方输入它点 JOIN。
- **自动测试：** 见 `AGENTS.md`。

## 文件在哪里

| 位置 | 内容 |
|---|---|
| `Assets/Game/Scripts` | 游戏代码，按 Net、Match、Player、Cargo、Bots、UI、Fx、Core 分文件夹 |
| `Assets/Game/Data`、`Assets/Game/Resources/GameConfig.asset` | 全部数值。调数值改这里，不用改代码 |
| `Assets/Game/Scenes/Level01.unity` | 教学关场景。`Layout` 下的空物体决定货物和出生点的位置 |
| `Assets/Game/Prefabs` | 角色、货物、撞毁的货车。`Scenery` 里是树、围栏这些场景物件，改一个预制体，关卡里所有同样的物件都会变 |
| `Assets/Game/Editor/CargoClashBuilder.cs`、`CargoClashBuilder.Title.cs` | 初版内容的生成脚本。后者是开始页面和关卡列表 |
| `Assets/Game/Models`、`Tools/blender` | Blender 脚本生成的模型（角色、货物、货车、直升机、场景）和生成它们的脚本，角色的动作也在脚本里 |
| `Assets/Game/Textures` | 地面贴图，由 `Tools/blender/make_ground.py` 画出来 |
| `Assets/Game/UI` | 开始页面的 Logo、按钮、关卡卡片图和字体，由 `Tools/blender/make_ui.py` 生成 |
| `Assets/Game/Animation` | 角色的 Animator，由生成脚本创建 |
| `Assets/CargoRover` | 开始场景，以及带动货车和地面的脚本 |
| `Tools/generate_audio.py` | 生成占位音效和音乐 |
| `Tools/package_windows.sh`、`Tools/windows_readme.txt` | 把 Windows 包打成 zip，以及放进去的说明文件。步骤见 `AGENTS.md` |

设计细节见设计文档。
