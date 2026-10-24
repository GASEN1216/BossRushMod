"""Custom visual roots must follow official fog visibility without changing gameplay state."""
from pathlib import Path
import re
import sys

from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parent.parent
HELPER = "Utilities/ModEntityFogVisibility.cs"
COURIER = "Integration/NPCs/Courier/CourierNpcRuntimeModule.cs"
GOBLIN = "Integration/NPCs/Goblin/GoblinNPC.cs"
NURSE = "Integration/NPCs/Nurse/NurseNPC.cs"
DUCK = "Integration/NPCs/DuckNpc/DuckNpcFactory.cs"
BODY = "Integration/SandstormChampion/SandstormChampionBody.cs"
WITCH = "Integration/PhantomWitch/PhantomWitchAbilityController_MovementAndDamage.cs"
FACTORY = "Integration/EquipmentFactory.cs"
PROCESSING = "Integration/EquipmentFactory_ItemProcessing.cs"
HALBERD = "Integration/DragonKing/Weapons/FenHuangHalberdWeaponConfig.cs"
SCYTHE = "Integration/PhantomWitch/PhantomWitchScytheWeaponConfig.cs"


def source(path):
    return clean_source((ROOT / path).read_text(encoding="utf-8-sig"))


def method(code, name):
    match = re.search(r"\b(?:public|private|internal)\s+(?:static\s+)?(?:[\w<>\[\]]+)\s+" + name + r"\s*\(", code)
    assert match, "缺少方法 " + name
    opening = code.index("{", match.start())
    depth, cursor = 1, opening + 1
    while depth:
        depth += (code[cursor] == "{") - (code[cursor] == "}")
        cursor += 1
    return re.sub(r"\s+", " ", code[opening + 1:cursor - 1]).strip()


def require(value, label):
    assert value, label
    print("PASS " + label)


def ordered(code, *tokens):
    positions = [code.find(token) for token in tokens]
    return all(position >= 0 for position in positions) and positions == sorted(positions)


