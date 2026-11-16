# V09 美术接入验证

提交基线为 `a7e154ef9b74c6dd43f8ab3760aaeb37a5cac2d9`，Unity 6000.6.1f1。验证使用独立仓库副本，未运行资源生成器。

## 既有美术检查

`fbx-interface.json` 和 `unity-art-validation.txt` 是已确认的 V09 交付资产检查，分别通过 748 和 744 项。Unity 图片来自独立美术预览，不是多人对局截图。`model-statistics.json` 包含所有 LOD 和破坏状态的资产规模。

## 最新分支接入

最新关卡代码及布局保持 `a7e154e` 的内容，包括藤梯出口 X=70.90。V09 四个模型、图集、材质、shader 和导入器的内容与已确认交付一致；原资源 GUID 保留。

Unity 6000.6.1f1 Mac Player 构建通过：`StandaloneOSX Succeeded errors=0 warnings=3`。构建调用 `CargoClashPlayerBuild.Mac`，未调用 Build Game Content。机器人对局已启动，但本次未取得完整对局的 `RESULT` / `DONE` 通过记录；两人联机与目标 Windows 设备帧率尚未验证。
