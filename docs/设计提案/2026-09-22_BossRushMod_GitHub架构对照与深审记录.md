# BossRushMod：GitHub 架构对照与深审记录

查询与审核日期：2026-09-22。分类：`SAFE`，文档与设计研究。执行方案见 [模块解耦、复用体系与上下文治理计划](2026-09-22_BossRushMod模块解耦与上下文治理计划.md)。

后续全量执行复审：owner 已要求一次授权后完成全部模块迁移。主计划 §7–12 已补全仓覆盖、连续检查点、宿主终态、工作区基线、工具覆盖与新窗口启动指令。本记录保留最初研究依据；文中“第一轮试点”只描述早期实施顺序，不再作为交付范围。此次复审未重新联网核验上游项目，也未实施生产迁移。

本轮实际联网查看了六个 GitHub 项目的官方 README、许可证和有关源码，并对照 BossRush 的宿主、作用域、事件、建筑、工厂与验证入口。筛选依据是可阅读的完整实现、明确的生命周期或模块边界、相关测试及 Unity/.NET 的适用性。引用固定到本轮查询到的提交，避免后续默认分支变化导致结论失去依据。

以下是上游源码阅读与本地 L1 静态审查。没有运行上游项目、导入其运行时框架或测试新架构的游戏表现；也没有把框架的性能宣传作为 BossRush 的性能证据。个别 GitHub API 请求受匿名额度限制，使用公开仓库页面和固定 SHA 的原始文件完成核对。

## 1. 查阅项目与采用范围

