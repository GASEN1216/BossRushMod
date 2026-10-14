# 动态 Mod 网络 DTO 编解码

直接链接生产 `BossRushCoopJson`、共享 token 解析器和转义工具。执行真实房间 roster、多个 owner 嵌套数组、List、官方 CustomData 形状的 private SerializeField/raw bytes、长整数、Unicode、NonSerialized、坏类型及循环数据拒绝。SerializeField 仅以同名元数据属性代替 Unity 依赖，编解码和解析没有替身。

触发缺陷的 L3 证据：运行时动态加载的类，即使 public + Serializable，Unity JsonUtility 仍省略自定义元素数组/List。原先各夹具 System.Text.Json 能保留该字段，不能代表 Unity 实际行为。本夹具验证替代实现；真实 ItemTreeData 和真实多端传输另在隔离游戏中验证。

运行 `python tools/run_runtime_regressions.py --filter CoopJson`。
