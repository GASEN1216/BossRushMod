using System;
using System.Collections.Generic;

namespace Duckov.UI.Animations
{
    public class FadeGroup { public bool Visible; public void Show() { Visible = true; } public void Hide() { Visible = false; } }
}
namespace NodeCanvas.DialogueTrees
{
    public interface IDialogueActor { }
    public interface IStatement { }
    public class SubtitlesRequestInfo
    {
        public Action done;
        public IStatement statement;
        public SubtitlesRequestInfo(IDialogueActor a, IStatement s, Action done) { this.done = done; statement = s; }
    }
    public class MultipleChoiceRequestInfo
    {
        public Action<int> done;
        public Dictionary<IStatement, int> options;
        public MultipleChoiceRequestInfo(IDialogueActor a, Dictionary<IStatement, int> s, float timeout, Action<int> done)
        { this.done = done; options = s; }
    }
    public static class DialogueTree
    {
        public static readonly List<Action> Lines = new List<Action>();
        public static readonly List<string> ShownText = new List<string>();
        public static readonly List<Action<int>> Choices = new List<Action<int>>();
        public static void RequestSubtitles(SubtitlesRequestInfo info)
        {
            Lines.Add(info.done);
            ShownText.Add(((SodaCraft.Localizations.LocalizedStatement)info.statement).Text);
        }
        public static readonly List<string[]> ShownChoices = new List<string[]>();
        public static void RequestMultipleChoices(MultipleChoiceRequestInfo info)
        {
            Choices.Add(info.done);
            var labels = new string[info.options.Count];
            foreach (var option in info.options)
                labels[option.Value] = ((SodaCraft.Localizations.LocalizedStatement)option.Key).Text;
            ShownChoices.Add(labels);
        }
    }
}
namespace SodaCraft.Localizations
{
    public class LocalizedStatement : NodeCanvas.DialogueTrees.IStatement
    {
        private readonly string key;
        public LocalizedStatement(string key) { this.key = key; }
        public string Text { get { return BossRush.LocalizationHelper.Texts[key]; } }
    }
}
namespace Dialogues
{
    public class DialogueUI
    {
        public static DialogueUI instance = new DialogueUI();
        private Duckov.UI.Animations.FadeGroup mainFadeGroup = new Duckov.UI.Animations.FadeGroup();
        private Duckov.UI.Animations.FadeGroup textAreaFadeGroup = new Duckov.UI.Animations.FadeGroup();
        public static event Action OnDialogueStatusChanged;
        public static void HideTextFadeGroup() { }
        public static bool Active { get { return instance != null && instance.mainFadeGroup.Visible; } }
        // 官方 WaitForChoice 读的私有字段（生产按名字反射）；取消多选时生产会把它置 0 推出选项。
        private int confirmedChoice = -1;
        public int ConfirmedChoiceForTest { get { return confirmedChoice; } }
        public static int ConfirmCalls;
        public static bool ConfirmCompletesRequests = true;
        // 官方 Confirm 推进正在显示的字幕：替身按「最后一条字幕请求」的完成回调来推。
        public void Confirm()
        {
            ConfirmCalls++;
            var lines = NodeCanvas.DialogueTrees.DialogueTree.Lines;
            if (ConfirmCompletesRequests && lines.Count > 0) lines[lines.Count - 1]();
        }
    }
}
public static class InputManager
{
    public static bool Disabled;
    public static void DisableInput(UnityEngine.GameObject token) { Disabled = true; }
    public static void ActiveInput(UnityEngine.GameObject token) { Disabled = false; }
}
namespace BossRush
{
    public static class L10n
    {
        public static bool Chinese;
        public static string T(string cn, string en) { return Chinese ? cn : en; }
    }
    public static class LocalizationHelper
    {
        public static readonly Dictionary<string, string> Texts = new Dictionary<string, string>();
        public static void InjectLocalization(string key, string text) { Texts[key] = text; }
    }
}
