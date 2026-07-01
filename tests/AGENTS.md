# tests/AGENTS.md — Python 守卫专项规则

> 先读根目录 `AGENTS.md`。本目录的 Python 脚本是静态守卫，不是 C# 单元测试。

## 规则

- 守卫脚本直接位于 `tests/*.py`，不要新建 `tests/guards/` 子目录。
- 改动被 guard 断言的结构时同步 guard，不要删除断言逃避失败。
- 新 guard 应聚焦一个明确 invariant，失败信息要指出文件和缺失模式。
- 白名单只能解释既有债务；新增代码默认不进白名单。
- 属性/随机测试脚本也应能在普通 Python 环境运行，不依赖游戏进程。

## 运行

单个：

```cmd
python tests\SomeGuard.py
```

全量：

```cmd
for %f in (tests\*.py) do python %f
```

如果只能在 Linux/WSL 跑，需要说明这不是 Windows 编译验证。
