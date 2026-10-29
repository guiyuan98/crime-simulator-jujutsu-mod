# v1.5.5 测试记录

环境：Windows x64、Unity2022.3.62f3、BepInEx5.4.23.5、Fusion Single。

发布包使用已经验证的同一DLL，不用重新编译的不同PE覆盖已测试成品：
`SHA256: 39a9e0338b23028cc8057094face1a55710ec8aa014c5ec8e15b99309a2f5716`。

以下为隔离副本、真实初始化游戏对象的保留结果；输入由API调用，不冒充本轮人工物理键鼠实玩：

```text
JJK_LIVE MODIFIED_PASS id=modified cases=36
JJK_LIVE AVATAR_PASS id=modified cases=18
JJK_LIVE AVATAR_PASS id=modified cases=18
JJK_LIVE MODEL_PASS id=modified cases=10
JJK_LIVE BREACH_PASS id=modified cases=38
JJK_LIVE SECURITY_PASS id=modified cases=54
JJK_LIVE RANGED_PASS id=modified cases=44
JJK_LIVE PLAYER_VIEW_PASS id=modified cases=33
```

共8组251项原生断言，另有训练场八技能检查：

- 防护/NPC/五条悟技能/音频36：常规身体伤害、电击直接死亡旁路、护甲、直接字段、无下限、足额/不足/耗尽咒力、暂停和玩家缓存缺失。
- 模型10、角色生命周期18+18：61身体骨骼、第一人称手部、原生持物、死亡布娃娃重定向、停用/重启/切图恢复。
- 任务/战利品/门/保险箱38：真实已接合同、双扇门与抽屉、机械/电子保险箱、拾取、原生权威账本一次奖励及磁盘重载。奖励检查读取原生currentQuotaSecured，不把延迟更新的显示值当权威值。
- 安防54：实际门禁、摄像头、绊线、卷帘/车库门等对象及对应合同。原生静默任务遇破坏仍会失败；不直接伪造任务进度。静态地图原生重置规则保留。
- 远程模型44：原生弩、电击枪、消音手枪可见性、弩原生换弹、两角色各11种原生手型及还原。不是所有武器射击/弹药路径的穷举。
- 玩家相机33：直接使用原生myCamera，控制器保持启用；八技能early/late帧和实际释放计数。代表图已打开检查，不复用可能穿入本地身体的确定性测试代理相机作为第一人称证据。

10个语音槽来源已核对：7个直接短裁剪、3个波形匹配，来源和WAV哈希写入嵌入来源表。4术式名+6角色短句，不是10个独立术式喊招。

原生测试保护了原始非日志存档及注册表；四个游戏基线文件哈希保持不变。独立副本回滚还原旧版，正式安装仍为1.5.5。发布流程另检查公开源码重建、规则测试、资源匹配及匿名下载哈希。

截图中的模型为风格化制作，不将主观“完美”作为自动测试结论。原游戏脚本强制死亡、睡眠等非攻击规则保留；未声称未来所有游戏补丁、地图、任务或攻击组合均已穷举。多人协作不是本版本验证目标。

docs/images中旧hud-day/hud-domain/fuga-charge/shrine图是1.4.0训练场历史图；新native-*为1.5.5原生玩家相机，*-model为原生骨骼模型检查相机。
