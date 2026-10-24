// 真实执行 helper 的登记、显式改名、刷新、注销、重置和 actor 查找节流。
// 官方 actor 和本地化字典是显式替身；不证明对话窗口实际绘制或实机镜头效果。
using System;
using BossRush;
using UnityEngine;

namespace UnityEngine { internal sealed class Camera : Object { } }
internal sealed class DuckovDialogueActor : Component
{
    internal string NameKey;
    internal string DisplayedName
    {
        get { string value; return LocalizationHelper.Texts.TryGetValue(NameKey, out value) ? value : NameKey; }
    }
}

namespace BossRush.Utils
{
    internal static partial class NPCNameTagHelper
    {
        internal static void RunNameVisibilityChecks(Action<bool, string> check)
        {
            ResetStaticCaches();
            Time.unscaledTime = 100f;
            var target = new GameObject("friendly NPC");
            var actor = target.AddComponent<DuckovDialogueActor>();
            actor.NameKey = "BossRush_TestOfficialActor";
            LocalizationHelper.InjectLocalization(actor.NameKey, "Owner Name");
            int objects = GameObject.CreatedCount, components = target.Components.Count, writes = LocalizationHelper.Writes;
            RegisterOriginalHealthBarName(target.transform, "出生中文", 2.2f, "test");
            for (int i = 0; i < 360; i++) RefreshOriginalHealthBarName(target.transform);
            check(actor.DisplayedName == "Owner Name" && LocalizationHelper.Writes == writes,
                "registration preserves the dialogue owner's initial name");
            check(GameObject.CreatedCount == objects && target.Components.Count == components,
                "friendly NPC registration and refresh create no independent name UI or components");
            UpdateOriginalHealthBarDisplayName(target.transform, "English Name");
            check(actor.DisplayedName == "English Name" && target.GetComponent<DuckovDialogueActor>() == actor,
                "explicit name update refreshes the existing official actor key");
            writes = LocalizationHelper.Writes;
            for (int i = 0; i < 120; i++)
            {
                RefreshOriginalHealthBarName(target.transform);
                UpdateOriginalHealthBarDisplayName(target.transform, "English Name");
            }
            check(LocalizationHelper.Writes == writes, "stable actor names do not repeatedly inject localization");
            target.SetActive(false);
            UpdateOriginalHealthBarDisplayName(target.transform, "隐藏姓名");
            check(actor.DisplayedName == "隐藏姓名" && !target.activeSelf,
                "hidden NPC name refresh preserves its active state");

            var delayed = new GameObject("actor created after language switch");
            RegisterOriginalHealthBarName(delayed.transform, "旧中文姓名", 2.2f, "test");
            var lateActor = delayed.AddComponent<DuckovDialogueActor>();
            lateActor.NameKey = "BossRush_TestLateActor";
            LocalizationHelper.InjectLocalization(lateActor.NameKey, "Current English Name");
            writes = LocalizationHelper.Writes;
            for (int i = 0; i < 120; i++) RefreshOriginalHealthBarName(delayed.transform);
            check(lateActor.DisplayedName == "Current English Name" && LocalizationHelper.Writes == writes,
                "late actor keeps its current-language owner name instead of stale registration text");

            var missing = new GameObject("missing actor");
            objects = GameObject.CreatedCount;
            components = missing.Components.Count;
            RegisterOriginalHealthBarName(missing.transform, "Initial", 2.2f, "test");
            UpdateOriginalHealthBarDisplayName(missing.transform, "Pending English");
            int queries = missing.GetComponentCalls;
            for (int i = 0; i < 120; i++) { Time.unscaledTime = 100.5f; RefreshOriginalHealthBarName(missing.transform); }
            check(queries == 1 && missing.GetComponentCalls == queries,
                "missing actor lookup waits one second before retrying");
            check(GameObject.CreatedCount == objects && missing.Components.Count == components,
                "missing actor refresh never creates an actor Health or UI component");
            Time.unscaledTime = 101f;
            RefreshOriginalHealthBarName(missing.transform);
            check(missing.GetComponentCalls == queries + 1, "pending name retries actor lookup at the one-second boundary");
            var recoveredActor = missing.AddComponent<DuckovDialogueActor>();
            recoveredActor.NameKey = "BossRush_TestRecoveredActor";
            LocalizationHelper.InjectLocalization(recoveredActor.NameKey, "Owner Default");
            Time.unscaledTime = 101.5f;
            RefreshOriginalHealthBarName(missing.transform);
            check(recoveredActor.DisplayedName == "Owner Default", "new actor waits for the scheduled pending-name retry");
            Time.unscaledTime = 102f;
            RefreshOriginalHealthBarName(missing.transform);
            check(recoveredActor.DisplayedName == "Pending English", "pending name reaches the actor when it becomes available");

            var vanilla = new GameObject("vanilla NPC");
            var vanillaActor = vanilla.AddComponent<DuckovDialogueActor>();
            vanillaActor.NameKey = "Character_NPC_Jeff";
            LocalizationHelper.InjectLocalization(vanillaActor.NameKey, "Jeff");
            RegisterOriginalHealthBarName(vanilla.transform, "杰夫", 2.2f, "test");
            writes = LocalizationHelper.Writes;
            UpdateOriginalHealthBarDisplayName(vanilla.transform, "Wrong Override");
            for (int i = 0; i < 120; i++) RefreshOriginalHealthBarName(vanilla.transform);
            check(vanillaActor.DisplayedName == "Jeff" && LocalizationHelper.Writes == writes,
                "official actor name keys cannot be overwritten by the helper");

            UnregisterOriginalHealthBarName(target.transform);
            writes = LocalizationHelper.Writes;
            check(actor != null && target.GetComponent<DuckovDialogueActor>() == actor,
                "unregister preserves the dialogue owner's actor");
            check(!UpdateOriginalHealthBarDisplayName(target.transform, "Resurrected")
                && !OriginalHealthBarEntriesByTransformId.ContainsKey(target.transform.GetInstanceID())
                && LocalizationHelper.Writes == writes,
                "name update cannot resurrect an unregistered actor binding");
            ResetStaticCaches();
            check(actor != null && lateActor != null && recoveredActor != null && vanillaActor != null,
                "reset never destroys official actors owned by dialogue systems");
            check(OriginalHealthBarEntriesByTransformId.Count == 0
                && !UpdateOriginalHealthBarDisplayName(missing.transform, "Reset Resurrected"),
                "reset removes all helper registrations without allowing update resurrection");
            UnityEngine.Object.Destroy(target);
            UnityEngine.Object.Destroy(delayed);
            UnityEngine.Object.Destroy(missing);
            UnityEngine.Object.Destroy(vanilla);
        }
    }
}
