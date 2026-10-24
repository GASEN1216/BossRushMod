"""友好 NPC 的旧名牌入口只能刷新现有官方对话 actor，不能创建头顶 UI。"""
from pathlib import Path
import re
import sys
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]
HELPER = ROOT / "Integration/Utils/NPCNameTagHelper.cs"


def method(source: str, signature: str) -> str:
    hits = list(re.finditer(r"^([ \t]*)" + re.escape(signature), source, re.M))
    if len(hits) != 1:
        raise ValueError("方法锚点必须唯一: " + signature)
    hit = hits[0]
    opening = source.index("{", hit.end())
    end = re.search(r"^" + re.escape(hit.group(1)) + r"\}", source[opening:], re.M)
    if end is None:
        raise ValueError("方法未闭合: " + signature)
    return source[opening:opening + end.end()]


def check(source: str) -> list[str]:
    source = clean_source(source).replace("\r\n", "\n")
    errors = []

    def need(condition, message):
        if not condition:
            errors.append(message)

    try:
        reset = method(source, "public static void ResetStaticCaches()")
        refresh_camera = method(source, "private static void RefreshCachedCamera()")
        rotate = method(source, "public static void UpdateNameTagRotation(")
        register = method(source, "public static bool RegisterOriginalHealthBarName(")
        refresh = method(source, "public static void RefreshOriginalHealthBarName(")
        rename = method(source, "internal static bool UpdateOriginalHealthBarDisplayName(")
        unregister = method(source, "public static void UnregisterOriginalHealthBarName(")
        apply_name = method(source, "private static void TryApplyOfficialDialogueName(")
    except ValueError as error:
        return [str(error)]

    need("private static Transform _cachedCameraTransform;" in source,
         "旧世界空间标签仍须缓存相机 Transform")
    need("_cachedCameraTransform = null;" in reset, "清理必须清掉相机 Transform 缓存")
    need("_cachedCameraTransform = _cachedCamera != null ? _cachedCamera.transform : null;" in refresh_camera,
         "刷新相机时必须一并刷新 Transform 缓存")
    need("nameTagObject.transform.rotation = _cachedCameraTransform.rotation;" in rotate,
         "旧世界空间标签旋转必须复用相机 Transform 缓存")

    registered_path = "\n".join((register, refresh, rename, unregister, reset, apply_name))
    need(not re.search(r"(?:new\s+GameObject\s*\(|AddComponent\s*<|Instantiate\s*\(|CreateNameTag\s*\(|HealthBarManager|\bHealth\b|SetActive\s*\(|WorldToScreenPoint\s*\()", registered_path),
         "友好 NPC 登记路径不得创建 UI、Health、actor 或改变节点显隐")
    need("RefreshOriginalHealthBarName(parent);" in register and "TryApplyOfficialDialogueName(entry);" in refresh,
         "登记与刷新入口必须接到官方对话姓名绑定")
    need("entry.Actor = entry.Target.GetComponent<DuckovDialogueActor>();" in apply_name,
         "姓名只能绑定现有 DuckovDialogueActor，不得创建替代组件")
    need("if (now < entry.NextActorLookupTime) return;" in apply_name
         and "entry.NextActorLookupTime = now + 1f;" in apply_name,
         "无 actor 时必须按 1 秒间隔重试查找")
    need('nameKey.StartsWith("BossRush_", StringComparison.Ordinal)' in apply_name,
         "只能写 BossRush_ 姓名键，不能覆盖原版全局姓名")
    need("LocalizationHelper.InjectLocalization(nameKey, entry.DisplayName);" in apply_name,
         "姓名须注入官方 actor 的 NameKey")
    need("!entry.PendingNameUpdate" in apply_name and "entry.PendingNameUpdate = false;" in apply_name
         and "entry.PendingNameUpdate = true;" in rename and "PendingNameUpdate = true" not in register,
         "只有显式姓名变化才能提交注入，登记不得覆盖 owner 初始姓名")
    need("string.Equals(entry.DisplayName, next, StringComparison.Ordinal)" in rename,
         "稳定姓名文字必须避免重复注入")
    need("OriginalHealthBarEntriesByTransformId.TryGetValue(parent.GetInstanceID(), out entry)" in rename
         and "RegisterOriginalHealthBarName(" not in rename,
         "姓名更新只作用于已有登记，不得复活注销项")
    need("OriginalHealthBarEntriesByTransformId.Remove(parent.GetInstanceID());" in unregister
         and "OriginalHealthBarEntriesByTransformId.Clear();" in reset,
         "注销和重置必须释放 helper 登记")
    need("Destroy(" not in unregister + reset and "Actor = null" not in unregister + reset,
         "注销和重置不得删除其他 owner 的官方 actor")
    return errors


def main() -> int:
    errors = check(HELPER.read_text(encoding="utf-8-sig"))
    for directory in (ROOT / "Integration/NPCs", ROOT / "SkyIsland"):
        for path in directory.rglob("*.cs"):
            source = clean_source(path.read_text(encoding="utf-8-sig"))
            if re.search(r"NPCNameTagHelper\.(?:CreateNameTag|UpdateNameTagRotation)\s*\(", source):
                errors.append(str(path.relative_to(ROOT)) + "：友好 NPC 不得再调用旧头顶标签入口")
    if errors:
        print("NPCNameTagHotPathTransformGuard: FAIL")
        for error in errors:
            print("  - " + error)
        return 1
    print("NPCNameTagHotPathTransformGuard: PASS (existing official dialogue actor only)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
