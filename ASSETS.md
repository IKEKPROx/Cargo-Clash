# 现成素材清单

课程报告要列出用到的所有现成素材。用了外部素材就在这里加一行。

| 素材 | 来源 | 授权 | 用在哪里 |
|---|---|---|---|
| Blender 默认字体 | Blender 5.2 自带 | 开放字体授权，见 Blender 安装包里的 `license.md`。具体是哪一款字体还没有确认，写报告前要查 | 两块广告牌上的文字，已转成模型 |
| 字体 Lilita One | Google Fonts（[google/fonts](https://github.com/google/fonts) 的 `ofl/lilitaone`），作者 Juan Montoreano | SIL Open Font License 1.1，全文在 `Assets/Game/UI/LilitaOne-OFL.txt` | 开始页面的所有文字，Logo 的字母形状 |

除此之外没有用外部素材：模型由脚本生成（角色、货物、货车、直升机和场景来自 `Tools/blender`），地面贴图由 `Tools/blender/make_ground.py` 画出来，开始页面的 Logo、按钮和卡片图由 `Tools/blender/make_ui.py` 生成，音效和音乐由 `Tools/generate_audio.py` 合成。带动开始页面货车和地面的脚本来自队友最早提交的 zip（`Assets/CargoRover`），它的来源要问提交的人。
