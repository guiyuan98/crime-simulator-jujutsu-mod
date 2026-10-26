# v1.4.0 测试记录

环境：Windows x64、Unity 2022.3.62f3、BepInEx 5.4.23.5。

以下为真实 Unity 引擎测试副本中的输出：

```text
[Info   :Jujutsu Local] JJK_BINDING_TEST F=0 G=1 V=2 X=3 oldDigitsRejected=True nativeReserved=3 nativeRestored=True
[Info   :Jujutsu Local] JJK_HUD_LAYOUT size=1280x720 textFits=True
[Info   :Jujutsu Local] JJK_HUD_LAYOUT size=1920x1080 textFits=True
[Info   :Jujutsu Local] JJK_HUD_LAYOUT size=1280x720 textFits=True
[Info   :Jujutsu Local] JJK_HUD_LAYOUT size=1024x768 textFits=True
[Info   :Jujutsu Local] JJK_HUD_VISIBILITY hidden=True restored=True
[Info   :Jujutsu Local] JJK_HUD_TEST nativeCanvas=True labels=True panel=272x238 topBanner=False resolutions=1280x720,1920x1080,1024x768
[Info   :Jujutsu Local] JJK_SELFTEST PASS casts=8 skills=8 input=True damageRouting=True infinity=True off=True heal=True hits=1 damage10x=True vfx=True movement=True cleanup=True errors=0 run=hud-update-modified-20261026
```

覆盖八技能输入路由、规则运算、真实Rewired映射临时保留/恢复、原生Canvas面板、隐藏/恢复及资源清理。输入通过真实技能调度器自动调用，不将其表述为新的人类物理按键实测。

HUD截图来自实际Unity UI和相机渲染，测试了1280×720、1920×1080、1024×768，以及领域展开状态；截图是训练场，不冒充原地图实测。未声称普适兼容或FPS提升。

本地规则测试随 `Build.ps1` 执行。发布包中的DLL与本地通过引擎回归的v1.4.0文件字节一致；SHA-256见Release附件。
