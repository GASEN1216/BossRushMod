using System;
using System.Reflection;
using BossRush;
using BossRush.Common.Equipment;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class Program
{
    private static int assertions;
    private static int failures;
    private static void Check(bool value, string message)
    {
        assertions++;
        if (!value) { failures++; Console.WriteLine("FAIL " + message); }
    }
    private static bool Protected(FlightTotemEffectManager manager)
    {
        return (bool)typeof(EquipmentEffectManager<FlightConfig, FlightAbilityManager>)
            .GetField("sceneTransitionProtection", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(manager);
    }
    private static void Main()
    {
        var playerObject = new GameObject("Player");
        var player = playerObject.AddComponent<CharacterMainControl>();
        var characterItem = playerObject.AddComponent<Item>();
        var slot = new Slot { Key = "Totem" };
        characterItem.Slots.Add(slot);
        var totem = new GameObject("FlightTotem").AddComponent<Item>();
        totem.TypeID = FlightConfig.TotemTypeIdBase;
        CharacterMainControl.Main = player;
        object originalDash = player.dashAction;
        FlightTotemEffectManager effect = FlightTotemEffectManager.Instance;
        FlightAbilityManager ability = FlightAbilityManager.Instance;
        Scene official = new Scene { name = "Base_SceneV2", path = "Assets/Scenes/Base_SceneV2.unity" };
        foreach (string path in new[] { SceneRuntimeGate.StoneOutpostResourceScenePath,
            SceneRuntimeGate.StoneOutpostResourceScenePath.ToLowerInvariant() })
        {
            SceneManager.Load(official);
            player.SetSlot(slot, totem);
            Check(ability.IsAbilityEnabled && player.dashAction == null, "穿上图腾应调用生产注册流程并暂存 Dash：" + path);
            Scene resource = new Scene { name = "Resource", path = path };
            SceneManager.Load(resource);
            SceneManager.Unload(resource);
            Check(!Protected(effect), "资源往返不得开启场景切换保护：" + path);
            player.SetSlot(slot, null);
            Check(!ability.IsAbilityEnabled, "资源返回后卸装必须停用真实飞行能力：" + path);
            Check(ReferenceEquals(player.dashAction, originalDash), "资源返回后卸装必须恢复原 Dash：" + path);
            // 即使旧代码未停用，下一测试也从真实 UnregisterAbility 恢复可比基线。
            UnityEngine.Object.Destroy(effect.gameObject);
            effect = FlightTotemEffectManager.Instance;
        }

        player.SetSlot(slot, totem);
        SceneManager.Unload(official);
        Check(Protected(effect), "真实关卡卸载仍启用保护");
        player.SetSlot(slot, null);
        Check(ability.IsAbilityEnabled && player.dashAction == null, "真实切图中的临时空槽不能停用能力或抢回 Dash");
        Scene resourceDuringTransition = new Scene { name = "StoneOutpost", path = SceneRuntimeGate.StoneOutpostResourceScenePath };
        SceneManager.Load(resourceDuringTransition);
        Check(Protected(effect), "真实切图期间的资源加载不得提前解除保护");
        SceneManager.Unload(resourceDuringTransition);
        Check(Protected(effect), "真实切图期间资源卸载不改保护状态");
        SceneManager.Load(official);
        Check(!Protected(effect), "真实关卡加载后应解除保护");
        effect.CheckCurrentEquipment();
        Check(!ability.IsAbilityEnabled && ReferenceEquals(player.dashAction, originalDash), "真实关卡加载后确认空槽，应停用并还原 Dash");

        foreach (string path in new[] { "Assets/SkyIsland/SkyIslandRaid.unity", "Assets/SkyIsland/SkyIslandWorld.unity",
            "Assets/Foreign/StoneOutpost.unity", "Assets/StoneOutpost/StoneOutpost.unity.extra" })
        {
            player.SetSlot(slot, totem);
            Scene foreign = new Scene { name = "SkyIslandWorld", path = path };
            SceneManager.Unload(foreign);
            Check(!SceneRuntimeGate.IsModResourceScene(foreign) && Protected(effect), "同名或仅路径前缀相同的外部 Scene 仍走真实切图保护：" + path);
            SceneManager.Load(official);
            player.SetSlot(slot, null);
            Check(ReferenceEquals(player.dashAction, originalDash), "外部 Scene 后卸装恢复：" + path);
        }
        Check(!SceneRuntimeGate.IsModResourceScene(new Scene()), "空路径不能被视作本 Mod 资源 Scene");
        UnityEngine.Object.Destroy(effect.gameObject);
        UnityEngine.Object.Destroy(ability.gameObject);
        Console.WriteLine("EquipmentResourceScene: " + (failures == 0 ? "PASS" : "FAIL") +
            " (" + assertions + " assertions, " + failures + " failures)");
        Environment.ExitCode = failures == 0 ? 0 : 1;
    }
}