| 项目及固定快照 | 许可证 | 本次采用的设计 | 对 BossRush 的限制 |
| --- | --- | --- | --- |
| [hadashiA/VContainer · 4fc1ed4](https://github.com/hadashiA/VContainer/tree/4fc1ed47dff1314e73fbf2a85ab88f53601414f6) | MIT | 显式装配、作用域、创建权与处置权 | 沿用现有宿主手工装配；不增加整包容器、自有 PlayerLoop 或到处 Resolve 的入口 |
| [Cysharp/UniTask · ceac8d6](https://github.com/Cysharp/UniTask/tree/ceac8d6946b1125fe782cd171fbcb245b567dbf9) | MIT | 异步寿命、取消传递、完成源和帧时序 | 项目已有 UniTask 使用，API 以本机程序集为准；不因研究最新版本自动升级 |
| [EllanJiang/GameFramework · d0c010b](https://github.com/EllanJiang/GameFramework/tree/d0c010b05167c58e92350449d04864a91ca13fd2) | MIT | 模块调度、明确的关闭顺序、同步/排队事件区分 | 复用现有 RuntimeModuleHost；保留逐项异常隔离与手工接线；不替换资源/存档体系 |
| [UnityTechnologies/open-project-1 · 608eac9](https://github.com/UnityTechnologies/open-project-1/tree/608eac98df29cd97821a6115cd52dfb9027345b1) | Apache-2.0 | 定义与实例分离、组合动作、成对订阅 | README 明确 2021 年已停止开发，是实现范例；不移植其 Addressables、整套 SO 状态机或附带工具 |
| [kgrzybek/modular-monolith-with-ddd · 91c8ef2](https://github.com/kgrzybek/modular-monolith-with-ddd/tree/91c8ef24b4cb6ef558c95d8267fa07d68c7059f8) | MIT | 小范围模块入口、查询/动作区分、边界检查 | 示例使用现代 .NET；数据库、事务消息、每模块容器和通用命令总线不纳入本轮 |
| [TNG/ArchUnitNET · 28b62ec](https://github.com/TNG/ArchUnitNET/tree/28b62ec0d98f9babb4ccdb6d8a1c3673a0497bcb) | Apache-2.0 | 可执行的类型/成员依赖规则、循环约束 | 作为开发期检查参考；IL 不自动提供 partial 源码模块归属，需配合源码层分析 |

许可证描述对应本次查看的仓库根许可证，不概括其中所有第三方素材/依赖。本轮采用设计思路；后续若直接复制实质源码或安装依赖，应固定版本并保留相应许可说明。

## 2. 从源码提取的设计与边界

**VContainer：把装配与业务执行分开。** [ContainerBuilder.Build](https://github.com/hadashiA/VContainer/blob/4fc1ed47dff1314e73fbf2a85ab88f53601414f6/VContainer/Assets/VContainer/Runtime/ContainerBuilder.cs#L129-L158) 体现注册表、容器构建与构建回调的集中入口；它不保证在 Build 时构造所有注册服务。[ScopedContainer](https://github.com/hadashiA/VContainer/blob/4fc1ed47dff1314e73fbf2a85ab88f53601414f6/VContainer/Assets/VContainer/Runtime/Container.cs#L119-L183) 区分当前作用域实例及其处置。BossRush 的对应做法是让 Host 明确构造模块与适配器，新核心只得到所需能力；先评估现有 RuntimeScope 的可复用部分。

容器处置有边界：该实现排除外部传入的实例，transient 也不会自动加入同一处置集合；借用对象仍需指定 owner。[CompositeDisposable](https://github.com/hadashiA/VContainer/blob/4fc1ed47dff1314e73fbf2a85ab88f53601414f6/VContainer/Assets/VContainer/Runtime/Internal/CompositeDisposable.cs#L7-L33) 的逆序处置没有 BossRush 式逐项异常隔离，不能原样替换当前 cleanup。[异步入口说明](https://github.com/hadashiA/VContainer/blob/4fc1ed47dff1314e73fbf2a85ab88f53601414f6/website/docs/integrations/unitask.mdx#L35-L58) 还明确 StartAsync 同时调度、后续循环不等完成，因此模块注册先后不能替代资源就绪协议。

**UniTask：异步跟随实际业务寿命。** [取消示例](https://github.com/Cysharp/UniTask/blob/ceac8d6946b1125fe782cd171fbcb245b567dbf9/README.md#L243-L298) 将令牌传至子步骤，并区分 disable 与 destroy。[取消时机说明](https://github.com/Cysharp/UniTask/blob/ceac8d6946b1125fe782cd171fbcb245b567dbf9/README.md#L331-L353) 指出部分取消在下一次 PlayerLoop 检查时才发生。BossRush 据此明确：退出模式、卸下装备、场景解绑须撤销各自 owner 的应用资格；不可取消操作返回后核对已有会话/激活代次，不能只绑定常驻 ModBehaviour 的销毁。

[可等待对象的复用约束](https://github.com/Cysharp/UniTask/blob/ceac8d6946b1125fe782cd171fbcb245b567dbf9/README.md#L201-L222) 说明裸 UniTask 通常不可重复 await；共享准备过程需要使用当前版本支持的完成源/保留结果机制。[帧时序说明](https://github.com/Cysharp/UniTask/blob/ceac8d6946b1125fe782cd171fbcb245b567dbf9/README.md#L519-L531) 区分 Yield、NextFrame 和协程阶段。建筑恢复现有“两帧等待”必须保留，不把去重扩展成批量 coroutine 转换。[PlayerLoop 初始化源码](https://github.com/Cysharp/UniTask/blob/ceac8d6946b1125fe782cd171fbcb245b567dbf9/src/UniTask/Assets/Plugins/UniTask/Runtime/PlayerLoopHelper.cs#L288-L321) 也表明初始化受 Unity 版本分支影响，应核对游戏实际版本和既有注入 owner。

**GameFramework：模块调度和事件时序属于契约。** [GameFrameworkEntry](https://github.com/EllanJiang/GameFramework/blob/d0c010b05167c58e92350449d04864a91ca13fd2/GameFramework/Base/GameFrameworkEntry.cs#L25-L43) 明确驱动和逆向关闭；[模块优先级](https://github.com/EllanJiang/GameFramework/blob/d0c010b05167c58e92350449d04864a91ca13fd2/GameFramework/Base/GameFrameworkModule.cs#L15-L36) 关联轮询与关闭次序。BossRush 已有对应宿主，应固定当前顺序并逐个移交 owner；从外部项目学习契约，不新增平行生命周期调度器。其按接口名称反射创建模块的方式不作为当前 Mod 的默认装配方案。

[EventPool.Fire 与 FireNow](https://github.com/EllanJiang/GameFramework/blob/d0c010b05167c58e92350449d04864a91ca13fd2/GameFramework/Base/EventPool/EventPool.cs#L213-L245) 分开表达排队与立即派发。BossRush 当前 EventBus 是同步派发；架构整理要保留时序，查询与拦截仍使用有返回结果的同步入口。引入排队通知时另行声明语义，不把掉落抑制、支付或存档成功当作普通广播替换。

**Unity Open Project：共享定义，独立创建运行动作。** [StateActionSO.GetAction](https://github.com/UnityTechnologies/open-project-1/blob/608eac98df29cd97821a6115cd52dfb9027345b1/UOP1_Project/Assets/Scripts/StateMachine/ScriptableObjects/StateActionSO.cs#L6-L27) 从定义创建动作；[TransitionTableSO.GetInitialState](https://github.com/UnityTechnologies/open-project-1/blob/608eac98df29cd97821a6115cd52dfb9027345b1/UOP1_Project/Assets/Scripts/StateMachine/ScriptableObjects/TransitionTableSO.cs#L16-L40) 为一次状态机构建创建实例映射。可借鉴到建筑、装备、随机事件：只读定义复用，目标、计时、协程和效果实例分别持有。

该映射同时限定实例复用范围：同一状态机的 `createdInstances` 会让同一 SO 返回已创建的动作，跨状态机另有映射。因此采用此模式时声明每个 owner/会话的实例缓存，不把它理解为每次使用都 new，也不将运行实例缓存提升为全局共享。

[VoidEventListener](https://github.com/UnityTechnologies/open-project-1/blob/608eac98df29cd97821a6115cd52dfb9027345b1/UOP1_Project/Assets/Scripts/Events/VoidEventListener.cs#L13-L28) 把订阅与启停对应起来。BossRush 延续命名方法、幂等与明确 owner，不需要为此将现有 JSON/Config 改为 ScriptableObject。该示例的定义/实例划分也不能自动证明所有 SO 数据深层不可变。[README](https://github.com/UnityTechnologies/open-project-1/blob/608eac98df29cd97821a6115cd52dfb9027345b1/README.md#L1-L9) 已说明项目停止开发，使用范围限于读过的模式。

**Modular Monolith：边界要能执行检查。** [IMeetingsModule](https://github.com/kgrzybek/modular-monolith-with-ddd/blob/91c8ef24b4cb6ef558c95d8267fa07d68c7059f8/src/Modules/Meetings/Application/Contracts/IMeetingsModule.cs#L3-L9) 展示模块统一入口；[ModuleTests](https://github.com/kgrzybek/modular-monolith-with-ddd/blob/91c8ef24b4cb6ef558c95d8267fa07d68c7059f8/src/Tests/ArchTests/Modules/ModuleTests.cs#L47-L67) 实际检查跨模块依赖。BossRush 采用“小范围查询与操作接口 + 依赖规则”，具体方法直接表达用途，不强制所有操作变成 Command 类或通过通用总线调度。

该示例按程序集和命名空间选取类型，不能直接区分 BossRush 同一宿主类型的 partial 文件。其 [LayersTests](https://github.com/kgrzybek/modular-monolith-with-ddd/blob/91c8ef24b4cb6ef558c95d8267fa07d68c7059f8/src/Modules/Meetings/Tests/ArchTests/Module/LayersTests.cs#L21-L29) 还存在可直接读出的断言错配：测试名称检查 Infrastructure，实际仍引用 Application。此处只说明固定快照中的该断言；BossRush 的架构守卫继续要求注入越界负例，不能把上游示例测试复制后直接视为有效。

**ArchUnitNET：成员可检查，源码归属仍要另建。** [方法规则测试](https://github.com/TNG/ArchUnitNET/blob/28b62ec0d98f9babb4ccdb6d8a1c3673a0497bcb/ArchUnitNETTests/Fluent/Syntax/Elements/MethodMemberSyntaxElementsTests.cs#L136-L164) 和 [方法体依赖测试](https://github.com/TNG/ArchUnitNET/blob/28b62ec0d98f9babb4ccdb6d8a1c3673a0497bcb/ArchUnitNETTests/Fluent/Syntax/Elements/MethodMemberSyntaxElementsTests.cs#L302-L330) 表明它不局限于类级规则；[ArchLoader](https://github.com/TNG/ArchUnitNET/blob/28b62ec0d98f9babb4ccdb6d8a1c3673a0497bcb/ArchUnitNET/Loader/ArchLoader.cs#L285-L300) 通过 Cecil 读取二进制。

BossRush 的问题是“这个合并后的成员属于哪个模块”，需用 Roslyn 声明位置与编译输入建立映射。已独立的类型再用 IL 规则辅助核对。[官方限制说明](https://github.com/TNG/ArchUnitNET/blob/28b62ec0d98f9babb4ccdb6d8a1c3673a0497bcb/documentation/docs/limitations/debug_artifacts.md#L2-L7) 指出 Release 优化可能使部分依赖在 IL 中消失，因此保留源码检查。ArchUnitNET 或等价实现属于开发工具，具体选型以试点验证决定，不随游戏运行时发布。

另一个必须包装处理的行为是 [引用解析失败后跳过程序集](https://github.com/TNG/ArchUnitNET/blob/28b62ec0d98f9babb4ccdb6d8a1c3673a0497bcb/ArchUnitNET/Loader/ArchLoader.cs#L305-L336)。IL 检查必须验证本次编译产物及引用闭包，显式报告漏项；缺少游戏 DLL、Harmony 或对应配置产物时，开发期为 PARTIAL、发布验收非通过。主计划将故意缺引用的负例列入检查器验收，不能只测违规规则本身。

## 3. 深审发现与已写入的设计修订

以下条目是计划的边界或实施缺口，均已写入主计划；不是对当前玩法运行故障的认定。

| 条目 | 缺口与依据 | 修订与验证方向 |
| --- | --- | --- |
| A1：核心依赖适配器 | 原线性 `Frameworks → GameIntegration` 使核心知道具体游戏接入 | 能力核心定义所需接口，游戏适配实现接口，Host 装配；对试点核心实际检查反向引用 |
| A2：兼容接口混入框架 | `IBossRushRuntimeModule.OnAwake` 接收完整 ModBehaviour；宿主日志也形成反向引用 | 旧入口留 Host 兼容壳，新核心接窄能力；保留旧 API 时仍可检查新核心的边界 |
| A3：注册完成与准备完成混淆 | 宿主同步回调顺序不能保证异步资源完成顺序 | 区分构造/发起/就绪，继承已有前置与失败重试语义；测试就绪延迟和失败分支 |
| A4：作用域底座遗漏 | `Utilities/RuntimeScope.cs:7` 已有资源/协程/清理登记，随机事件是真实消费者 | 优先复用既有实现；保持事件 OnCleanup 后再 Scope.Clear，以及 scope 内分阶段顺序 |
| A5：边界检查不可执行 | 只要求符号分析，但原首批没有分析器输入与缺引用协议 | 批次 0 交付最小 Roslyn 成员归属检查；完整编译引用、正式/Dev 分析、缺引用非通过和越界负例 |
| A6：changed-only 漏选 | `tools/run_guards.py:132` 按守卫文本筛选，动态清单守卫可被漏掉 | 架构类守卫 always-run 或等效显式映射；同时接入 PARTIAL 结果协议，避免错误归类 |
| A7：共享状态与迟到结果 | 同一框架供多个会话使用，需要区分共享定义、实例与任务应用权 | 定义只读；状态按 owner；取消后核代次，旧任务不能清理新任务或污染其它消费者 |
| A8：通用通信可能改变行为 | EventBus 当前同步；官方购买回调、落盘和掉落 defer 有顺序要求 | 查询/动作/事实通知/拦截分开；保留同步性、事务权威与实际提交点，不照搬排队/企业消息模式 |
| A9：开发期源码可能误入生产边界 | `tests/OfficialCompileListFileExistenceGuard.py` 排除 tests，却不排除 tools 下的 C# 文件 | Roslyn 工程拟放 tests/fixtures，tools 只保留调用入口；独立依赖与回归，不加入 Mod DLL |
| A10：依赖清单和债务易失真 | 人工维护实际调用图会漂移；仅比较违规总数可能用删除旧违规抵消新增违规 | 人工声明允许依赖，分析器生成实际引用；迁移债务按精确成员依赖边核对，禁止目录级豁免 |
| A11：分析输入可能与当前构建不一致 | `Build/BossRush.rsp` 是生成制品；正式/Dev 的条件符号决定实际解析范围 | 核对清单、引用、语言版本和符号；报告完整输入与试点规则的覆盖范围，缺引用保持未验证 |
| A12：IL 加载可静默缺依赖 | ArchUnitNET 的上述加载分支捕获解析异常后跳过程序集 | IL 包装器预检引用闭包和产物指纹；用缺引用负例验证 PARTIAL/非通过，不能只依赖工具返回的图 |
| A13：1A 夹具替代了复用目标 | `tests/fixtures/AffixCombat/run.py:55` 未链接共享追踪器，`tests/fixtures/AffixCombat/Stubs.cs:291` 定义同名 RemoveAll 替身 | 切换消费者时同步链接真实追踪器或等效生产代码夹具；验证三种类型、失败、移除与 owner 隔离 |
| A14：1B 场景语义没有固定 | 两份恢复协程都在等待两帧后读取当前活动场景；并没有发起场景捕获与代次核验 | 首批保留等待后选场景；请求身份保护取消/替换后的收尾；按初始场景拒绝恢复属于另列的行为修复 |
| A15：建筑适配的对象与清理契约过于概括 | `Common/Infrastructure/ObjectCache.cs:92` 依赖 Unity 假 null；遗种巢建筑与日报建筑清理位于不同宿主阶段 | 保留场景句柄、实例 ID、常驻 prefab 排除和独立 owner；夹具模拟销毁语义，生命周期表列明原有清理顺序 |

计划已有的合理部分继续保留：单 DLL 内逐步解耦；不批量改 TypeID/存档/资源身份；先提取行为再迁路径；已重复与前瞻抽象采用不同验收；Mode G/H 的特殊协议独立核对；上下文节省通过代表任务实际测量。

## 4. 如何用于全量迁移的前置试点

批次 0 先完成试点模块与能力的归属、契约、生命周期表和最小边界检查，接入必跑规则。编译列表、运行时注册、内容数据仍是原事实源；模块索引只维护归属与声明关系，实际依赖由分析器生成，避免人工维护两份调用图。

1A 采用已有属性追踪器消除词缀挂载副本，切换夹具到真实追踪器后核对三种 ModifierType、边界值、失败与移除；它验证公共组件的提取方法。1B 使用日报/遗种巢的真实建筑流程验证“共享核心 + 业务策略 + 游戏适配”：复用等待/遍历/恢复，分别持有缓存与协程；保留两帧后选择活动场景的语义，重入、取消、场景更替、对象销毁和迟到收尾纳入回归。具体判据已列入主计划第 5 节。

每一项抽象的交付同时包含：当前消费者、扩展场景、唯一实现、明确 owner、公开契约、行为与顺序回归、局部阅读入口。收益按重复维护点、触及公共实现的范围、读取量与执行成本衡量。用试点结果修正后续提取方法，再按主计划 M00–M15 持续完成全仓范围；物理目录、接口数量与采用模式名称本身不作为完成指标。

本次新增研究记录并修订主计划，未实施生产代码重构。上游源码缓存仅用于核对，位于 `.codex_tmp/`，不进入正式编译清单或发布物。

终审证据：六个项目的固定 SHA 均通过 GitHub 提交 API 精确解析；源码论述按固定快照与现存缓存复核。主计划和本记录的本地引用、行号范围、Markdown 表格、代码围栏与互链经过脚本检查；文档总导航已补上入口。结果属于 L1 静态研究与文档核验，未执行上游测试、架构检查器、生产回归或游戏验证；不能据此宣称解耦效果或性能收益已经实现。
