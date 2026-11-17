# Mac V10 实际验证

2026-11-17，Unity **6000.6.1f1**，macOS ARM64、Mono；代码和模型提交 `3279e90`。最终构建使用 `RuinCargoValidation.BuildMac`，未运行内容重建器。

| 实际执行 | 结果 |
| --- | --- |
| Unity 编译及 Mac 构建 | 成功，0 errors、4 warnings，631 MB，图标和签名成功 |
| 中文 Metal 窗口主机、完整两半场 | `DONE errors=0 timedOut=False`，96 项检查通过 |
| 同机独立客户端、完整两半场 | `DONE errors=0 timedOut=False`，35 项检查通过 |
| 双方结算一致 | orange=240、blue=350 |
| 教学关机器人回归、强制合搬 | `DONE errors=0 timedOut=False`，180／515 |
| 河流关机器人回归、强制合搬 | `DONE errors=0 timedOut=False`，620／640 |
| Unity 资产元数据 | 778 个 GUID 唯一，无缺失 `.meta` |

主机专项验证了四种仓库现用货物网格、15 件／430 分和实际落地尺寸，双方出生标记与无阻挡集合牌，门和占位拒绝，破坏／复原，藤蔓减速、到期、藤梯与合作托举。两个坑分别实际通过 CharacterController 携带 10 货物上楼，并通过正常双人输入合搬 100 箱走完每级石阶；未在路线中逐点传送。

回收专项检查四边内外及高度边界、初始地面货物不自动计分，以及搬运方站在圈外正常拾取和放手：货物从区域外飞入，获得 10 分，保持在区内也不重复记分。客户端观察到门、破坏墙、藤蔓和两人合搬的同步状态。测试半场时间为 120 秒；正式关卡仍为 60 秒。

窗口截图实际查看了两处坑内石阶、中英标牌的中文显示、100 鹅箱近景和回收圈全貌。这里只验证此 Mac 的实际渲染，没有把无窗口测试当作渲染验证。玩家个人窗口设置在测试前备份、测试后还原。

## 复现

在工程根目录编译：

```bash
"/Applications/Unity/Hub/Editor/6000.6.1f1/Unity.app/Contents/MacOS/Unity" -batchmode -quit -projectPath "$PWD" -executeMethod RuinCargoValidation.BuildMac -logFile "$PWD/Builds/Verification/unity-build-v10-final.log"
```

主机窗口与客户端分开启动，端口一致：

```bash
"Builds/Mac/CargoClash.app/Contents/MacOS/Cargo Clash" -ccMode host -ccExpectClients 2 -ccPort 7784 -ccLevel 4 -ccRuinTest -ccBotLocal -ccLanguage zh -ccHalfSeconds 120 -ccTimeout 380 -ccQuit -ccShotDir "$PWD/Builds/Verification/ruin-v10-window" -logFile "$PWD/Builds/Verification/ruin-v10-host.log"
"Builds/Mac/CargoClash.app/Contents/MacOS/Cargo Clash" -batchmode -nographics -ccMode client -ccAddress 127.0.0.1 -ccPort 7784 -ccLevel 4 -ccRuinTest -ccBotLocal -ccHalfSeconds 120 -ccTimeout 380 -ccQuit -logFile "$PWD/Builds/Verification/ruin-v10-client.log"
```

本目录保存测试日志摘录、输入摘要与原始实机截图。完整日志、Blender 详细检查和构建产物保存在本机 `Builds/Verification/`、`Builds/Deliverables/`。真人双机局域网手感、网络延迟下的合搬以及 Windows 未在本轮验证。

交付包 `CargoClash_Mac_RuinV10_2026-11-17.zip` 解压后再次通过 `codesign --verify --deep --strict`，可执行文件确认 ARM64。SHA-256：`1677bf75d1335a49c5e474d3f802a50646ffe4923bb97ca2afa8a644221de18d`。本机应用安装为 `/Applications/CargoClash_RuinV10.app`，桌面提供同名启动入口及 ZIP。

![环绕车厢的回收范围](truck-recovery.png)

![遗迹中的现用 100 鹅箱](cargo-100.png)

![坑内石阶](pit-stairs.png)

![双方出生位的标记](interrupter-spawn.png)
