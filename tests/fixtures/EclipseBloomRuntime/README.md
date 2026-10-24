# 蚀日花冠运行时接线执行回归

入口：`python tools/run_runtime_regressions.py --filter EclipseBloomRuntime`。

同一中央入口构建三个独立执行对象。战斗工程直接链接完整生产 `EclipseBloomAbilityController.cs`、`EclipseBloomPetal.cs`、`EclipseBloomBoss.cs`、`EclipseBloomRuntimeModule.cs`、`EclipseBloomEncounterPlacement.cs`、`EclipseBloomPoolAdapter.cs`、`EclipseBloomGroundProjection.cs`、`EclipseBloomRules.cs` 和 `CharacterOnDeadPatch.cs`。另链接只读官方 `DamageReceiver.cs` / `HealthSimpleBase.cs`，后者副本只加 `Random` 类型别名，官方方法体不变。

真实战斗路径覆盖：失活装配后第一次激活的 `Awake`、官方 `Hurt → Dead` 花瓣破坏、沿瓣 Z 轴胶囊、即时撤伤、核心接收体绑定、旧命中体关闭恢复、出场期间核心可伤、官方死亡前缀恢复底模身份并跳过四类额外掉落、暂停、有限高度、墙体与阵营门控、无可见地面预警不判伤、休整期追近、护甲修正 owner、切图 / 令牌失效清理、正常 / 倒序 / 重复死亡事件、入箱失败后的落地兜底、出生资源失败与部分花瓣失败回收。真实地面采样类覆盖每帧 96 次预算、同帧去重、未采样、坡面插值、孔洞、跨楼层、非有限高度、移动重采和实际三角形内的高度判定。

自然遭遇覆盖普通 37 主 / 子场景的加载顺序、官方生成后的主场景父节点解除与目标场景绑定、重复 callback、活动场景切换、同 owner 临时 additive 加载、真正卸载销毁、模式和待处理模式入场互斥、引导门控、单次出击仅一次成功生成、失败可重试、旧异步结果晚于后继到达、无竞技场路牌和交互。池中遭遇覆盖目录幂等、E / F 阵营模式拒绝、并发实例、波次与非波次身份、独立取消和死亡奖励；池中击杀不能通过原生实例身份核验推进引导。

引导工程直接链接完整生产 `EclipseBloomIntroduction.cs`、`EclipseBloomIntroductionPersistence.cs`、`OfficialQuestBinding.cs`、`BossRushSlotJsonStore.cs`、`BossRushSaveCoordinatorEngine.cs`、`BossRushJsonValue.cs` 和 `SimpleJsonHelper.cs`。实际通过绑定的 Accept / BeginDelivery / Deliver / EndDelivery 执行接取、普通 37 击败与回 Jeff 交付；覆盖不收走盾片、不追加奖励、事务内不保存、完成后仍可生成、同帧死亡放弃、不自动重接、Store 拒写恢复、物理保存失败重试、换槽隔离、未知 schema 与矛盾事实闭锁、地图圈场景归属、缓存读取和事件幂等退订。

边界替身：Unity 对象、组件、首次激活 / Start 和销毁（含 destroyed-as-null 与 GameObject 连带销毁组件）、场景、物理遮挡、角色属性、背包、官方角色生成异步边界、音频、图鉴和外形 `EclipseBloomBody`。运行时注入字段保持未赋值默认空，官方 `HealthSimpleBase.Awake` 在生产完成接线并激活后才执行。角色替身在官方死亡计数边界前调用真实前缀，没有实际装载 Harmony。背包替身只记录生产转交次数；异步替身使用可手动完成的角色生成与逐帧 `Yield`。引导工程替换 Unity、官方任务投影以及 Saves 的内存 IO，保留真实状态机、JSON 和共享保存协调器；没有访问任何玩家存档。

