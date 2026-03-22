using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    public enum ModeFPhase
    {
        None = 0,
        Preparation = 1,
        Bounty = 2,
        HuntStorm = 3,
        Extraction = 4
    }

    public enum FortificationType
    {
        FoldableCover = 1,
        ReinforcedRoadblock = 2,
        BarbedWire = 3
    }

    public class ModeFState
    {
        public bool IsActive;
        public ModeFPhase CurrentPhase;
        public float PhaseElapsed;
        public float PhaseDuration;
        public float InitialMaxHealthSnapshot;
        public float TempMaxHealthGrowth;
        public int PlayerKillCount;
        public int PlayerBountyMarks;
        public CharacterMainControl CurrentBountyLeader;
        public int CurrentBountyLeaderMarks;
        public List<CharacterMainControl> ActiveBosses = new List<CharacterMainControl>();
        public HashSet<int> InitialBountyBossIds = new HashSet<int>();
        public Dictionary<int, int> BountyMarksByCharacterId = new Dictionary<int, int>();
        public Dictionary<int, ModeFFortificationMarker> ActiveFortifications = new Dictionary<int, ModeFFortificationMarker>();
        public CountDownArea ActiveExtractionArea;
        public bool ExtractionPointSpawned;
        public bool ExtractionResolved;
        public int RuntimeSessionToken;
        public float PhaseStatusBroadcastTimer;

        public void Reset()
        {
            IsActive = false;
            CurrentPhase = ModeFPhase.None;
            PhaseElapsed = 0f;
            PhaseDuration = 0f;
            InitialMaxHealthSnapshot = 0f;
            TempMaxHealthGrowth = 0f;
            PlayerKillCount = 0;
            PlayerBountyMarks = 0;
            CurrentBountyLeader = null;
            CurrentBountyLeaderMarks = 0;
            ActiveBosses.Clear();
            InitialBountyBossIds.Clear();
            BountyMarksByCharacterId.Clear();
            ActiveFortifications.Clear();
            ActiveExtractionArea = null;
            ExtractionPointSpawned = false;
            ExtractionResolved = false;
            RuntimeSessionToken = 0;
            PhaseStatusBroadcastTimer = 0f;
        }
    }

    public class ModeFFortificationMarker : MonoBehaviour
    {
        public FortificationType Type;
        public CharacterMainControl Owner;
        public int OwnerCharacterId;
        public float MaxHealth;
        public float LastKnownHealth;
        public bool IsDestroyed;
        public Health BoundHealth;
        public GameObject HighlightRoot;
        public float HighlightUntilTime;
        // L1: 静态变量跨模式重启累积，但 ID 只需唯一不需连续，int 溢出前需 ~2B 次放置，无需重置
        private static int nextFortId = 1;
        public int FortificationId;

        void Awake()
        {
            FortificationId = nextFortId++;
        }
    }

    public class ModeFBossDisplayNameMarker : MonoBehaviour
    {
        public string DisplayName;
        public Teams OriginalFaction = Teams.middle;
    }
}
