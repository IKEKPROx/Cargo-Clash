# Cargo Clash

最多 6 人的联网对抗游戏，COMP4122 课程项目。货车翻车后，正方把散落的货物搬回车上，反方把货砸掉，比谁抢救或毁掉的货物价值更高。每关两个半场，中场换边；教学关与藤坠石庭每半场 1 分钟，河流关每半场 3 分钟。时间到时直升机赶来，把车上的货箱吊走。

本分支开放教学关（TUTORIAL）、河流关（RIVER FLATS）和藤坠石庭 V10（RUIN COURTYARD），可以与机器人练习或联网对战，其余卡片保留为占位。V10 更新现用货物、中央可破坏建筑、双坑石阶、出生标识与环绕车厢的投放回收区，见[地图与交互说明](Docs/RuinCourtyardV10.md)及[实际验证](Docs/verification/mac-v10/README.md)。原 [Mac 整合记录](Docs/MacRuinV09.md)保留。

每一版改了什么、还缺什么，见 `CHANGELOG.md`。

## 打开工程

1. 安装 Git LFS（只需一次）：`brew install git-lfs`，然后运行 `git lfs install`。Windows 从 [git-lfs.com](https://git-lfs.com) 安装。
2. 克隆仓库：`git clone https://github.com/IKEKPROx/Cargo-Clash.git`
3. 用 Unity Hub 安装 **6000.6.1f1**：打开 `unityhub://6000.6.1f1/7efac9f6c10e`。Mac 上要勾选 Windows Build Support (Mono)，才能出 Windows 包。
4. 在 Unity Hub 里添加并打开工程，打开场景 `Assets/CargoRover/Scenes/CargoRover.unity`，按 Play。

全组用同一个 Unity 版本，不要各自升级。

## 操作

| 按键 | 作用 |
|---|---|
| WASD | 移动 |
| 鼠标 | 转视角（方向键也能转） |
| E | 拿起 / 放下货物 |
| 空格 | 冲撞 |
| 左键 | 用手里的工具打人：一下推开，连打两下打倒。收集方喷气，距离远；毁坏方抡锤，对着货按住还能砸货 |
| 右键 | 收集方按住吸货吸人；毁坏方按住蓄力，松手重砸 |
| F | 检视手里的工具（第一人称） |
| V | 切换第一人称和肩后视角 |
| Esc | 暂停菜单，可以在这里调转视角的速度 |

手柄也能用：左摇杆移动，右摇杆转视角，A / B / X / Y 对应拿放、冲撞、攻击、检视，右扳机对应右键。

## 怎么测试

- **单人练习**　进入主菜单后点「和机器人练习」，选关、选角色，再点「开始」。第 5 张卡片是「藤坠石庭」。
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
