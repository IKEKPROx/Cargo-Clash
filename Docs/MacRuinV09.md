# Mac 版整合与运行验证

2026-11-16，分支 `codex/mac-ruin-v09`。以桌面旧包对应的 `origin/Gattuso`（`e6c9c3d`，第 14–17 步）为基础，合并遗迹 V09 `3624008`。Unity **6000.6.1f1**，目标 **macOS ARM64**、Mono。未更改 Unity、URP 或包依赖版本，未运行 Build Game Content。

## 整合内容

新 Mac 包同时开放 TUTORIAL、RIVER FLATS 和 RUIN COURTYARD，保留桌面版本的四种 Astra 货物、检视、河流规则、中文与设置页。原教学关、河流关和 CargoRover 场景内容保留；仅为遗迹关复制最新的独立 HUD 根节点，接入设置页和本地化引用。

遗迹关独立引用原三种箱子，按 8×10、5×30、2×100 放置，共 15 箱、430 分，保留已检查的原箱体尺寸和抓取方向。`CargoKind.Legacy=255` 明确使用旧箱子的显示分支，其他关卡继续使用 Astra 四种货物。主机注册这三种额外网络预制体。

河流关的合搬继续跟随河岸、涉水和把手高度；遗迹关保留旋转轴向、真实角色扫掠、两人脚下支撑检查及落坑复位。遗迹交互提示增加中文。

## 实际发现与修复

- V09 材质在片元阶段从 `SV_POSITION.z` 计算雾，使近处场景被洗成蓝白色。改为顶点阶段计算并插值雾因子，保留屏幕坐标用于光照；已查看 Metal 窗口中修复后的货仓、坑沿和正常第一人称画面。
- 文件提供程序附加的 Finder 信息导致换图标后签名失败。Mac 构建仅移除 `com.apple.FinderInfo`、`com.apple.ResourceFork` 后重新签名，不移除 quarantine/provenance。
- 打包时在临时目录清理并验证副本，避免同步目录再次附加元数据。正式应用安装在 `/Applications/CargoClash_RuinV09.app`，桌面提供启动入口与独立 ZIP；原桌面应用保留。
- `RuinMacIntegration.Prepare` 仅更新遗迹关 HUD，不重建共享场景或资源。

## 实际运行结果

编辑器及支持文件安装在 `/Applications/Unity/Hub/Editor/6000.6.1f1/`，Unity Personal 已确认激活。

| 检查 | 结果 |
| --- | --- |
| Unity 准备与编译 | 退出码 0；HUD、四个构建场景、10/30/100 引用检查通过 |
| 最终 Mac 构建 | `StandaloneOSX Succeeded errors=0 warnings=4 size=644MB`，图标及签名成功，退出码 0 |
| 教学关完整机器人对局、强制合搬 | `DONE errors=0 timedOut=False` |
| 河流关完整机器人对局、强制合搬 | `DONE errors=0 timedOut=False`，每半场物资 1530 分 |
| 遗迹关完整机器人对局 | `DONE errors=0 timedOut=False`，每半场物资 430 分 |
| 遗迹专项 | 32 项主机断言通过：门、占位拒绝、R1 破坏与复原、投藤减速/到期、藤梯、托举与拉人、落坑复位与计分、合搬坑沿支撑 |
| 中文菜单和 Metal 窗口运行 | 通过实际菜单进入，完成两半场、直升机和结算；`DONE errors=0 timedOut=False` |
| 同机双进程主机/客户端 | 双方 `DONE errors=0 timedOut=False`，结算均为 orange=230、blue=380 |
| 客户端交互复制 | 门、破坏墙、减速区、藤梯和两人合搬状态的 3 项断言通过 |

测试半场覆盖值只作用于测试进程，未改正式关卡时长。窗口测试为 1600×900、解除帧率限制；采样仅代表此 Mac 和当时视角，不是其他设备的性能结论。未把无窗口测试当作渲染验证。

复现示例（仓库根目录）：

```bash
"/Applications/Unity/Hub/Editor/6000.6.1f1/Unity.app/Contents/MacOS/Unity" -batchmode -quit -projectPath "$PWD" -executeMethod CargoClashPlayerBuild.Mac -logFile "$PWD/Builds/Verification/build.log"
"Builds/Mac/CargoClash.app/Contents/MacOS/Cargo Clash" -batchmode -nographics -ccMode practice -ccLevel 4 -ccRuinTest -ccBotLocal -ccHalfSeconds 60 -ccTimeout 220 -ccQuit -logFile "$PWD/Builds/Verification/ruin.log"
```

双进程测试分别使用 `-ccMode host -ccExpectClients 2` 与 `-ccMode client -ccAddress 127.0.0.1`，两边传相同 `-ccPort`、`-ccLevel 4` 和 `-ccRuinTest`。客户端专项期间暂停自己的测试机器人，避免其他送货动作干扰计分隔离断言。

完整日志与截图在本机 `Builds/Verification/`；[精简记录和截图](verification/mac-v09/) 随源码保存。真人双机局域网手感、Windows 构建和 Windows 性能尚未验证。
