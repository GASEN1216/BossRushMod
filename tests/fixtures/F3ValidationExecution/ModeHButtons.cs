using System;
using System.Collections.Generic;

// Button/layout/native UI are stateful boundaries. The click, label resolution,
// production bet suffix and co-op page decoration are extracted verbatim.
namespace HButtonBoundary
{
    internal sealed class HButtonText { internal string text; }
    internal sealed class HButtonEvent { internal Action Handler; internal void Invoke() { if (Handler != null) Handler(); } }
    internal sealed class HButtonButton
    {
        internal bool isActiveAndEnabled = true, Interactable = true;
        internal string name;
        internal readonly HButtonText Text = new HButtonText();
        internal readonly HButtonEvent onClick = new HButtonEvent();
        internal bool IsInteractable() { return Interactable; }
        internal T GetComponentInChildren<T>() where T : class { return Text as T; }
    }
    internal sealed class HButtonGameObject
    {
        internal static HButtonGameObject Current;
        internal readonly List<HButtonButton> Buttons = new List<HButtonButton>();
        internal static HButtonGameObject Find(string name) { return name == "ModeH_Modal" ? Current : null; }
        internal T[] GetComponentsInChildren<T>(bool inactive) where T : class
        { return Buttons.ConvertAll(button => button as T).ToArray(); }
    }
    internal static class L10n
    {
        internal static bool Chinese;
        internal static string T(string cn, string en) { return Chinese ? cn : en; }
        internal static string T(string key) { return key; }
    }
    internal static class ModeHConfig { internal const string LocalizationKeyPrefix = "ModeH_"; }
    internal sealed class BossRushCoopPlayer { internal string Id; internal bool IsConnected = true; }
    internal static class BossRushCoop
    {
        internal static bool IsConnected, IsHost = true;
        internal static string LocalPlayerId;
        internal static readonly List<BossRushCoopPlayer> Players = new List<BossRushCoopPlayer>();
    }
    internal sealed class ModeHActionData { internal string Label; internal Action OnClick; }
    internal sealed class ModeHPageContent
    {
        internal readonly List<ModeHActionData> Actions = new List<ModeHActionData>();
        internal readonly List<string> OptionRows = new List<string>(), Lines = new List<string>();
    }
    internal sealed class ModeHCashBetRecord { internal string Text; }
    internal static class ModeHCashBetService
    { internal static bool IsAvailable = true, IsCustomStandingBet; internal static long StandingAmount; }
    internal static class ModeHItemBetStake { internal static bool HasSelection; internal static int SelectedCount; }
    internal sealed class View { internal string[] Exited, Funded; }
    internal sealed partial class ModeHRuntimeModule
    {
        internal static bool GroupModeEnabled = true;
        internal bool CoopLocalReady;
        internal readonly HashSet<string> _coopExited = new HashSet<string>();
        internal readonly HashSet<string> Funded = new HashSet<string>();
        internal View _coopView;
        internal int ConfirmedStartCalls;
        // This boundary observes which actual UI callback is invoked. The complete
        // funding/skip/refund policy is executed by the ModeHCoop fixture.
        private void StartCoopWithConfirmedPlayers() { ConfirmedStartCalls++; }
        internal ModeHCashBetRecord Carried;
        private ModeHCashBetRecord CarriedBetForCurrentMatch() { return Carried; }
        private static string DescribeRecordStake(ModeHCashBetRecord value) { return value.Text; }
        private static string FormatMoney(long value) { return value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture); }
        private bool IsCoopFunded(string id) { return Funded.Contains(id); }
        private static bool Contains(string[] values, string id) { return values != null && Array.IndexOf(values, id) >= 0; }
        internal void Decorate(ModeHPageContent page) { DecorateCoopBetPage(page); }
        internal string Suffix() { return DescribeStandingBetSuffix(); }
    }
    internal sealed class HButtonHost { internal ModeHRuntimeModule ModeHRuntime = new ModeHRuntimeModule(); }
    internal sealed partial class ProductionHButtonCase
    {
        internal readonly HButtonHost _host = new HButtonHost();
        internal readonly List<string> Actions = new List<string>();
        private void WriteRaw(string message) { Actions.Add(message); }
        internal bool Click(string label, string name = null) { return TryClickModeHButton(name, label); }
    }
    internal static class ModeHButtons
    {
        private static HButtonButton Render(ModeHPageContent page)
        {
            HButtonGameObject.Current = new HButtonGameObject();
            for (int i = 0; i < page.Actions.Count; i++)
            {
                var button = new HButtonButton { name = "ModeH_Action_" + i };
                button.Text.text = page.Actions[i].Label; button.onClick.Handler = page.Actions[i].OnClick;
                HButtonGameObject.Current.Buttons.Add(button);
            }
            return HButtonGameObject.Current.Buttons[0];
        }
        internal static void Run(Action<bool, string> check)
        {
            foreach (bool chinese in new[] { false, true })
            {
                L10n.Chinese = chinese; BossRushCoop.IsConnected = true; BossRushCoop.IsHost = true;
                BossRushCoop.LocalPlayerId = "host";
                BossRushCoop.Players.Clear();
                foreach (string id in new[] { "host", "guest1", "guest2" }) BossRushCoop.Players.Add(new BossRushCoopPlayer { Id = id });
                var test = new ProductionHButtonCase(); var runtime = test._host.ModeHRuntime;
                runtime.Funded.Add("guest1"); runtime.Funded.Add("guest2");
                int starts = 0;
                var page = new ModeHPageContent();
                page.Actions.Add(new ModeHActionData { Label = L10n.T("ModeH_Button_StartMatch") + " stale suffix", OnClick = () => starts++ });
                runtime.Decorate(page); HButtonButton button = Render(page);
                check(page.Actions.Count == 1, "H unfunded host has no force-start action");
                check(page.Actions[0].Label == L10n.T("确认押注并准备", "Confirm bet and ready"), "H real co-op page replaces start label");
                check(test.Click(L10n.T("ModeH_Button_StartMatch")) && starts == 1 && test.Actions.Count == 1, "H F3 clicks actual decorated ready button in both languages");
                button.Interactable = false;
                check(!test.Click(L10n.T("ModeH_Button_StartMatch")) && starts == 1, "H disabled button is never invoked");
                button.Interactable = true; button.isActiveAndEnabled = false;
                check(!test.Click(L10n.T("ModeH_Button_StartMatch")), "H inactive button is never invoked");
                button.isActiveAndEnabled = true; button.Text.text += " another action";
                check(!test.Click(L10n.T("ModeH_Button_StartMatch")), "H exact label cannot accidentally match an action sharing a prefix");
                HButtonGameObject.Current = null;
                check(!test.Click(L10n.T("ModeH_Button_StartMatch")), "H absent modal records no fictitious UI action");

                runtime.Funded.Add("host");
                var confirmedPage = new ModeHPageContent();
                confirmedPage.Actions.Add(new ModeHActionData { Label = "original", OnClick = () => starts++ });
                runtime.Decorate(confirmedPage); Render(confirmedPage);
                check(confirmedPage.Actions.Count == 2 && test.Click(L10n.T("开始并跳过未确认", "Start and skip unconfirmed"))
                    && runtime.ConfirmedStartCalls == 1 && starts == 1,
                    "H funded host rendered start action invokes its own callback, not ready again");
                BossRushCoop.IsHost = false;
                var guestPage = new ModeHPageContent();
                guestPage.Actions.Add(new ModeHActionData { Label = "original" });
                runtime.Decorate(guestPage);
                check(guestPage.Actions.Count == 1, "H guest never receives host force-start action");
                BossRushCoop.IsHost = true;

                BossRushCoop.IsConnected = false; ModeHCashBetService.StandingAmount = 2000;
                ModeHCashBetService.IsCustomStandingBet = false; ModeHItemBetStake.HasSelection = false;
                foreach (string label in new[] { "ModeH_Button_StartMatch", "ModeH_Button_LockIn" })
                {
                    page.Actions[0].Label = label + runtime.Suffix(); Render(page);
                    check(test.Click(label), "H solo actual cash suffix still reaches exact production button");
                    ModeHCashBetService.IsCustomStandingBet = true;
                    page.Actions[0].Label = label + runtime.Suffix(); Render(page);
                    check(test.Click(label), "H solo custom amount suffix still reaches exact production button");
                    ModeHCashBetService.IsCustomStandingBet = false;
                }
                ModeHItemBetStake.HasSelection = true; ModeHItemBetStake.SelectedCount = 3;
                page.Actions[0].Label = "ModeH_Button_StartMatch" + runtime.Suffix(); Render(page);
                check(test.Click("ModeH_Button_StartMatch"), "H real selected item suffix is accepted");
                runtime.Carried = new ModeHCashBetRecord { Text = "original stake" };
                page.Actions[0].Label = "ModeH_Button_StartMatch" + runtime.Suffix(); Render(page);
                check(test.Click("ModeH_Button_StartMatch"), "H carried original stake suffix is accepted without changing bet");
                ModeHItemBetStake.HasSelection = false; ModeHCashBetService.StandingAmount = 0;
            }
            BossRushCoop.IsConnected = false; HButtonGameObject.Current = null;
        }
    }
}
