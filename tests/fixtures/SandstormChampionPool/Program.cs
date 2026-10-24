using System;
using System.Linq;
using System.Threading.Tasks;
using BossRush;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class Program
{
    private static int checks;
    private static void Check(bool condition, string label)
    {
        checks++;
        if (!condition) throw new Exception(label);
    }

    private static ModBehaviour Fresh()
    {
        Check(UniTask.Frames.Count == 0 && CharacterRandomPreset.Pending.Count == 0, "previous case drains all asynchronous preparation");
        foreach (GameObject go in UnityEngine.Object.All.OfType<GameObject>().ToArray()) UnityEngine.Object.Destroy(go);
        UnityEngine.Object.All.Clear(); Debug.Warnings.Clear(); Probe.Events.Clear();
        ModBehaviour.ModeG = ModBehaviour.ModeH = SceneLoader.IsSceneLoading = false;
        SceneManager.Current = new Scene { handle = 17, buildIndex = 7 };
        ItemAssetsCollection.Available = AstralStaffWeaponConfig.Valid = true;
        new CharacterRandomPreset { name = "Official_Red", nameKey = "Cname_Boss_Red" };
        return ModBehaviour.Instance = new ModBehaviour();
    }

    private static Task<CharacterMainControl> Spawn(ModBehaviour owner, Func<bool> active, bool nonWave = false, bool deferred = false)
    {
        return SandstormChampionPoolAdapter.SpawnAsync(owner, new Vector3(1f, 0f, 2f), active, nonWave, deferred).AsTask();
    }

    private static SandstormChampionController Controller(CharacterMainControl character)
    {
        return character.gameObject.GetComponent<SandstormChampionController>();
    }

    private static void CatalogueAndTwoInstances()
    {
        ModBehaviour owner = Fresh();
        SandstormChampionPoolAdapter.Register(owner); SandstormChampionPoolAdapter.Register(owner);
        Check(owner.Catalog.Count == 1, "catalogue registers champion once per owner");
        EnemyPresetInfo info = owner.Catalog.Single();
        Check(SandstormChampionPoolAdapter.IsPreset(info) && SandstormChampionPoolAdapter.IsPreset(SandstormChampionBoss.NameKey)
            && !SandstormChampionPoolAdapter.IsPreset((EnemyPresetInfo)null) && !SandstormChampionPoolAdapter.IsPreset("other"),
            "champion identity uses its stable key and is null safe");
        Check(info.team == (int)Teams.wolf && info.baseHealth == SandstormChampionConfig.BaseHealth
            && info.baseDamage == SandstormChampionConfig.DashDamage, "catalogue uses existing champion tuning and hostile faction");

        bool active = true;
        var firstPending = Spawn(owner, () => active);
        CharacterRandomPreset firstPreset = CharacterRandomPreset.LastCreatedFrom;
        Check(!firstPreset.hasSkill && firstPreset.skillPfb == null && !firstPreset.defaultWeaponOut,
            "real champion factory strips source combat before awaiting official creation");
        Check(Resources.FindObjectsOfTypeAll<CharacterRandomPreset>().Single(p => p.name == "Official_Red").hasSkill,
            "factory modifies a clone and preserves the official source preset");
        CharacterMainControl first = CharacterRandomPreset.Complete();
        Check(!firstPending.IsCompleted && owner.WaveBosses.Count == 0, "factory preparation yield precedes wave ownership");
        UniTask.Frame();
        Check(firstPending.Result == first && owner.CurrentBoss == first && owner.WaveBosses.Single() == first,
            "completed pool factory commits champion wave ownership");
        Check(first.Team == Teams.wolf && first.Released && first.gameObject.Active && first.isBossCharacter && first.dropBoxOnDead,
            "real factory retains hostile fallback, wakeup, activation and corpse reward prerequisites");
        Check(CharacterRandomPreset.RelatedScene == 7 && owner.StatApplications == 1 && first.MeleeScale == 1.3f
            && Controller(first).Baseline == .65f, "real factory records skill baseline before applying global boss scaling once");
        Check(Controller(first).Reward.TypeID == BossRushItemIds.AstralStaff
            && Controller(first).Reward.Durability == Controller(first).Reward.MaxDurability,
            "factory preconfigures the exclusive staff before encounter commit");

        var secondPending = Spawn(owner, () => active);
        CharacterRandomPreset secondPreset = CharacterRandomPreset.LastCreatedFrom;
        CharacterMainControl second = CharacterRandomPreset.Complete(); UniTask.Frame();
        Check(secondPending.Result == second && second != first && secondPreset != firstPreset
            && Controller(second) != Controller(first) && Controller(second).Reward != Controller(first).Reward,
            "concurrent champions own separate characters, presets, controllers and rewards");
        Check(owner.WaveBosses.Count == 2 && owner.CurrentBoss == second, "later champion owns current slot without removing first wave member");
        UnityEngine.Object.Destroy(first.gameObject);
        Check(owner.WaveBosses.Single() == second && owner.CurrentBoss == second && !secondPreset.Destroyed && firstPreset.Destroyed,
            "destroying one instance only removes its own wave lease and cloned resources");
        active = false;
        Check(!Controller(second).Current(), "committed encounter retains its run owner cancellation callback");
        UnityEngine.Object.Destroy(second.gameObject);
        Check(owner.WaveBosses.Count == 0 && owner.CurrentBoss == null && secondPreset.Destroyed,
            "last destroyed instance clears its wave lease and runtime preset");
    }

    private static void NonWaveAndReward()
    {
        ModBehaviour owner = Fresh();
        var pending = Spawn(owner, () => true, true); CharacterMainControl character = CharacterRandomPreset.Complete(); UniTask.Frame();
        SandstormChampionController controller = Controller(character); Item reward = controller.Reward;
        Check(pending.Result == character && owner.WaveBosses.Count == 0 && owner.CurrentBoss == null,
            "nonwave event never claims arena wave ownership");
        controller.TransferReward(); controller.TransferReward();
        Check(character.CharacterItem.Inventory.Content.Count(item => item == reward) == 1 && controller.Reward == null,
            "real production predeath method transfers staff exactly once");
        UnityEngine.Object.Destroy(character.gameObject);
        Check(!reward.gameObject.Destroyed, "instance disposal preserves reward already transferred into corpse inventory");

        owner = Fresh(); pending = Spawn(owner, () => true, true);
        character = CharacterRandomPreset.Complete(); UniTask.Frame(); controller = Controller(character); reward = controller.Reward;
        character.CharacterItem.Inventory.Accept = false; controller.TransferReward(); controller.TransferReward();
        Check(reward.Dropped && controller.Reward == null && character.CharacterItem.Inventory.Content.Count == 0,
            "real reward transfer falls back to one ground drop when inventory insertion fails");

        owner = Fresh(); pending = Spawn(owner, () => true, true);
        character = CharacterRandomPreset.Complete(); UniTask.Frame(); reward = Controller(character).Reward;
        UnityEngine.Object.Destroy(character.gameObject);
        Check(reward.gameObject.Destroyed, "controller boundary disposes an unreleased reward during cancellation");
    }

    private static void RejectedModes()
    {
        foreach (Action<ModBehaviour> setMode in new Action<ModBehaviour>[] {
            o => o.IsModeEActive = true, o => o.IsModeFActive = true, o => ModBehaviour.ModeG = true,
            o => ModBehaviour.ModeH = true, o => o.IsZombieModeActive = true, o => SceneLoader.IsSceneLoading = true })
        {
            ModBehaviour owner = Fresh(); setMode(owner);
            Task<CharacterMainControl> pending = Spawn(owner, () => true);
            Check(pending.IsCompleted && pending.Result == null && CharacterRandomPreset.Pending.Count == 0
                && owner.WaveBosses.Count == 0, "unsupported team, spectator, managed or zombie modes fail before official factory invocation");
        }
        ModBehaviour emptyOwner = Fresh(); emptyOwner.HasArenaEnemyPresetCatalog = false;
        SandstormChampionPoolAdapter.Register(emptyOwner);
        Check(emptyOwner.Catalog.Count == 0, "missing catalogue does not create partial registration");
    }

    private static void CancellationAndRewardFailure()
    {
        ModBehaviour owner = Fresh(); bool active = true;
        Task<CharacterMainControl> pending = Spawn(owner, () => active);
        CharacterRandomPreset preset = CharacterRandomPreset.LastCreatedFrom;
        active = false; CharacterMainControl character = CharacterRandomPreset.Complete();
        Check(pending.IsCompleted && pending.Result == null && character == null && preset.Destroyed && UniTask.Frames.Count == 0,
            "cancellation during official creation destroys character and cloned preset before preparation");

        owner = Fresh(); active = true; pending = Spawn(owner, () => active); character = CharacterRandomPreset.Complete();
        active = false; UniTask.Frame();
        Check(pending.Result == null && character == null && owner.WaveBosses.Count == 0,
            "cancellation at factory yield cannot commit an orphan wave boss");

        owner = Fresh(); pending = Spawn(owner, () => true); character = CharacterRandomPreset.Complete();
        ModBehaviour.Instance = new ModBehaviour(); UniTask.Frame();
        Check(pending.Result == null && character == null && owner.WaveBosses.Count == 0,
            "owner replacement invalidates the old factory completion");

        owner = Fresh(); pending = Spawn(owner, () => true); character = CharacterRandomPreset.Complete();
        SceneManager.Current = new Scene { handle = 18, buildIndex = 8 }; UniTask.Frame();
        Check(pending.Result == null && character == null, "scene transition cancels character preparation");

        owner = Fresh(); active = true; pending = Spawn(owner, () => active, deferred: true);
        character = CharacterRandomPreset.Complete();
        Check(!character.gameObject.Active && Controller(character) == null && !character.Released,
            "deferred factory deactivates the official active return before its first preparation yield");
        UniTask.Frame(); Item reward = ItemAssetsCollection.Last;
        Check(!pending.IsCompleted && owner.WaveBosses.Count == 0 && Controller(character) == null
            && !Probe.Events.Contains("initialize") && !Probe.Events.Contains("healthbar"),
            "extra deferred frame precedes controller initialization, activation, health bar and wave ownership");
        active = false; UniTask.Frame();
        Check(pending.Result == null && character == null && reward.gameObject.Destroyed && owner.WaveBosses.Count == 0
            && !Probe.Events.Contains("initialize") && !Probe.Events.Contains("healthbar"),
            "deferred cancellation destroys prepared reward and refuses controller creation or wave commit");

        owner = Fresh(); pending = Spawn(owner, () => true, deferred: true);
        character = CharacterRandomPreset.Complete(); UniTask.Frame(); UniTask.Frame();
        Check(pending.Result == character && Probe.Events.SequenceEqual(new[] {
            "active:False", "yield", "yield", "initialize", "active:True", "wake", "healthbar" }),
            "successful deferred production factory initializes combat only after both waits then explicitly activates and wakes once");

        foreach (bool available in new[] { false, true })
        {
            owner = Fresh(); ItemAssetsCollection.Available = available; AstralStaffWeaponConfig.Valid = false;
            pending = Spawn(owner, () => true); character = CharacterRandomPreset.Complete(); preset = CharacterRandomPreset.LastCreatedFrom;
            UniTask.Frame();
            Check(pending.Result == null && character == null && preset.Destroyed && owner.WaveBosses.Count == 0
                && (ItemAssetsCollection.Last == null || ItemAssetsCollection.Last.gameObject.Destroyed),
                "exclusive reward registration or configuration failure fails closed with full factory cleanup");
        }
    }

    private static void Main()
    {
        CatalogueAndTwoInstances(); NonWaveAndReward(); RejectedModes(); CancellationAndRewardFailure();
        Check(UniTask.Frames.Count == 0 && CharacterRandomPreset.Pending.Count == 0, "all pool factory tasks settle");
        Console.WriteLine("SandstormChampionPool: PASS (" + checks + " assertions; production factory, pool adapter and reward transfer)");
    }
}
