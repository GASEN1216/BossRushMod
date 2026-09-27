# 遗种巢成长与自定义血脉装备回归

运行 `python tools/run_runtime_regressions.py --filter PetNestGrowth`。

直接链接生产 `PetNestGrowth.cs` 和 `PetNestTuning.cs`，逐次抽取生产中性化与专属装备换装方法，只把 `UniTask` 返回类型换成 `Task`。覆盖官方 Wiki 伤害样本顺序、初生与成年体型、异常数值、击杀经验、旧槽替换、缺资源返回 FallbackItem、插槽拒绝与异步期间场景消失。

Unity 角色、物品和插槽用最小替身；Boss Config 替身只供身份分派。本夹具不证明实际装备模型挂点、子弹、AI 行为、血量刷新或渲染效果正确，这些需 Windows 构建与 owner 实机验收。