def main():
    code = source(HELPER)
    attach, subscribe, fog, late, enforce, restore, unsubscribe, destroy = (
        method(code, name) for name in ("Attach", "Subscribe", "OnFogActiveChanged", "LateUpdate", "EnforceHidden", "RestoreRendering", "Unsubscribe", "OnDestroy"))
    require("visualRoot.GetComponent<ModEntityFogVisibility>()" in attach and "if (binding != null) return binding;" in attach,
            "视野 owner 重复装配必须复用")
    require("sample.GetComponentInChildren<FogOfWarHider>(true)" in attach,
            "优先复用角色现有官方 hider")
    require(ordered(attach, "SetActive(false)", "AddComponent<FogOfWarHider>()", "source.SamplePoints =",
                    "source.PermanentlyReveal = false;", "ownedProbe.SetActive(true)"),
            "新 probe 必须 inactive 配齐采样再激活")
    require("new Vector3(0f, 0.526f, 0f)" in attach and "new Vector3(-0.15f, 0f, 0f)" in attach
            and "new Vector3(0.15f, 0f, 0f)" in attach
            and "new Transform[] { binding.ownedProbe.transform, left, right }" in attach,
            "独立 NPC 采样必须沿用原版三点高度和间距")
    require("if (subscribed || source == null) return;" in subscribe
            and "source.OnActiveChanged += OnFogActiveChanged;" in subscribe
            and "LevelManager.OnMainCharacterDead += OnMainCharacterDead;" in subscribe,
            "官方视野和死亡事件必须幂等订阅")
    require("OnFogActiveChanged(!source.isActiveAndEnabled || source.NumObservers > 0);" in subscribe
            and "main.Health.IsDead" in subscribe,
            "首次显隐须读取官方启用观察者与玩家死亡状态")
    require("!visible && !mainCharacterDead && LevelManager.Instance != null && LevelManager.Instance.IsRaidMap" in fog,
            "只在 Raid 存活玩家视野外隐藏")
    require("if (shouldHide == hidden) return;" in fog
            and "rendererRestore[i] = renderers[i].forceRenderingOff;" in fog
            and "lightRestore[i] = lights[i].cullingMask;" in fog,
            "隐藏快照只在状态切换时保存")
    require("renderers[i].forceRenderingOff = true;" in enforce and "lights[i].cullingMask = 0;" in enforce,
            "只通过渲染强制关闭和光源遮罩隐藏")
    require("renderers[i].forceRenderingOff = rendererRestore[i];" in restore
            and "lights[i].cullingMask = lightRestore[i];" in restore,
            "恢复必须保留技能强制隐藏与原始光源遮罩")
    require(late == "if (!hidden) return; EnforceHidden();"
            and "GetComponents" not in enforce and "GetComponent" not in enforce,
            "热路径只在隐藏时遍历缓存不得扫描层级")
    require(not re.search(r"\b(?:renderers\s*\[.*?\]|renderer)\s*\.enabled\s*=", code)
            and "SetTeam(" not in code and "Collider" not in code and "visualRoot.SetActive(" not in code
            and "gameObject.SetActive(" not in code,
            "视野 owner 不得改技能 enabled 根对象碰撞或阵营")
    require("source.OnActiveChanged -= OnFogActiveChanged;" in unsubscribe
            and "LevelManager.OnMainCharacterDead -= OnMainCharacterDead;" in unsubscribe
            and "RestoreRendering();" in unsubscribe
            and "Unsubscribe();" in method(code, "OnDisable")
            and "Unsubscribe();" in destroy and "Destroy(ownedProbe);" in destroy,
            "禁用销毁必须退订还原并清理自建 probe")
    require("mainCharacterDead = true; RestoreRendering();" in method(code, "OnMainCharacterDead"),
            "玩家死亡必须立即恢复渲染")
    for path, variable in ((COURIER, "courierNPCInstance"), (GOBLIN, "goblinNPCInstance"), (NURSE, "nurseNPCInstance")):
        compact = re.sub(r"\s+", " ", source(path))
        layer = "NPCCommonUtils.SetLayerRecursively(" + variable + ', LayerMask.NameToLayer("Default"));'
        require(layer + " ModEntityFogVisibility.Attach(" + variable + ");" in compact,
                path + " 独立 NPC 修层后必须接官方视野")
    duck = method(source(DUCK), "ConfigureSpawnedNpc")
    require("AttachMarker(npc, request); ModEntityFogVisibility.Attach(npc.gameObject);" in duck,
            "DuckNpc 工厂完成模型阵营配置后必须接视野")
    body = method(source(BODY), "Attach")
    require(ordered(body, "body.Build();", "ModEntityFogVisibility.Attach(go, boss.transform);"),
            "沙暴额外外观建好后必须复用 Boss 视野")
    witch = method(source(WITCH), "RestoreVisibleState")
    require("SetStealthMode(PhantomWitchStealthMode.Visible);" in witch and not re.search(r"\bShow\s*\(", witch),
            "女巫技能恢复不能清除官方迷雾 Hidden")
    equipment = source(PROCESSING)
    ensure = method(equipment, "EnsureModelSubVisuals")
    require("if (modelGo == null || modelGo.GetComponent<CharacterSubVisuals>() != null) { return; }" in ensure
            and "modelGo.AddComponent<CharacterSubVisuals>();" in ensure and "visuals.SetRenderers();" in ensure,
            "装备只在缺少官方子视觉时初始化且保留已有缓存")
    factory = re.sub(r"\s+", " ", source(FACTORY))
    require("EnsureModelSubVisuals(modelAgent.gameObject);" in factory
            and "if (detectedType != EquipmentType.Gun) { EnsureModelSubVisuals(agent.gameObject); }" in factory,
            "装备发现和显式绑定入口必须登记官方子视觉")
    require(equipment.count("EnsureModelSubVisuals(runtimeCopy);") == 2,
            "embedded 与最终手持 clone 都必须登记官方子视觉")
    for path in (HALBERD, SCYTHE):
        prepare = method(source(path), "PrepareRuntimeHoldAgentVisual")
        require(ordered(prepare, "DisableMotionBlur(go);", "FixModelGraphics(go);", "EnableRuntimeRenderers(go);",
                        "go.GetComponentInParent<CharacterMainControl>(true)", "holder.characterModel.SyncHiddenToMainCharacter();"),
                path + " 近战视觉准备末尾必须还原官方迷雾层")
    require("Utilities\\ModEntityFogVisibility.cs" in (ROOT / "compile_official.bat").read_text(encoding="utf-8-sig")
            and "FOW.dll" in (ROOT / "compile_official.bat").read_text(encoding="utf-8-sig"),
            "正式编译必须纳入视野 owner 和 FOW 引用")
    print("ModEntityFogVisibilityGuard passed")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (AssertionError, ValueError) as error:
        print("FAIL " + str(error))
        raise SystemExit(1)
