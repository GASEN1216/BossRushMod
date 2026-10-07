# 遗种巢基地／出击激活回归

运行 `python tools/run_runtime_regressions.py --filter PetNestCompanionActivation`。逐次抽取实际 TryActivate、CleanupOnce、伤害归一、性格与补血方法，直接链接完整 PetNestCompanionAgent 和官方 Team 判据。

Unity 替身模拟 inactive 子树查询、父子激活关系、销毁连带子组件、销毁对象等于 null；用同一主角先激活并清理基地实体，再激活新的战斗实体，断言 AI 开关、目标、跟随、非零枪械／近战倍率、生命可受伤、主人换队、重复绑定、换主角和退订。旧 staging 查询漏传 includeInactive 会在基地 AI 禁用断言失败。模型、背包、天赋叠加与实际官方射击/碰撞由其它测试或实机验证，此夹具不伪装实机 AI 攻击结果。
