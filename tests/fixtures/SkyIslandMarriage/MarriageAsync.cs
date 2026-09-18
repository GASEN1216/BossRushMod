using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Saves { internal static class SavesSystem { internal static int CurrentSlot; } }
namespace BossRush
{
    // 视频/UI/player loop 是可控替身；入口和跨 await 的控制流来自生产文件。
    internal static class MarriageAsync
    {
        internal static readonly List<Task> Tasks = new List<Task>();
        internal static readonly Queue<TaskCompletionSource<bool>> Delays = new Queue<TaskCompletionSource<bool>>();
        internal static TaskCompletionSource<bool> Video, Dialogue;
        internal static Func<bool> VideoValid;
        internal static bool VideoResult = true;
        internal static int Feedback, DialogueCalls, ForceEnds;
        internal static void Reset()
        {
            foreach (Task task in Tasks) if (!task.IsCompleted) throw new Exception("unreleased marriage task");
            Tasks.Clear(); Delays.Clear(); Video = Dialogue = null; VideoValid = null;
            VideoResult = true; Feedback = DialogueCalls = ForceEnds = 0;
        }
        internal static void FinishDelay() { Delays.Dequeue().SetResult(true); }
        internal static void Forget(this Task task) { Tasks.Add(task); }
        internal static void Drain()
        { foreach (Task task in Tasks) { if (!task.IsCompleted) throw new Exception("unfinished action"); task.GetAwaiter().GetResult(); } }
    }
    public interface INPCController
    {
        void StartDialogue(); void EndDialogueWithStay(float seconds, bool farewell);
        void ShowBrokenHeartBubble(); void ShowLoveHeartBubble();
    }
    internal static class UniTask
    {
        internal static Task Delay(TimeSpan interval)
        { var wait = new TaskCompletionSource<bool>(); MarriageAsync.Delays.Enqueue(wait); return wait.Task; }
    }
    internal static class DialogueManager
    {
        internal static async Task ShowDialogueSequenceBilingual(object actor, string[][] body, string prefix, CancellationToken token)
        {
            MarriageAsync.DialogueCalls++;
            var wait = MarriageAsync.Dialogue;
            if (wait != null)
                using (token.Register(() => wait.TrySetCanceled())) await wait.Task;
        }
        internal static void ForceEndDialogue() { MarriageAsync.ForceEnds++; }
    }
    internal static class NPCDialogueSystem
    { internal static void ShowDialogue(string id, Transform target, string text, float duration) { MarriageAsync.Feedback++; } }
    public static partial class NPCMarriageSystem
    {
        private static Task<bool> PlayMarriageVideoCutsceneAsync(string id, Func<bool> valid = null)
        {
            MarriageAsync.VideoValid = valid;
            return MarriageAsync.Video == null ? Task.FromResult(MarriageAsync.VideoResult) : MarriageAsync.Video.Task;
        }
        internal static DuckovDialogueActor MarriageActor(string id, Transform npc) { return (DuckovDialogueActor)ResolveDialogueActor(id, npc); }
    }
}
