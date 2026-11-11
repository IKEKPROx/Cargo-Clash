# 现成素材清单

课程报告要列出用到的所有现成素材。用了外部素材就在这里加一行。

| 素材 | 来源 | 授权 | 用在哪里 |
|---|---|---|---|
| Blender 默认字体 | Blender 5.2 自带 | 开放字体授权，见 Blender 安装包里的 `license.md`。具体是哪一款字体还没有确认，写报告前要查 | 两块广告牌上的文字，已转成模型 |
| 字体 Lilita One | Google Fonts（[google/fonts](https://github.com/google/fonts) 的 `ofl/lilitaone`），作者 Juan Montoreano | SIL Open Font License 1.1，全文在 `Assets/Game/UI/LilitaOne-OFL.txt` | 开始页面的所有文字，Logo 的字母形状 |

除此之外没有用外部素材：人物（包括 CREW 页上能选的帽子、眼镜和捏脸）、锤子和收集器是组员 Astra 在 Blender 里做的（原件在仓库外的 `独立建模/`，`Tools/blender/make_crew.py` 和 `make_hands.py` 减面后放进游戏），货车、车斗和直升机也是她做的（`独立建模/运输载具_v07/`，她导出的减面版原样放进 `Assets/Game/Models`，直升机吊运的动作也按她的方案），其他模型由脚本生成（货物、交通锥和场景来自 `Tools/blender`），地面贴图由 `Tools/blender/make_ground.py` 画出来，开始页面的 Logo、按钮和卡片图由 `Tools/blender/make_ui.py` 生成，音效和音乐由 `Tools/generate_audio.py` 合成。带动开始页面货车和地面的脚本来自队友最早提交的 zip（`Assets/CargoRover`），它的来源要问提交的人。
