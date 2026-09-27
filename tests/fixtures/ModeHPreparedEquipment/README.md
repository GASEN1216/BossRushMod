# 鸭王杯公开属性与弹药解析

运行 `python tools/run_runtime_regressions.py --filter ModeHPreparedEquipment`。

每次从当前生产文件抽取公开属性冻结、伤害/暴击/移动/强度计算、默认弹药解析方法；无公式镜像。覆盖难度生命系数、预制体 setStats 中点、重复准备幂等、Constants 与 Stat 冲突、霰弹最低伤害、近战分支、未指定弹药和不兼容弹药。夹具替代 Unity Item/Stat/Constants、官方搜索与规则对象，只验证数值和选择逻辑；实际装备 Modifier、持武器切换、场景生命周期仍需正式构建与实机。
