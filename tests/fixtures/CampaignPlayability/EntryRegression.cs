using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using BossRush;

namespace BossRush
{
    internal static class BossRushInitialSpawn { internal static bool HasArrived(Vector3 point) { return false; } }
    internal static class BossRushMapSelectionHelper { internal static void ClearPendingEntryFlowState() { } }
    internal static class ModeGInteractable
    {
        internal static bool IsConfirmationOpen, LastConfirmationAttemptedStart;
        internal static bool TryOpenConfirmation(ModBehaviour owner) { return false; }
    }
    internal sealed class BossRushMapConfig { internal Vector3? defaultSignPos; }
    internal sealed class BossRushSignInteractable : Component
    { internal BoxCollider interactCollider; internal Vector3 interactMarkerOffset; }
    internal static class EntityModelFactory
    {
        // Geometry input adapter: the real prefab pivot sits 0.8m above its ground anchor.
        internal static bool Fallback;
        internal static GameObject CreateSignpost(Vector3 point, Quaternion rotation)
        {
            var sign = new GameObject();
            sign.transform.position = point + Vector3.up*(Fallback ? 0f : 0.8f);
            sign.transform.rotation = rotation;
            if (!Fallback) sign.AddComponent<MeshRenderer>();
            return sign;
        }
    }
    internal sealed partial class UIAndSignsRuntimeModule
    {
        private readonly ModBehaviour owner;
        private BossRushSignInteractable bossRushSignInteract;
        private object _signInteractBase;
        private GameObject _bossRushSignGameObject;
        internal UIAndSignsRuntimeModule(ModBehaviour value) { owner = value; }
        internal BossRushSignInteractable SignInteract { get { return bossRushSignInteract; } }
        private void RemoveRigidbodyAndSetTrigger(GameObject sign) { }
        private void CreateTrashCanNextToSignpost(Vector3 point, Quaternion rotation, bool right) { }
    }
    internal sealed class IntegrationRuntimeModule
    {
        internal void StartArenaEntryContinuation(IEnumerator routine) { }
    }
    public partial class ModBehaviour
    {
        private readonly IntegrationRuntimeModule bossRushIntegrationRuntime = new IntegrationRuntimeModule();
        internal Vector3 EntryTarget;
        internal BossRushMapConfig CurrentMap;
        internal readonly UIAndSignsRuntimeModule uiAndSignsRuntime;
        private Vector3 demoChallengeStartPosition;
        private CharacterMainControl playerCharacter;
        private BossRushEntryMode DetermineBossRushEntryMode(string context) { return BossRushEntryMode.Normal; }
        public static Vector3 GetCurrentSceneDefaultPosition() { return Instance.EntryTarget; }
        internal static BossRushMapConfig GetCurrentMapConfig() { return Instance.CurrentMap; }
        internal void SetArenaCenterFromSign_UIAndSigns(Vector3 point) { }
        internal IEnumerator DemoEntry(Scene scene) { return SetupBossRushInDemoChallenge(scene); }
        internal void CreateSignForTest(Vector3? position = null)
        {
            ModBehaviour.Instance = this;
            bossRushArenaActive = true;
            if (uiAndSignsRuntime.SignInteract != null) UnityEngine.Object.Destroy(uiAndSignsRuntime.SignInteract.gameObject);
            uiAndSignsRuntime.TryCreateArenaDifficultyEntryPoint_UIAndSigns(position ?? CharacterMainControl.Main.transform.position);
        }
        public IEnumerator StartCoroutine(IEnumerator routine) { return routine; }
        private void ScheduleModeEStartupWarmup(string context) { }
        private void PreCacheMapSpawnerPositions() { }
        private void DisableAllSpawners() { }
        private void ClearEnemiesForBossRush() { }
        private bool TryStartModeE() { return false; }
        private bool TryStartModeF() { return false; }
        private bool TryStartModeD() { return false; }
        private IEnumerator WaitForModeEStartupVerification(Action<bool> result) { result(false); yield break; }
        private void SpawnCommonNPCs(string context) { }
        private void ScheduleRestoreFollowingSpouse(string scene, string context) { }
        private void StopModeEStartupWarmupIfPending() { }
        private void TryRefundModeGPendingPrepaidTicket() { }
        private void CreateRescueTeleportBubble() { }
        private void TryCreateArenaDifficultyEntryPoint() { uiAndSignsRuntime.TryCreateArenaDifficultyEntryPoint_UIAndSigns(null); }
        private IEnumerator EnsureArenaEntryPointCreated() { yield break; }
    }
}

