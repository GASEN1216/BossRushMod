using System;
using System.Collections.Generic;
using BossRush;
using UnityEngine;
using TMPro;

// 只替代宿主存储与生命周期驱动；名称/描述 getter 从官方源码逐字抽取。
internal partial class InteractableBase : Component
{
    protected bool overrideInteractName;
    protected string _overrideInteractNameKey;
    private string defaultInteractNameKey = "UI_Interact";
    protected Collider interactCollider;
    protected Vector3 interactMarkerOffset;
    private float interactTime;
    internal float InteractTime { get { return interactTime; } }
    protected virtual void Awake() { }
    protected virtual void Start() { }
    protected virtual bool IsInteractable() { return true; }
    protected virtual void OnTimeOut() { }
}

namespace SodaCraft.Localizations
{
    internal static class LocalizationLookup
    {
        internal static string ToPlainText(this string key)
        {
            string value;
            return key != null && LocalizationHelper.Texts.TryGetValue(key, out value) ? value : "*" + key + "*";
        }
    }
}

namespace BossRush.Utils
{
    internal static class NPCInteractionGroupHelper
    {
        internal static void GetOrCreateGroupList(InteractableBase owner, string label) { }
    }
}

namespace BossRush
{
    internal abstract partial class BossRushBuildingInteractableBase
    {
        internal void AwakeForTest() { Awake(); }
        internal void StartForTest() { Start(); }
        internal void CompleteForTest() { if (IsInteractable()) OnTimeOut(); }
    }
}

internal static class ItemAndInteractionLocalizationRegression
{
    internal static void Run(Action<bool, string> check)
    {
        int completed = 0;
        var searches = new Dictionary<string, SkyIslandSearchPoint>();
        L10n.IsChinese = true;
        foreach (string[] chapter in SkyIslandJournal.Chapters)
            foreach (string marker in chapter)
            {
                var point = new GameObject(marker).AddComponent<SkyIslandSearchPoint>();
                point.AwakeForTest();
                point.Bind(() => completed++, SkyIslandPointText.Name(marker));
                point.StartForTest();
                searches.Add(marker, point);
            }
        check(searches.Count == 20, "all production journal search points are exercised");
        var gather = new GameObject("SkyIslandGather_C1").AddComponent<SkyIslandGatherPoint>();
        gather.AwakeForTest();
        gather.Bind("采集青穗", 1.5f, () => completed++);
        gather.StartForTest();
        var pigeon = new GameObject("SkyIslandPigeon").AddComponent<SkyIslandStoryInteractable>();
        pigeon.AwakeForTest();
        pigeon.Bind("信鸽来信", () => completed++);
        pigeon.StartForTest();
        var sign = new GameObject("Label").AddComponent<TextMeshPro>();
        sign.transform.SetParent(pigeon.transform, false);
        int pigeonId = pigeon.GetInstanceID(), gatherId = gather.GetInstanceID();
        gather.gameObject.SetActive(false);

        foreach (bool chinese in new[] { true, false, true, false })
        {
            L10n.IsChinese = chinese;
            SkyIslandItems.InjectLocalization();
            int definitions = 0;
            foreach (string[] definition in SkyIslandItems.DefinitionsForTest())
            {
                definitions++;
                var item = new OfficialItemDescriptionProbe { DisplayNameRaw = definition[0] };
                ModeFItemConfigHelper.SetHiddenMember(item, "description", "cannot replace the official computed key");
                check(item.DescriptionRaw == definition[0] + "_Desc", "official description remains a computed key: " + definition[0]);
                check(!string.IsNullOrEmpty(item.Description) && item.Description == definition[chinese ? 1 : 2],
                    "all item descriptions resolve through official getter: " + definition[0]);
            }
            check(definitions == SkyIslandItemRules.AllTypeIds.Length, "every island item definition was exercised");
            SkyIslandSceneReferenceBridge.InjectLocalization();
            string sceneName;
            check(LocalizationHelper.Texts.TryGetValue("BossRush_SkyIsland_SceneName", out sceneName) && sceneName ==
                L10n.T("天空岛 · 晴岚群岛", "Sky Islands · Qinglan"), "registered scene name refreshes without recreating the scene");

            // 固定搜索点不重建、不重新 Bind；只走宿主现有语言重注入入口。
            SkyIslandSearchPoint.InjectLocalizations();
            foreach (var entry in searches)
            {
                // 延迟 Start 不可用 Bind 时缓存的旧语言覆盖新语言。
                entry.Value.StartForTest();
                check(entry.Value.InteractName == SkyIslandPointText.Name(entry.Key), "existing F prompt refreshes: " + entry.Key);
            }
            string gatherName = L10n.T("采集青穗", "Gather greenear");
            string pigeonName = L10n.T("信鸽来信", "Pigeon letter");
            gather.Relabel(gatherName);
            pigeon.Relabel(pigeonName);
            check(gather.InteractName == gatherName && gather.InteractTime == 1.5f,
                "hidden gathering F prompt changes without changing its timer");
            check(pigeon.InteractName == pigeonName && sign.text == pigeonName,
                "same pigeon updates both overhead text and official F prompt");
            check(pigeon.GetInstanceID() == pigeonId && gather.GetInstanceID() == gatherId,
                "language refresh retains live objects");
            int writes = LocalizationHelper.Writes;
            for (int frame = 0; frame < 120; frame++)
                check(gather.InteractName == gatherName && pigeon.InteractName == pigeonName, "official repeated name lookup");
            check(LocalizationHelper.Writes == writes, "official per-frame name queries do not reinject localization");
            gather.CompleteForTest();
            pigeon.CompleteForTest();
        }
        foreach (var entry in searches) entry.Value.CompleteForTest();
        check(completed == 28, "all interaction callbacks survive localization refresh");
        foreach (var entry in searches) UnityEngine.Object.Destroy(entry.Value.gameObject);
        UnityEngine.Object.Destroy(gather.gameObject);
        UnityEngine.Object.Destroy(pigeon.gameObject);
        check(gather == null && pigeon == null && sign == null, "scene cleanup destroys the original objects and their label");
    }
}
