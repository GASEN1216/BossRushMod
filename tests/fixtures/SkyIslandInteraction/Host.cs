// 测试宿主：只提供未抽取的引擎渲染和外部对象；所有被验证的决策/状态提交方法来自 Production.cs。
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BossRush
{
    internal sealed partial class SkyIslandStoryPresentation
    {
        internal string TextForTest { get { return shownText; } }
        internal string TitleForTest { get { return shownTitle; } }
        internal int SelectionForTest { get { return selected; } }
        internal int ChoicesForTest { get { return buttons.Count; } }
        internal string LabelForTest(int index) { return shownChoices[index].Label; }
        internal bool PendingForTest { get { return pendingPage != null || selecting; } }
        internal void ClickForTest(int index) { buttons[index].onClick.Invoke(); }
        internal void SelectForTest(int index) { Select(index); }
        internal void DispatchForTest(Func<string> callback) { RunChoice(callback); }
        internal void DestroyCanvasForTest() { UnityEngine.Object.Destroy(canvas.gameObject); }
        internal float BottomForTest { get { return panelRect.anchoredPosition.y - panelRect.sizeDelta.y * .5f; } }
        internal void Show(string title, string text, IList<Choice> choices) { Show(title, text, choices, null, null); }
        private void SubscribeInput() { inputSubscribed = true; }
        private void UnsubscribeInput() { inputSubscribed = false; }
        private static RectTransform BuildBackground(RectTransform panel, Sprite background) { return panel; }
        private static void BuildHero(RectTransform panel, TextMeshProUGUI title, Sprite portrait, Sprite banner,
            float height, float width, float titleBlock, float cursor, Action close) { }
        private void BuildBody(RectTransform panel, TextMeshProUGUI text, float height, float natural, float cursor) { }
        private static Image KeyCap(RectTransform parent, string label, float width, Vector2 position)
        { return new GameObject("key-cap").AddComponent<Image>(); }
    }

    internal sealed class SkyIslandSession
    {
        internal bool IsReady = true, HasPlantingDelivered = true;
        internal SkyIslandServices Services;
    }
    internal sealed class TestStory
    {
        internal readonly SkyIslandStoryData Current = SkyIslandStoryRules.CreateDefault();
        internal bool AcceptRecord;
        internal int Records;
        internal void LogTiming(string action, string name) { }
        internal bool RecordSearch(string key, out string message)
        {
            Records++;
            message = AcceptRecord ? "recorded" : "save blocked";
            if (AcceptRecord) Current.flags |= (int)SkyIslandPuzzles.For(key).Flag;
            return AcceptRecord;
        }
    }
    internal sealed class TestDialogue { internal void Dispose() { } }
    internal sealed class SkyIslandFieldcraft { internal SkyIslandGnats Gnats; }

    internal sealed partial class SkyIslandWorldStory
    {
        internal readonly SkyIslandSession session;
        internal readonly TestStory story = new TestStory();
        internal readonly SkyIslandStoryPresentation presentation = new SkyIslandStoryPresentation();
        private readonly SkyIslandPuzzleState puzzles = new SkyIslandPuzzleState();
        private readonly List<string> hiddenHints = new List<string>();
        private Action reopen;
        private TestDialogue dialogue;
        private SkyIslandFieldcraft fieldcraft;
        internal SkyIslandWorldStory(SkyIslandSession session) { this.session = session; }
        private string OverlookGuarded(string key) { return null; }
        internal void BindAudioForTest(SkyIslandGnats gnats) { fieldcraft = new SkyIslandFieldcraft { Gnats = gnats }; }
        internal void ShowHealForTest()
        {
            reopen = ShowHealForTest;
            hiddenHints.Clear();
            var choices = new List<SkyIslandStoryPresentation.Choice>();
            HealChoice(choices);
            presentation.Show("remedy", "intro", choices);
        }
        internal void ShowRepairForTest()
        {
            reopen = ShowRepairForTest;
            hiddenHints.Clear();
            var choices = new List<SkyIslandStoryPresentation.Choice>();
            RepairChoice(choices);
            presentation.Show("refit", "intro", choices);
        }
        internal void ShowMealForTest()
        {
            reopen = ShowMealForTest;
            hiddenHints.Clear();
            var choices = new List<SkyIslandStoryPresentation.Choice>();
            MealChoice(choices);
            presentation.Show("meal", WithNextStep("intro"), choices);
        }
        internal void ShowHintForTest()
        {
            reopen = ShowHintForTest;
            hiddenHints.Clear();
            Hint(L10n.T("还要清理林间道路", "Clear the woodland path next"));
            var choices = new List<SkyIslandStoryPresentation.Choice>();
            choices.Add(new SkyIslandStoryPresentation.Choice("record", () => Refreshed(true, "recorded")));
            presentation.Show("beacon", WithNextStep("intro"), choices);
        }
        internal void ShowNoHintForTest()
        {
            // 合成页面不重新登记世界提示；Refreshed 必须清掉上个入口的提示。
            reopen = ShowNoHintForTest;
            var choices = new List<SkyIslandStoryPresentation.Choice>();
            choices.Add(new SkyIslandStoryPresentation.Choice("craft", () => Refreshed(true, "crafted")));
            presentation.Show("workbench", "materials", choices);
        }
        internal void ShowPuzzleForTest(SkyIslandPuzzle puzzle)
        {
            // 这里只提供宿主页面；生产 ReadPoint 的 solving 门另由结构守卫钉住。
            reopen = () => ShowPuzzleForTest(puzzle);
            hiddenHints.Clear();
            var choices = new List<SkyIslandStoryPresentation.Choice>();
            if (puzzles.IsSolved(puzzle))
            {
                if (!story.Current.Has(puzzle.Flag))
                    choices.Add(new SkyIslandStoryPresentation.Choice("retry record", delegate
                    {
                        string message;
                        return Refreshed(story.RecordSearch(puzzle.Key, out message), message);
                    }));
            }
            else PuzzleChoices(choices, puzzle, null);
            presentation.Show("puzzle", puzzles.IsSolved(puzzle) ? "solved" : PuzzleBody(puzzle), choices);
        }
    }

    internal sealed partial class SkyIslandServices
    {
        private readonly CharacterMainControl player;
        private readonly GameObject root = new GameObject("island");
        private readonly int groundMask = 1, raidSeed = 7;
        private float healReadyAt;
        private bool mealUsed, disposed;
        internal bool BadgeForTest, NeedsRepairForTest = true;
        internal int RepairsForTest, MealsForTest;
        internal SkyIslandServices(CharacterMainControl player) { this.player = player; }
        internal bool MealEaten { get { return mealUsed; } }
        internal void SetReadyAtForTest(float time) { healReadyAt = time; }
        internal void DisposeForTest() { disposed = true; }
        private static bool AccountAvailable { get { return true; } }
        private bool CarriesBadge() { return BadgeForTest; }
        // 维修与实际属性系统不在本夹具范围：这里仅提供 ServiceChoice 的可观察成交后状态。
        internal SkyIslandServiceReadiness RepairReadiness(out int price)
        { price = 60; return NeedsRepairForTest ? SkyIslandServiceReadiness.Ready : SkyIslandServiceReadiness.NothingToDo; }
        internal string Repair() { NeedsRepairForTest = false; RepairsForTest++; return "repaired"; }
        private bool ApplyMeal() { MealsForTest++; mealUsed = true; return true; }
    }

    internal sealed partial class SkyIslandGnats
    {
        private bool buzzing;
        private GameObject buzzEmitter;
        private readonly MethodInfo stopAll = typeof(Duckov.AudioObject).GetMethod("StopAll", BindingFlags.Instance | BindingFlags.NonPublic);
        private readonly object stopImmediately = Duckov.StopMode.Immediate;
        internal static void RemedyClearsItch() { }
        internal Duckov.AudioObject StartBuzzForTest()
        {
            buzzEmitter = new GameObject("buzz");
            var audio = buzzEmitter.AddComponent<Duckov.AudioObject>();
            buzzing = true;
            return audio;
        }
        internal void DestroyEmitterForTest() { UnityEngine.Object.Destroy(buzzEmitter); }
    }
}
