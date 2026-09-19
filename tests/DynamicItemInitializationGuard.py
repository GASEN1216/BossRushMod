"""动态克隆物品必须在官方同步、异步与 fallback 交付前初始化。"""
from pathlib import Path
import sys
from cs_source_util import clean_source


def main():
    source = clean_source(Path("Patches/ItemStatsSystem/ItemAssetsCollectionDynamicRegistrationPatch.cs").read_text(encoding="utf-8"))
    requirements = (
        '"ItemAssetsCollection.InstantiateSync(int)", true)',
        # 静态 InstantiateAsync 的方法体是编译器生成的状态机，反编译源里看不出它走不走
        # _Local；三条实例化入口都必须自己包一层，不能靠「应该会转发」。
        '"ItemAssetsCollection.InstantiateAsync(int)", true)',
        '"ItemAssetsCollection.InstantiateAsync_Local(int)", true)',
        "foreach (Patch postfix in patchInfo.Postfixes)",
        "item.Initialize();",
        "item.AgentUtilities.Initialize(item);",
        "Item item = await operation;",
        "InitializeInstance(item);",
        "DynamicItemRegistrationPatchSupport.InitializeInstance(__result);",
        "__result = DynamicItemRegistrationPatchSupport.InitializeAsyncInstance(__result);",
    )
    missing = [value for value in requirements if value not in source]
    for name, call in (
        ("ItemAssetsCollectionInstantiateSyncDynamicRegistrationPatch", "InitializeInstance(__result)"),
        ("ItemAssetsCollectionInstantiateAsyncDynamicRegistrationPatch", "InitializeAsyncInstance(__result)"),
        ("ItemAssetsCollectionInstantiateAsyncLocalDynamicRegistrationPatch", "InitializeAsyncInstance(__result)"),
        ("ItemAssetsCollectionInstantiateFallbackDynamicRegistrationPatch", "InitializeInstance(__result)"),
    ):
        start = source.index("internal static class " + name)
        end = source.find("internal static class ", start + 1)
        if call not in source[start:end if end >= 0 else len(source)]:
            missing.append(name + ": " + call)
    if missing:
        print("DynamicItemInitializationGuard: FAIL - " + "; ".join(missing))
        return 1
    print("DynamicItemInitializationGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
