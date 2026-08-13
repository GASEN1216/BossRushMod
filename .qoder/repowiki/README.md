# BossRushMod 仓库知识库

这里保存面向开发者和 AI 协作者的详细代码知识。开始实现功能前，先读仓库根目录的 [`AGENTS.md`](../../AGENTS.md)；本知识库用于快速定位资料，不能替代当前代码、构建脚本和 guard 对事实的确认。

## 从哪里开始

| 需求 | 首选入口 | 内容 |
| --- | --- | --- |
| 判断模块边界和主要源码 | [`knowledge/zh/_index.yaml`](knowledge/zh/_index.yaml) | 模块、scope、依赖和关联关系的总索引 |
| 快速了解一个子系统 | [`knowledge/zh/`](knowledge/zh/) | 每个模块的概述、架构设计、技术栈、编码规范和特殊配置 |
| 理解具体玩法或实现流程 | [`zh/content/`](zh/content/) | 按主题组织的详细说明和源码引用 |
| 了解 Mode G 宿命回响 | [Mode G 知识卡](knowledge/zh/BossRushMod%20模组根工程/Mode%20G%20宿命回响模式运行时/概述.md) / [游戏模式总览](zh/content/游戏模式系统/游戏模式系统.md) | 九波编排、宿敌、契约、奖励、持久化和发布门控 |
| 检查生成状态 | [`zh/meta/`](zh/meta/) | repowiki 工具元数据；通常由生成工具维护 |

常用主题入口：

- [项目概览](zh/content/项目概览/项目概览.md)
- [游戏模式系统](zh/content/游戏模式系统/游戏模式系统.md)
- [自定义 Boss 系统](zh/content/自定义%20Boss%20系统/自定义%20Boss%20系统.md)
- [装备与物品系统](zh/content/装备与物品系统/装备与物品系统.md)
- [NPC 关系系统](zh/content/NPC%20关系系统/NPC%20关系系统.md)
- [用户界面与本地化](zh/content/用户界面与本地化/本地化系统.md)
- [工具与调试](zh/content/工具与调试/测试框架/Python%20守卫脚本.md)
- [在线 Wiki 站点](zh/content/文档站点/在线%20Wiki%20站点.md)

若不知道文件名，直接在两个正文目录搜索：

```powershell
rg -n "关键词" .qoder/repowiki/knowledge/zh .qoder/repowiki/zh/content
```

## 与其他文档的边界

- `AGENTS.md`：协作规则和不可破坏契约的唯一事实来源。
- `docs/`：本地设计、契约、迁移和历史资料，默认 local-only。
- `WikiContent/`：游戏内百科实际内容。
- `wiki-site/`：面向玩家的在线 Wiki 站点。
- `.qoder/repowiki/`：面向实现和维护的仓库级详细知识库。

知识库内容可能落后于代码。出现冲突时，按“当前代码与脚本 > `AGENTS.md` 和专项约定 > 本知识库 > 历史设计稿”的顺序核对，并在同一次变更中修正文档。

## 代码变更后的同步清单

1. 在 `knowledge/zh/_index.yaml` 按源码 scope 找到对应模块。
2. 更新该模块的知识卡；涉及具体玩法、流程或配置时，同时更新 `zh/content/` 中的主题文档。
3. 新增子系统时补充模块目录、`_module.yaml` 和 `_index.yaml` 条目，并建立依赖或关联关系。
4. 保留精确的源码路径、类型名、配置 key、TypeID、存档 key 和发布门控；不写未经确认的运行时结论。
5. 提交前检查引用路径存在、Markdown 相对链接可解析，并运行受影响的编译和 guard。

`zh/meta/repowiki-metadata.json` 是生成器状态，不作为人工理解代码的首选入口。使用生成工具刷新知识库时应一并更新；普通手工勘误不要直接改写其中的编码状态。

## 提交边界

`.qoder/repowiki/` 是需要版本控制的长期知识资产。`.qoder/better-harness/`、`.qoder/better-harness-runs/` 和临时统计脚本属于工具过程产物，不应随知识库提交。
