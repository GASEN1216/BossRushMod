using System;
using NodeCanvas.DialogueTrees;
using UnityEngine;

namespace UnityEngine
{
    public struct Vector3 { public Vector3(float x, float y, float z) { } }
    public class Sprite : Object { }
}
namespace NodeCanvas.DialogueTrees { public interface IDialogueActor { } }

// 官方序列化字段/getter 的最小替身；字段必须默认空，由真实 Factory 反射装配。
#pragma warning disable 0649, 0169
public class DuckovDialogueActor : Component, IDialogueActor
{
    private string id;
    private string nameKey;
    private Vector3 offset;
    private Sprite _portraitSprite;
    public string ID { get { return id; } }
    public string NameKey { get { return nameKey; } }
    public Sprite portraitSprite { get { return _portraitSprite; } }
    public static DuckovDialogueActor Get(string value) { return null; }
}
#pragma warning restore 0649, 0169

namespace BossRush
{
    internal sealed class NpcConfig { internal string DisplayName; }
    internal static class SkyIslandUiArt
    {
        internal static Sprite Portrait;
        internal static Sprite GetPortrait(string id) { return Portrait; }
    }
    internal static partial class ActorReuseRegression
    {
        internal static void Run(Func<string,CharacterMainControl> create, Action<bool,string> check)
        {
            foreach (string id in new[] { "sky_qinghe", "sky_weibai" })
            foreach (bool marriageFirst in new[] { false, true })
            foreach (bool chinese in new[] { false, true })
            {
                DialogueActorFactory.ResetStaticCaches(); LocalizationHelper.Text.Clear();
                L10n.IsChinese = chinese; SkyIslandUiArt.Portrait = new Sprite();
                CharacterMainControl npc = create(id);
                var first = marriageFirst ? NPCMarriageSystem.MarriageActor(id, npc.transform)
                    : (DuckovDialogueActor)EnsureActor(id, npc.transform);
                string key = first.NameKey, actorId = first.ID;
                L10n.IsChinese = !chinese;
                var story = (DuckovDialogueActor)EnsureActor(id, npc.transform);
                check(ReferenceEquals(first, story) && story.NameKey == key && story.ID == actorId,
                    "actor reuse preserves identity and localization key");
                check(LocalizationHelper.Text[story.NameKey] == SkyIslandWorldStory.ResidentName(id),
                    "story updates the name actually read by official UI");
                check(story.portraitSprite == SkyIslandUiArt.Portrait, "story attaches available portrait even after wedding first");
                L10n.IsChinese = chinese;
                var replay = NPCMarriageSystem.MarriageActor(id, npc.transform);
                check(ReferenceEquals(first, replay) && LocalizationHelper.Text[replay.NameKey] == SkyIslandWorldStory.ResidentName(id),
                    "wedding replay also refreshes a story-created actor's actual name");
                Sprite portrait = replay.portraitSprite;
                SkyIslandUiArt.Portrait = null;
                EnsureActor(id, npc.transform);
                check(replay.portraitSprite == portrait, "missing resource does not erase an existing portrait");
            }
            L10n.IsChinese = true;
        }
    }
}