程序化草稿工具的几何回归另直接链接 `EclipseBloomVisuals_Geometry.cs` 与 `ResidualSunShieldModelGeometry.cs`，检查有限值、非退化面、焊接后闭合、成对反向边、各连通分量正体积和盾片最低点。正式模型来自 Blender 作者工程和 Unity bundle；本项仅证明保留的草稿生成工具，不证明正式 bundle 内容或游戏画面。

资产工程直接链接 `EclipseBloomAuthoredAssets.cs`、`EclipseBloomAssetManager.cs`、完整 `EclipseBloomVisuals.cs`、`ResidualSunShieldModel.cs`、`ResidualSunShieldIcon.cs` 和 `EquipmentFactory_RuntimeModels.cs`，并链接真实规则、地面投影与扇区生成工具。租约用例覆盖按需首次加载、并发共享、各实例动态网格独立销毁、最后 owner 卸载、重复释放、缺文件 / 网格 / 材质 / shader / 不支持 shader、部分失败卸载与恢复重试。真实 SectorVisual.Apply / CoversPoint 覆盖投影后的实际三角面，以及 fill renderer 禁用、fog 强制关闭、父节点隐藏和恢复；盾片测试执行专用 bundle 请求、原生 agent 检查、缺 GroundPoint 或不足六个 MeshFilter 时拒绝模板、完整六部件（含失活子节点）通过、ItemGraphic / EquipmentModel 注册、关闭平面拾取和借用模板缓存重置。工厂资源查询和图标测试覆盖按 bundle 身份借用、复用、缺资产或类型不符拒绝、排除旧通用图标、512 尺寸、错误尺寸恢复与销毁组件不销毁借用的 Sprite / texture / bundle。渲染、装备工厂加载和 bundle IO 是记录替身，正式 bundle 的二进制内容另由资源守卫检查。

反向验证：`python tests/fixtures/EclipseBloomRuntime/negative_probes.py`、`python tests/fixtures/EclipseBloomRuntime/introduction_negative_probes.py` 与 `python tests/fixtures/EclipseBloomRuntime/asset_negative_probes.py`。在 `Build/runtime-regressions/` 的独立副本中破坏网格封闭和绕向、战斗清理、奖励幂等、模式互斥、自然名额、场景归属、晚到异步结果、地面预算和跨层拒绝、预警伤害门控、暂停、死亡放弃、交付事务、槽位 owner、格式闭锁、事件退订与资源租约，要求在各自行为断言上转红，且核对共享生产文件 SHA-256 未变。

提交前等待帧专测：工厂完成角色创建后、生产 `UniTask.Yield` 尚未返回时，断言角色失活；模拟其它系统把 Health 置死，恢复该帧后要求回收 Boss / preset、拒绝提交死亡实例、不遗留奖励和战斗对象，再由自然生成重试提交存活后继。

回归提供 L2 证据，不模拟真实 Unity 武器查询、导航、完整官方尸体箱、渲染、性能或玩家操作。真实射击能否命中花瓣/核心、扇区视觉可读性与难度仍需 owner 游戏内验收。

最终 bundle 另由 `tests/EclipseBloomAuthoredBundleGuard.py` 实读验证；运行 `python tests/fixtures/EclipseBloomRuntime/authored_bundle_negative_probes.py` 在独立副本破坏真实包/清单/图标，7项预期失败，原资源SHA不变。此项不是宿主替身或草稿几何证明。

专属音乐用例直接执行生产 Controller 的起播与收尾：记录独立 EclipseBloom key、并发实例 owner、重复取消/销毁只释放一次、死亡、令牌失效和池直接销毁。音频仍为记录替身，不证明 FMOD 播放或听感。`python tests/fixtures/EclipseBloomRuntime/music_negative_probes.py` 在独立副本破坏起播 key、释放 owner、OnDestroy 收尾和曲目数据，共 7 项预期失败；验证工作区生产 SHA 不变。音频实际 PCM 的响度、静音和接缝由作者目录 Music/analyze_music.py 实读检查。
