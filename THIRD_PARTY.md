# 依赖与素材

- 游戏：Crime Simulator；构建时引用本机游戏程序集，仓库和发布包不分发游戏文件。
- BepInEx 5 与 Harmony/HarmonyX：由用户安装的加载器提供，不随本仓库分发。上游项目：[BepInEx](https://github.com/BepInEx/BepInEx)、[HarmonyX](https://github.com/BepInEx/HarmonyX)。
- `assets/slash.png`：由用户提供的斩击短视频提取为23帧透明图集；不附带原始下载链接或签名参数。
- `assets/shrine-wood.png` 与 `assets/fuga-flame.png`：图像工具生成的纹理，已实际用于运行时材质。
- 本项目未授予原作名称、角色或第三方素材的额外权利；仓库暂未选择开源许可证。

- 新增`gojo-plasma-wisp.png`及双角色脸部材质由图像工具生成，实际绑定运行时网格；人体/手部原生网格仅在用户本机游戏中取得并适配，不发布游戏网格文件。
- `assets/audio/`含10个原片短台词槽及7个短音效；来源页、片段区间、哈希和台词类型见`bank-manifest.json`。语音来自可定位的TOHO原片公开片段，不含完整视频、长音轨、合成仿声或词语拼接。两项五条悟技能有意复用同一完整短句，不声称10个独立术式喊招。
- 来源表中的`primary_media_file`是制作时的相对证据索引；原视频与原始识别文本未上传。来源验证方法与阈值保留在表中，不将其说成录音棚母带。
