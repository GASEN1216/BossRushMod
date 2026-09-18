using System;
using BossRush;
using BossRush.Utils;
using UnityEngine;
using TMPro;

namespace UnityEngine
{
    internal static class ColorUtility { internal static string ToHtmlStringRGB(Color color) { return "A0B0C0"; } }
}
namespace BossRush
{
    internal static class SkyIslandResidentInteractable { internal static void InjectLocalizations() { } }
    internal sealed partial class SkyIslandRuntimeModule
    {
        internal void BindText(TextMeshProUGUI text) { signText = text; signChinese = null; }
        internal void RefreshForTest() { RefreshSignText(); }
    }
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
        // 替换官方 UI 后端；缓存数据与更新决定来自生产方法。
        internal static int Refreshes;
        internal static TextMeshProUGUI RegisterForTest(CharacterMainControl npc, string text)
        {
            var ui = new GameObject("official name").AddComponent<TextMeshProUGUI>();
            ui.text = text;
            OriginalHealthBarEntriesByTransformId.Add(npc.transform.GetInstanceID(), new OriginalHealthBarEntry
            { Target = npc.transform, DisplayName = text, Height = 2.2f, NameText = ui, RootObject = ui.gameObject });
            return ui;
        }
        private static void RefreshOriginalHealthBarName(Transform target)
        {
            Refreshes++;
            var entry = OriginalHealthBarEntriesByTransformId[target.GetInstanceID()];
            if (entry.NameText != null && entry.NameText.text != entry.DisplayName) entry.NameText.text = entry.DisplayName;
        }
        internal static void UnregisterForTest(CharacterMainControl npc)
        { OriginalHealthBarEntriesByTransformId.Remove(npc.transform.GetInstanceID()); }
        internal static bool OriginalUiRetained(CharacterMainControl npc, TextMeshProUGUI ui)
        {
            var entry = OriginalHealthBarEntriesByTransformId[npc.transform.GetInstanceID()];
            return ReferenceEquals(entry.NameText, ui) && ReferenceEquals(entry.RootObject, ui.gameObject) && entry.Height == 2.2f;
        }
    }
}
internal static class LocalizationRegression
{
    internal static void Run(Action<bool, string> check)
    {
        string[] keys = { "BossRush_SkyIsland_Departure", "BossRush_SkyIslandPrelude_Objective", "BossRush_SkyIslandPrelude_Instrument" };
        string[] english = { "Depart for Sky Islands · Qinglan", "Lost Navigation Instrument", "Read the lost navigation instrument" };
        string[] chinese = { "前往天空岛 · 晴岚群岛", "失落的航向仪", "读取失落的航向仪" };
        var sign = new SkyIslandRuntimeModule();
        var label = new GameObject("sign").AddComponent<TextMeshProUGUI>();
        sign.BindText(label);
        var residents = new SkyIslandResidents();
        string[] ids = { "sky_qinghe", "sky_weibai", "sky_fuzhou", "sky_miantai", "sky_zheling", "sky_bellkeeper" };
        var npcs = new CharacterMainControl[ids.Length];
        var names = new TextMeshProUGUI[ids.Length];
        L10n.IsChinese = true;
        for (int i = 0; i < ids.Length; i++)
        {
            npcs[i] = new CharacterMainControl();
            names[i] = NPCNameTagHelper.RegisterForTest(npcs[i], SkyIslandWorldStory.ResidentName(ids[i]));
            residents.Add(ids[i], npcs[i]);
        }
        npcs[4].transform.gameObject.SetActive(false);
        // 不重建目标、登记或 UI，也不清语言覆盖字典，中英双向切换同一批实例。
        foreach (bool cn in new[] { true, false, true, false })
        {
            L10n.IsChinese = cn;
            SkyIslandPreludeFlow.InjectLocalizations();
            sign.RefreshForTest(); residents.RefreshForTest();
            for (int i = 0; i < keys.Length; i++) check(LocalizationHelper.Texts[keys[i]] == (cn ? chinese[i] : english[i]), "live entry key: " + keys[i]);
            check(label.text.Contains(cn ? "天空岛 · 晴岚群岛" : "Sky Islands · Qinglan") && label.text.Contains(cn ? "与船点互动即可出发" : "Interact with the boat to depart"), "same sign changes language");
            for (int i = 0; i < ids.Length; i++)
                check(names[i].text == SkyIslandWorldStory.ResidentName(ids[i]) && NPCNameTagHelper.OriginalUiRetained(npcs[i], names[i]), "same resident UI and height retained: " + ids[i]);
            int signWrites = label.TextWrites, refreshes = NPCNameTagHelper.Refreshes;
            for (int i = 0; i < 120; i++) { sign.RefreshForTest(); residents.RefreshForTest(); }
            check(label.TextWrites == signWrites && NPCNameTagHelper.Refreshes == refreshes, "unchanged language does no text writes or UI refreshes");
        }
        var absent = new CharacterMainControl();
        check(!npcs[4].transform.gameObject.activeSelf && names[4].text == "Zheling", "hidden resident name also follows language");
        check(!NPCNameTagHelper.UpdateOriginalHealthBarDisplayName(absent.transform, "missing"), "name update cannot create an unregistered UI");
        NPCNameTagHelper.UnregisterForTest(npcs[0]);
        check(!NPCNameTagHelper.UpdateOriginalHealthBarDisplayName(npcs[0].transform, "removed"), "name update cannot resurrect removed registration");
        UnityEngine.Object.Destroy(npcs[1]);
        L10n.IsChinese = true;
        residents.RefreshForTest();
        check(names[2].text == "浮舟", "dead and unregistered residents do not stop remaining name refreshes");
        UnityEngine.Object.Destroy(label.gameObject);
        sign.RefreshForTest();
        var replacement = new GameObject("new sign").AddComponent<TextMeshProUGUI>();
        sign.BindText(replacement); sign.RefreshForTest();
        check(replacement.text.Contains("天空岛"), "new scene sign initializes even without language change");
    }
}