internal static class EntryRegression
{
    internal static void Run(Action<bool, string> check)
    {
        CampaignProgressService.Active = "ch6";
        CampaignProgressService.State = CampaignChapterState.ContractActive;
        SceneLoader.IsSceneLoading = false;
        LevelManager.AfterInit = true;
        SceneManager.ActiveHandle = 90;
        CharacterMainControl.Main = new CharacterMainControl { Health = new Health(), gameObject = new GameObject() };
        CharacterMainControl.Main.transform.position = new Vector3(-100f, -8f, -100f);
        var owner = new ModBehaviour { Arena = true, bossRushArenaActive = true,
            EntryTarget = new Vector3(235f, -8f, 202f),
            CurrentMap = new BossRushMapConfig { defaultSignPos = new Vector3(600f, -8f, 700f) } };
        ModBehaviour.Instance = owner;
        Physics.GroundY = -8f;
        IEnumerator entry = owner.DemoEntry(new Scene { handle = 90, name = "Level_DemoChallenge_1" });
        check(entry.MoveNext() && entry.Current is WaitForSeconds, "real demo entry yields before its own second teleport");
        owner.TickCampaignFinalBossAltar();
        check(owner.CampaignRuntime.AltarForTest == null && !owner.CanStartCampaignFinalBoss(),
            "official AfterInit cannot create the altar before BossRush's real second teleport and roadsign creation");
        while (entry.MoveNext()) { }
        check(CharacterMainControl.Main.PositionCalls == 1 && CharacterMainControl.Main.transform.position.x == 235f,
            "production demo entry performs its second teleport into the configured arena");
        BossRushSignInteractable sign = owner.CampaignArenaSignForRuntime;
        check(sign != null && sign.transform.position.x == 600f && sign.transform.position.y > -8f,
            "real roadsign creation uses its distinct configured sign position and the prefab's raised pivot");
        CharacterMainControl.Main.transform.position = new Vector3(500f, 0f, 500f);
        Time.unscaledTime += 2f;
        owner.TickCampaignFinalBossAltar();
        GameObject altar = owner.CampaignRuntime.AltarForTest;
        check(altar != null && altar.transform.position.x == 600f && altar.transform.position.z == 703f
            && altar.transform.position.y == -8f && UnityEngine.AI.NavMesh.LastRaw.y > -8f,
            "real geometry finds the altar beside the actual roadsign and raycasts its raised pivot back to ground");
        owner.TickCampaignFinalBossAltar();
        check(ReferenceEquals(altar, owner.CampaignRuntime.AltarForTest), "real entry produces one final altar");
        sign.gameObject.activeInHierarchy = false;
        owner.TickCampaignFinalBossAltar();
        check(altar == null && !owner.CanStartCampaignFinalBoss(), "inactive actual roadsign dismisses the altar");
        sign.gameObject.activeInHierarchy = true;
        SceneManager.ActiveHandle = 91;
        check(!owner.CanStartCampaignFinalBoss(), "previous-scene roadsign cannot satisfy the current arena gate");
        SceneManager.ActiveHandle = 90;
        UnityEngine.Object.Destroy(sign.gameObject);
        check(!owner.CanStartCampaignFinalBoss(), "destroyed roadsign and owned component cannot satisfy the arena gate");

        SceneManager.ActiveHandle = 92;
        SceneManager.ActiveName = "CustomArena";
        owner.CreateSignForTest(new Vector3(900f, -8f, 950f));
        Time.unscaledTime += 2f;
        owner.TickCampaignFinalBossAltar();
        check(owner.CampaignRuntime.AltarForTest != null && owner.CampaignRuntime.AltarForTest.transform.position.x == 900f,
            "real custom-position roadsign is also the final altar anchor");
        owner.CleanupCampaignFinalBoss(true);
        UnityEngine.Object.Destroy(owner.CampaignArenaSignForRuntime.gameObject);
        SceneManager.ActiveHandle = 93;
        SceneManager.ActiveName = "Level_ChallengeSnow";
        owner.CreateSignForTest(new Vector3(1100f, -8f, 1200f));
        Time.unscaledTime += 2f;
        owner.TickCampaignFinalBossAltar();
        check(owner.CampaignArenaSignForRuntime != null && owner.CampaignArenaSignForRuntime.transform.position.y == -8f
            && owner.CampaignRuntime.AltarForTest != null && owner.CampaignRuntime.AltarForTest.transform.position.z == 1203f,
            "real ChallengeSnow invisible entry component supplies the final altar anchor without a visible sign model");
        owner.CleanupCampaignFinalBoss(true);
        UnityEngine.Object.Destroy(owner.CampaignArenaSignForRuntime.gameObject);
        SceneManager.ActiveName = "Level_DemoChallenge_1";
        Physics.GroundY = 0f;
    }
}
