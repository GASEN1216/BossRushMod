using System;
using BossRush;
using BossRush.Utils;
using UnityEngine;

namespace UnityEngine
{
    internal static class ColorUtility { internal static string ToHtmlStringRGB(Color color) { return "A0B0C0"; } }
}
namespace BossRush
{
    internal static class SkyIslandResidentInteractable { internal static void InjectLocalizations() { } }
    internal sealed partial class SkyIslandResidents
    {
        internal void Add(string id, CharacterMainControl npc) { owned.Add(id, npc); }
        internal void RefreshForTest() { RefreshLocalizedNames(); }
    }
}
namespace BossRush.Utils
{
    internal static partial class NPCNameTagHelper
    {
        internal static DuckovDialogueActor RegisterForTest(CharacterMainControl npc, string text)
        {
            var actor = npc.transform.gameObject.AddComponent<DuckovDialogueActor>();
            actor.NameKey = "BossRush_TestResident_" + npc.transform.GetInstanceID();
            // 姓名初值由既有 actor owner 注入；helper 登记不得重写它。
            LocalizationHelper.InjectLocalization(actor.NameKey, text);
            RegisterOriginalHealthBarName(npc.transform, text, 2.2f, "test");
            return actor;
        }
        internal static bool OfficialActorRetained(CharacterMainControl npc, DuckovDialogueActor actor)
        {
            OriginalHealthBarEntry entry;
            return OriginalHealthBarEntriesByTransformId.TryGetValue(npc.transform.GetInstanceID(), out entry)
                && ReferenceEquals(entry.Target, npc.transform)
                && ReferenceEquals(npc.transform.GetComponent<DuckovDialogueActor>(), actor);
        }
    }
}
internal static class LocalizationRegression
{
    internal static void Run(Action<bool, string> check)
    {
        string[] keys = { "BossRush_SkyIsland_Departure", "BossRush_SkyIslandPrelude_Objective", "BossRush_SkyIslandPrelude_Instrument" };
        string[] english = { "Depart for Sky Islands · Qinglan", "Lost Navigation Instrument", "Salvage one from the downed instrument" };
        string[] chinese = { "前往天空岛 · 晴岚群岛", "失落的航向仪", "从摔坏的航向仪上拆一具" };
        var residents = new SkyIslandResidents();
        string[] ids = { "sky_qinghe", "sky_weibai", "sky_fuzhou", "sky_miantai", "sky_zheling", "sky_bellkeeper" };
        var npcs = new CharacterMainControl[ids.Length];
        var names = new DuckovDialogueActor[ids.Length];
        L10n.IsChinese = true;
        for (int i = 0; i < ids.Length; i++)
        {
            npcs[i] = new CharacterMainControl();
            names[i] = NPCNameTagHelper.RegisterForTest(npcs[i], SkyIslandWorldStory.ResidentName(ids[i]));
            residents.Add(ids[i], npcs[i]);
        }
        npcs[4].transform.gameObject.SetActive(false);
        // 同一批 actor 中英双向切换，不清语言覆盖字典，不创建独立名牌。
        foreach (bool cn in new[] { true, false, true, false })
        {
            L10n.IsChinese = cn;
            SkyIslandPreludeFlow.InjectLocalizations();
            residents.RefreshForTest();
            for (int i = 0; i < keys.Length; i++) check(LocalizationHelper.Texts[keys[i]] == (cn ? chinese[i] : english[i]), "live entry key: " + keys[i]);
            for (int i = 0; i < ids.Length; i++)
                check(names[i].DisplayedName == SkyIslandWorldStory.ResidentName(ids[i]) && NPCNameTagHelper.OfficialActorRetained(npcs[i], names[i]), "same official resident actor retained: " + ids[i]);
            int writes = LocalizationHelper.Writes;
            for (int i = 0; i < 120; i++) residents.RefreshForTest();
            check(LocalizationHelper.Writes == writes, "unchanged language does no actor name injections");
        }
        var absent = new CharacterMainControl();
        check(!npcs[4].transform.gameObject.activeSelf && names[4].DisplayedName == "Zheling", "hidden resident official name also follows language");
        check(!NPCNameTagHelper.UpdateOriginalHealthBarDisplayName(absent.transform, "missing"), "name update cannot create an unregistered actor binding");
        NPCNameTagHelper.UnregisterOriginalHealthBarName(npcs[0].transform);
        check(!NPCNameTagHelper.UpdateOriginalHealthBarDisplayName(npcs[0].transform, "removed"), "name update cannot resurrect removed registration");
        UnityEngine.Object.Destroy(npcs[1]);
        L10n.IsChinese = true;
        residents.RefreshForTest();
        check(names[2].DisplayedName == "浮舟", "dead and unregistered residents do not stop remaining official name refreshes");
    }
}
