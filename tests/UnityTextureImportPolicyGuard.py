"""作者工程里的贴图导入设置必须符合 2026-09-19 定下的 Max Size / Crunch 口径。

背景（2026-09-19 人工实测第 1 条）：玩家反馈「游戏内贴图看不清有噪点」。查下来是两件事：

1. **看不清**——一批 1024 源图的 importer Max Size 被留在 64 / 128
   （36 张成就图标全是 64，霜之哀伤与焚皇断界戟的物品图标是 128），
   进包被压成糊图。Max Size 只会向下压，源图再大也救不回来。
2. **有噪点**——图标与随身装备模型贴图开着 `crunchedCompression`，
   `compressionQuality: 50`。Crunch 只压磁盘、不省显存，50 档的块状噪点
   在近距离看的 UI 图标上肉眼可见。

口径：物品 / 装备 / 图标 128–512（最多 512），立绘 / 横幅 / 海报最多 1024。
环境与地形贴图**不在**这条口径内，见 `tools/apply_unity_texture_policy.py` 的 `UNTOUCHED`。

本守卫复用生产脚本自己的策略表做 dry-run：只要它还想改任何一张，就说明作者工程
被改回去了或者新素材没按口径导入。作者工程是 local-only，找不到时打印 SKIP 原因
——**不要把 SKIP 当 PASS**，这正是旧 `SkyIslandMiniMapGuard` 写死 `D:/code/...`
之后静默跳过、谁都没发现的那种坑。
"""
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "tools"))

from apply_unity_texture_policy import iter_texture_metas, match_policy, rewrite  # noqa: E402
from unity_project_path import describe_missing, find_unity_project  # noqa: E402


def main():
    project = find_unity_project()
    if project is None:
        print("UnityTextureImportPolicyGuard: SKIP - " + describe_missing())
        return 0

    assets = os.path.join(project, "Assets")
    offenders = []
    for rel, _meta_path, text in iter_texture_metas(assets):
        policy = match_policy(rel)
        if policy is None:
            continue
        target_size, kill_crunch, _label = policy
        _new_text, changes = rewrite(text, target_size, kill_crunch)
        if changes:
            offenders.append(rel + ": " + "; ".join(sorted(set(changes))))

    if offenders:
        print("UnityTextureImportPolicyGuard: FAIL")
        for line in offenders[:40]:
            print("  " + line)
        if len(offenders) > 40:
            print("  ... 另有 %d 张不合口径" % (len(offenders) - 40))
        print("  修复：python tools/apply_unity_texture_policy.py --unity-project <工程> --apply"
              "，然后在 Unity 里重新导入并重打相关 bundle。")
        return 1

    print("UnityTextureImportPolicyGuard: PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
