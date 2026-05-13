// ============================================================================
// BossRushLootboxInteractables.cs - lootbox, carry, return, and trash-can interactables
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using Duckov.ItemUsage;
using Duckov.Scenes;
using Duckov.Economy;
using System.Reflection;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using Duckov.UI.DialogueBubbles;
using Duckov.UI;
using UnityEngine.AI;
using Duckov.ItemBuilders;

namespace BossRush
{
    public class BossRushLootboxMarker : MonoBehaviour
    {
        public BossRushTrackedLootboxMode TrackedMode = BossRushTrackedLootboxMode.None;
        public int SessionToken = 0;
        private InteractableLootbox cachedLootbox;

        internal InteractableLootbox CachedLootbox
        {
            get
            {
                if (cachedLootbox == null)
                {
                    try
                    {
                        cachedLootbox = GetComponent<InteractableLootbox>();
                    }
                    catch {}
                }

                return cachedLootbox;
            }
        }

        private void OnEnable()
        {
            BossRushLootboxUtility.RegisterMarkedLootboxMarker(this);
        }

        private void OnDisable()
        {
            BossRushLootboxUtility.UnregisterMarkedLootboxMarker(this);
        }

        private void OnDestroy()
        {
            BossRushLootboxUtility.UnregisterMarkedLootboxMarker(this);
        }
    }

    internal static class BossRushLootboxUtility
    {
        private static readonly List<BossRushLootboxMarker> MarkedLootboxMarkers = new List<BossRushLootboxMarker>();
        private static readonly Collider[] NearbyLootboxHits = new Collider[128];
        private static readonly Queue<LootboxDecorationRequest> PendingLootboxDecorationRequests = new Queue<LootboxDecorationRequest>();
        private const int DecorateLootboxesRetryFrames = 15;
        private const int LootboxDecorationQueriesPerFrame = 3;
        private static bool lootboxDecorationWorkerRunning = false;

        private struct LootboxDecorationRequest
        {
            public ModBehaviour Owner;
            public Vector3 DeathPosition;
            public bool RegisterSweepTracking;
            public float Radius;
            public int RemainingAttempts;
        }

        internal static void RegisterMarkedLootboxMarker(BossRushLootboxMarker marker)
        {
            if (marker == null)
            {
                return;
            }

            for (int i = MarkedLootboxMarkers.Count - 1; i >= 0; i--)
            {
                BossRushLootboxMarker existing = MarkedLootboxMarkers[i];
                if (!TryGetLootboxFromMarker(existing, out _))
                {
                    MarkedLootboxMarkers.RemoveAt(i);
                    continue;
                }

                if (ReferenceEquals(existing, marker))
                {
                    return;
                }
            }

            if (TryGetLootboxFromMarker(marker, out _))
            {
                MarkedLootboxMarkers.Add(marker);
            }
        }

        internal static void UnregisterMarkedLootboxMarker(BossRushLootboxMarker marker)
        {
            for (int i = MarkedLootboxMarkers.Count - 1; i >= 0; i--)
            {
                BossRushLootboxMarker existing = MarkedLootboxMarkers[i];
                if (!TryGetLootboxFromMarker(existing, out _) || ReferenceEquals(existing, marker))
                {
                    MarkedLootboxMarkers.RemoveAt(i);
                }
            }
        }

        private static bool TryGetLootboxFromMarker(BossRushLootboxMarker marker, out InteractableLootbox lootbox)
        {
            lootbox = null;
            if (marker == null || marker.gameObject == null)
            {
                return false;
            }

            try
            {
                lootbox = marker.CachedLootbox;
            }
            catch
            {
                lootbox = null;
            }

            return lootbox != null && lootbox.gameObject != null;
        }

        internal static bool IsMarkedLootbox(InteractableLootbox lootbox, int requiredSceneBuildIndex = int.MinValue)
        {
            if (lootbox == null || lootbox.gameObject == null)
            {
                return false;
            }

            if (requiredSceneBuildIndex != int.MinValue &&
                lootbox.gameObject.scene.buildIndex != requiredSceneBuildIndex)
            {
                return false;
            }

            BossRushLootboxMarker marker = null;
            try
            {
                marker = lootbox.GetComponent<BossRushLootboxMarker>();
            }
            catch {}

            return marker != null;
        }

        internal static bool StampLootboxMarker(
            InteractableLootbox lootbox,
            BossRushTrackedLootboxMode mode,
            int sessionToken)
        {
            if (lootbox == null || lootbox.gameObject == null || sessionToken <= 0)
            {
                return false;
            }

            BossRushLootboxMarker marker = null;
            try
            {
                marker = lootbox.GetComponent<BossRushLootboxMarker>();
            }
            catch {}

            if (marker == null)
            {
                return false;
            }

            marker.TrackedMode = mode;
            marker.SessionToken = sessionToken;
            return true;
        }

        internal static bool IsMarkedLootboxForSession(
            InteractableLootbox lootbox,
            BossRushTrackedLootboxMode mode,
            int sessionToken,
            int requiredSceneBuildIndex = int.MinValue)
        {
            if (!IsMarkedLootbox(lootbox, requiredSceneBuildIndex))
            {
                return false;
            }

            if (sessionToken <= 0 || mode == BossRushTrackedLootboxMode.None)
            {
                return false;
            }

            BossRushLootboxMarker marker = null;
            try
            {
                marker = lootbox.GetComponent<BossRushLootboxMarker>();
            }
            catch {}

            return marker != null &&
                   marker.TrackedMode == mode &&
                   marker.SessionToken == sessionToken;
        }

        internal static bool IsEmptyLootbox(InteractableLootbox lootbox)
        {
            if (lootbox == null)
            {
                return false;
            }

            try
            {
                Inventory inv = lootbox.Inventory;
                if (inv == null)
                {
                    return true;
                }

                return inv.IsEmpty();
            }
            catch
            {
                return false;
            }
        }

        internal static int CollectMarkedLootboxes(
            List<InteractableLootbox> output,
            int requiredSceneBuildIndex = int.MinValue,
            bool onlyEmpty = false)
        {
            return CollectMarkedLootboxesFromRegistry(
                output,
                requiredSceneBuildIndex,
                onlyEmpty);
        }

        internal static int CollectMarkedLootboxesForSession(
            List<InteractableLootbox> output,
            BossRushTrackedLootboxMode mode,
            int sessionToken,
            int requiredSceneBuildIndex = int.MinValue,
            bool onlyEmpty = false)
        {
            return CollectMarkedLootboxesFromRegistry(
                output,
                requiredSceneBuildIndex,
                onlyEmpty,
                mode,
                sessionToken,
                true);
        }

        internal static int CollectMarkedLootboxesFromRegistry(
            List<InteractableLootbox> output,
            int requiredSceneBuildIndex = int.MinValue,
            bool onlyEmpty = false,
            BossRushTrackedLootboxMode mode = BossRushTrackedLootboxMode.None,
            int sessionToken = 0,
            bool requireSessionFilter = false)
        {
            if (output == null)
            {
                return 0;
            }

            output.Clear();

            if (requireSessionFilter && (sessionToken <= 0 || mode == BossRushTrackedLootboxMode.None))
            {
                return 0;
            }

            for (int i = MarkedLootboxMarkers.Count - 1; i >= 0; i--)
            {
                BossRushLootboxMarker marker = MarkedLootboxMarkers[i];
                InteractableLootbox lootbox = null;
                if (!TryGetLootboxFromMarker(marker, out lootbox))
                {
                    MarkedLootboxMarkers.RemoveAt(i);
                    continue;
                }

                if (requiredSceneBuildIndex != int.MinValue &&
                    lootbox.gameObject.scene.buildIndex != requiredSceneBuildIndex)
                {
                    continue;
                }

                if (requireSessionFilter &&
                    (marker.TrackedMode != mode || marker.SessionToken != sessionToken))
                {
                    continue;
                }

                if (onlyEmpty && !IsEmptyLootbox(lootbox))
                {
                    continue;
                }

                output.Add(lootbox);
            }

            return output.Count;
        }

        internal static int DestroyLootboxes(List<InteractableLootbox> lootboxes)
        {
            int removed = 0;
            if (lootboxes == null)
            {
                return 0;
            }

            for (int i = 0; i < lootboxes.Count; i++)
            {
                InteractableLootbox box = lootboxes[i];
                if (box == null || box.gameObject == null)
                {
                    continue;
                }

                try
                {
                    if (box.gameObject != null)
                    {
                        removed++;
                        UnityEngine.Object.Destroy(box.gameObject);
                    }
                }
                catch {}
            }

            return removed;
        }

        internal static int DestroyMarkedLootboxes(int requiredSceneBuildIndex = int.MinValue, bool onlyEmpty = false)
        {
            List<InteractableLootbox> boxes = new List<InteractableLootbox>();
            CollectMarkedLootboxes(boxes, requiredSceneBuildIndex, onlyEmpty);
            return DestroyLootboxes(boxes);
        }

        internal static void DecorateLootbox(
            InteractableLootbox lootbox,
            ModBehaviour owner = null,
            bool registerSweepTracking = false,
            bool includeCarryInteraction = false)
        {
            if (lootbox == null || lootbox.gameObject == null)
            {
                return;
            }

            try
            {
                if (lootbox.GetComponentInParent<PetAI>() != null)
                {
                    return;
                }
            }
            catch {}

            try
            {
                if (lootbox.GetComponentInParent<PetProxy>() != null)
                {
                    return;
                }
            }
            catch {}

            try
            {
                BossRushLootboxMarker marker = lootbox.GetComponent<BossRushLootboxMarker>();
                if (marker == null)
                {
                    lootbox.gameObject.AddComponent<BossRushLootboxMarker>();
                }
            }
            catch {}

            BossRushDeleteLootboxInteractable deleteInteract = null;
            try
            {
                deleteInteract = lootbox.gameObject.GetComponent<BossRushDeleteLootboxInteractable>();
            }
            catch {}

            if (deleteInteract == null)
            {
                try
                {
                    deleteInteract = lootbox.gameObject.AddComponent<BossRushDeleteLootboxInteractable>();
                }
                catch {}
            }

            BossRushCarryInteractable carryInteract = null;
            if (includeCarryInteraction)
            {
                try
                {
                    carryInteract = lootbox.gameObject.GetComponent<BossRushCarryInteractable>();
                    if (carryInteract == null)
                    {
                        carryInteract = lootbox.gameObject.AddComponent<BossRushCarryInteractable>();
                    }
                }
                catch {}
            }

            try
            {
                lootbox.interactableGroup = true;

                System.Reflection.FieldInfo othersField = ReflectionCache.InteractableBase_OtherInterablesInGroup;
                if (othersField != null)
                {
                    System.Collections.Generic.List<InteractableBase> hostList =
                        othersField.GetValue(lootbox) as System.Collections.Generic.List<InteractableBase>;
                    if (hostList == null)
                    {
                        hostList = new System.Collections.Generic.List<InteractableBase>();
                        othersField.SetValue(lootbox, hostList);
                    }

                    if (deleteInteract != null && !hostList.Contains(deleteInteract))
                    {
                        hostList.Add(deleteInteract);
                    }
                    if (carryInteract != null && !hostList.Contains(carryInteract))
                    {
                        hostList.Add(carryInteract);
                    }
                }
            }
            catch {}

            if (!registerSweepTracking)
            {
                return;
            }

            try
            {
                if (owner == null)
                {
                    owner = ModBehaviour.Instance;
                }

                if (owner != null)
                {
                    owner.TryRegisterModeEFLootbox(lootbox);
                }
            }
            catch {}
        }

        private static void EnqueueLootboxDecorationRequest(ModBehaviour owner, Vector3 deathPosition, bool registerSweepTracking, float radius)
        {
            LootboxDecorationRequest request = new LootboxDecorationRequest();
            request.Owner = owner;
            request.DeathPosition = deathPosition;
            request.RegisterSweepTracking = registerSweepTracking;
            request.Radius = radius > 0f ? radius : 3f;
            request.RemainingAttempts = DecorateLootboxesRetryFrames;
            PendingLootboxDecorationRequests.Enqueue(request);

            if (lootboxDecorationWorkerRunning)
            {
                return;
            }

            ModBehaviour runner = owner != null ? owner : ModBehaviour.Instance;
            if (runner == null)
            {
                return;
            }

            try
            {
                lootboxDecorationWorkerRunning = true;
                runner.StartCoroutine(ProcessQueuedLootboxDecorations());
            }
            catch
            {
                lootboxDecorationWorkerRunning = false;
            }
        }

        private static IEnumerator ProcessQueuedLootboxDecorations()
        {
            try
            {
                while (PendingLootboxDecorationRequests.Count > 0)
                {
                    yield return null;

                    int processedThisFrame = 0;
                    while (processedThisFrame < LootboxDecorationQueriesPerFrame &&
                           PendingLootboxDecorationRequests.Count > 0)
                    {
                        LootboxDecorationRequest request = PendingLootboxDecorationRequests.Dequeue();
                        bool completed = TryProcessLootboxDecorationRequest(request);
                        request.RemainingAttempts--;

                        if (!completed && request.RemainingAttempts > 0)
                        {
                            PendingLootboxDecorationRequests.Enqueue(request);
                        }

                        processedThisFrame++;
                    }
                }
            }
            finally
            {
                lootboxDecorationWorkerRunning = false;
            }
        }

        private static bool TryProcessLootboxDecorationRequest(LootboxDecorationRequest request)
        {
            try
            {
                int hitCount = 0;
                try
                {
                    hitCount = Physics.OverlapSphereNonAlloc(request.DeathPosition, request.Radius, NearbyLootboxHits, -1);
                }
                catch {}

                if (hitCount <= 0)
                {
                    return false;
                }

                for (int i = 0; i < hitCount; i++)
                {
                    Collider col = NearbyLootboxHits[i];
                    if (col == null)
                    {
                        continue;
                    }

                    InteractableLootbox lootbox = null;
                    try
                    {
                        lootbox = col.GetComponent<InteractableLootbox>();
                    }
                    catch {}

                    if (lootbox == null)
                    {
                        continue;
                    }

                    DecorateLootbox(lootbox, request.Owner, request.RegisterSweepTracking);
                }

                return true;
            }
            catch
            {
                return true;
            }
        }

        internal static IEnumerator DecorateLootboxesNearPosition(ModBehaviour owner, Vector3 deathPosition, bool registerSweepTracking, float radius = 3f)
        {
            EnqueueLootboxDecorationRequest(owner, deathPosition, registerSweepTracking, radius);
            yield break;
        }
    }

    public class BossRushDeleteLootboxInteractable : InteractableBase
    {
        private InteractableLootbox lootbox;

        protected override void Awake()
        {
            try
            {
                this.overrideInteractName = true;
                // 使用本地化键，避免两边出现 *
                this.InteractName = "BossRush_Delete";
                this._overrideInteractNameKey = "BossRush_Delete";
            }
            catch {}
            try
            {
                this.interactCollider = GetComponent<Collider>();
            }
            catch {}
            try
            {
                base.Awake();
            }
            catch {}
            try
            {
                this.finishWhenTimeOut = true;
                if (this.interactCollider != null)
                {
                    this.interactCollider.enabled = true;
                }
                this.MarkerActive = false;
            }
            catch {}
            try
            {
                this.lootbox = GetComponent<InteractableLootbox>();
            }
            catch {}
        }

        protected override void Start()
        {
            try
            {
                base.Start();
            }
            catch {}
            try
            {
                this.overrideInteractName = true;
                // 使用本地化键，避免两边出现 *
                this.InteractName = "BossRush_Delete";
                this._overrideInteractNameKey = "BossRush_Delete";
            }
            catch {}
        }

        protected override bool IsInteractable()
        {
            return true;
        }

        protected override void OnTimeOut()
        {
            try
            {
                InteractableLootbox target = this.lootbox;
                if (target == null)
                {
                    try
                    {
                        target = GetComponent<InteractableLootbox>();
                    }
                    catch {}
                }
                if (target != null && target.gameObject != null)
                {
                    UnityEngine.Object.Destroy(target.gameObject);
                }
            }
            catch {}
        }
    }

    public class BossRushCarryInteractable : InteractableBase
    {
        public Carriable carryTarget;

        private bool isCarrying = false;
        private CharacterMainControl carrier = null;
        private Transform carrierRoot = null;
        private Rigidbody rb = null;
        private InteractableLootbox lootbox = null;
        private Vector3 carryOffset = new Vector3(0f, 1f, 0.8f);

        protected override void Awake()
        {
            try
            {
                this.overrideInteractName = true;
                this._overrideInteractNameKey = "BossRush_Carry_Up";
            }
            catch {}
            try
            {
                this.interactCollider = GetComponent<Collider>();
            }
            catch {}
            try
            {
                base.Awake();
            }
            catch {}
            try
            {
                this.finishWhenTimeOut = true;
                if (this.interactCollider != null)
                {
                    this.interactCollider.enabled = true;
                }
                this.MarkerActive = false;
            }
            catch {}

            try
            {
                this.lootbox = GetComponent<InteractableLootbox>();
                this.rb = GetComponentInChildren<Rigidbody>();
            }
            catch {}
        }

        protected override void Start()
        {
            try
            {
                base.Start();
            }
            catch {}
            try
            {
                this.overrideInteractName = true;
                this._overrideInteractNameKey = "BossRush_Carry_Up";
            }
            catch {}
        }

        protected override bool IsInteractable()
        {
            return true;
        }

        protected override void OnInteractFinished()
        {
            if (!this.interactCharacter)
            {
                return;
            }

            CharacterMainControl character = this.interactCharacter;
            base.StopInteract();

            if (!this.isCarrying)
            {
                StartPseudoCarry(character);
            }
            else
            {
                StopPseudoCarry();
            }
        }

        private void StartPseudoCarry(CharacterMainControl character)
        {
            if (character == null)
            {
                return;
            }

            this.carrier = character;
            this.isCarrying = true;

            try
            {
                this.carrierRoot = null;
                try
                {
                    this.carrierRoot = this.carrier.modelRoot;
                }
                catch {}
            }
            catch {}

            if (this.rb == null)
            {
                try
                {
                    this.rb = GetComponentInChildren<Rigidbody>();
                }
                catch {}
            }

            try
            {
                if (this.rb != null)
                {
                    if (!this.rb.isKinematic)
                    {
                        this.rb.velocity = Vector3.zero;
                        this.rb.angularVelocity = Vector3.zero;
                    }
                    this.rb.isKinematic = true;
                }
            }
            catch {}

            try
            {
                if (this.lootbox == null)
                {
                    this.lootbox = GetComponent<InteractableLootbox>();
                }
                if (this.lootbox != null && this.lootbox.interactCollider != null)
                {
                    this.lootbox.interactCollider.isTrigger = true;
                }
            }
            catch {}

            try
            {
                this.overrideInteractName = true;
                this._overrideInteractNameKey = "BossRush_Carry_Down";
            }
            catch {}
        }

        private void StopPseudoCarry()
        {
            this.isCarrying = false;

            try
            {
                if (this.lootbox == null)
                {
                    this.lootbox = GetComponent<InteractableLootbox>();
                }

                // 尝试将箱子放到玩家朝向方向前方一点的地面上，而不是玩家脚下，避免把玩家挤进地形
                try
                {
                    Vector3 dropPos = base.transform.position;

                    if (this.carrier != null)
                    {
                        // 使用角色的瞄准方向（CurrentAimDirection），再投影到水平面上作为前方方向
                        Vector3 fwd = this.carrier.CurrentAimDirection;
                        fwd.y = 0f;
                        if (fwd.sqrMagnitude < 0.0001f)
                        {
                            // 退回到 transform.forward，防止极端情况下长度为 0
                            fwd = this.carrier.transform.forward;
                            fwd.y = 0f;
                        }
                        if (fwd.sqrMagnitude < 0.0001f)
                        {
                            fwd = this.carrier.transform.forward;
                        }
                        fwd.Normalize();

                        Vector3 baseForwardPos = this.carrier.transform.position + fwd * 1.0f;
                        Vector3 origin = baseForwardPos + Vector3.up * 1f;
                        Vector3 dir = Vector3.down;
                        float maxDist = 5f;

                        // 只使用地面层，避免命中高处的墙体等碰撞体导致箱子出现在半空/天上
                        LayerMask mask = Duckov.Utilities.GameplayDataSettings.Layers.groundLayerMask;
                        RaycastHit hit;
                        if (Physics.Raycast(origin, dir, out hit, maxDist, mask, QueryTriggerInteraction.Ignore))
                        {
                            dropPos = hit.point + Vector3.up * 0.3f;
                        }
                        else
                        {
                            // 回退方案：直接放在玩家前方一点、略微抬高的位置
                            dropPos = baseForwardPos + Vector3.up * 0.3f;
                        }
                    }

                    if (this.rb != null)
                    {
                        this.rb.position = dropPos;
                        if (this.rb.isKinematic)
                        {
                            this.rb.isKinematic = false;
                        }
                        this.rb.velocity = Vector3.zero;
                        this.rb.angularVelocity = Vector3.zero;
                    }
                    else
                    {
                        base.transform.position = dropPos;
                    }
                }
                catch {}

                if (this.lootbox != null && this.lootbox.interactCollider != null)
                {
                    this.lootbox.interactCollider.isTrigger = false;
                }
            }
            catch {}

            try
            {
                if (this.rb != null)
                {
                    this.rb.isKinematic = false;
                }
            }
            catch {}

            this.carrier = null;
            this.carrierRoot = null;

            try
            {
                this.overrideInteractName = true;
                this._overrideInteractNameKey = "BossRush_Carry_Up";
            }
            catch {}
        }

        private void LateUpdate()
        {
            if (!this.isCarrying || this.carrier == null)
            {
                return;
            }

            try
            {
                Transform root = this.carrierRoot;
                if (root == null)
                {
                    try
                    {
                        root = this.carrier.modelRoot;
                        this.carrierRoot = root;
                    }
                    catch {}
                }

                Vector3 targetPos;
                if (root != null)
                {
                    targetPos = root.TransformPoint(this.carryOffset);
                }
                else
                {
                    Vector3 forward = this.carrier.transform.forward;
                    targetPos = this.carrier.transform.position + forward * this.carryOffset.z + Vector3.up * this.carryOffset.y;
                }

                if (this.rb != null)
                {
                    this.rb.MovePosition(targetPos);
                }
                else
                {
                    base.transform.position = targetPos;
                }
            }
            catch {}
        }
    }

    /// <summary>
    /// Boss Rush 结束后返回出生点的交互类
    /// </summary>
    public class BossRushReturnInteractable : MonoBehaviour
    {
        private bool playerNear = false;

        void Start()
        {
            var col = GetComponent<Collider>();
            if (col != null)
            {
                col.isTrigger = true;
            }
        }

        void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Player") || other.name.Contains("Player"))
            {
                playerNear = true;
                if (BossRush.ModBehaviour.Instance != null)
                {
                    BossRush.ModBehaviour.Instance.ShowMessage(L10n.T("按E键返回出生点！", "Press E to return to spawn!"));
                }
            }
        }

        void OnTriggerExit(Collider other)
        {
            if (other.CompareTag("Player") || other.name.Contains("Player"))
            {
                playerNear = false;
            }
        }

        void Update()
        {
            if (playerNear && UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.E))
            {
                if (BossRush.ModBehaviour.Instance != null)
                {
                    BossRush.ModBehaviour.Instance.ReturnToBossRushStart();
                }

                gameObject.SetActive(false);
            }
        }
    }

    /// <summary>
    /// 垃圾桶交互组件 - 提供清空箱子功能
    /// <para>在路牌旁边显示，一直可见，包含"清空所有箱子"和"清空空箱子"两个选项</para>
    /// <para>第一个选项直接显示"清空所有箱子"，不显示"垃圾桶"</para>
    /// </summary>
    public class TrashCanInteractable : InteractableBase
    {
        private bool optionsInjected = false;
        private List<InteractableBase> groupOptions = new List<InteractableBase>();

        protected override void Awake()
        {
            try
            {
                // 设置为交互组，包含多个子选项
                this.interactableGroup = true;

                // 直接显示"清空所有箱子"而不是"垃圾桶"
                this.overrideInteractName = true;
                this._overrideInteractNameKey = "BossRush_ClearAllLootboxes";
                this.InteractName = "BossRush_ClearAllLootboxes";

                // 交互标记放在垃圾桶中部
                this.interactMarkerOffset = new Vector3(0f, 0f, 0f);
            }
            catch {}

            try
            {
                base.Awake();
            }
            catch {}

            try
            {
                // 一直显示交互标记
                this.MarkerActive = true;
            }
            catch {}
        }

        protected override void Start()
        {
            try
            {
                base.Start();
            }
            catch {}

            // 注入清空箱子选项
            if (!optionsInjected)
            {
                InjectClearOptions();
            }
        }

        /// <summary>
        /// 注入清空箱子选项（仅清空空箱子，因为主交互已经是清空所有箱子）
        /// </summary>
        private void InjectClearOptions()
        {
            try
            {
                optionsInjected = true;

                // 获取或创建 otherInterablesInGroup 列表
                var field = typeof(InteractableBase).GetField("otherInterablesInGroup",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field == null) return;

                var list = field.GetValue(this) as List<InteractableBase>;
                if (list == null)
                {
                    list = new List<InteractableBase>();
                    field.SetValue(this, list);
                }

                // 只添加"清空空箱子"选项，因为主交互已经是"清空所有箱子"
                GameObject clearEmptyObj = new GameObject("TrashCanOption_ClearEmpty");
                clearEmptyObj.transform.SetParent(transform);
                clearEmptyObj.transform.localPosition = Vector3.zero;
                var clearEmptyInteract = clearEmptyObj.AddComponent<BossRushClearEmptyLootboxesInteractable>();
                list.Add(clearEmptyInteract);
                groupOptions.Add(clearEmptyInteract);

                ModBehaviour.DevLog("[BossRush] TrashCanInteractable: 已注入清空空箱子选项");
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[BossRush] TrashCanInteractable.InjectClearOptions 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 主交互触发时执行清空所有箱子
        /// </summary>
        protected override void OnTimeOut()
        {
            try
            {
                int removed = BossRushLootboxUtility.DestroyMarkedLootboxes();

                try
                {
                    var main = CharacterMainControl.Main;
                    if (main != null)
                    {
                        string msg = L10n.T(
                            "已清空 " + removed + " 个箱子",
                            "Cleared " + removed + " lootboxes");
                        main.PopText(msg, -1f);
                    }
                }
                catch {}

                ModBehaviour.DevLog("[BossRush] TrashCanInteractable: 已清空 " + removed + " 个箱子");
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[BossRush] TrashCanInteractable.OnTimeOut 失败: " + e.Message);
            }
        }

        protected override bool IsInteractable()
        {
            try
            {
                ModBehaviour mod = ModBehaviour.Instance;
                if (mod == null)
                {
                    return false;
                }

                // 只要 BossRush 激活、ModeD 激活、或竞技场激活，都允许交互
                bool bossRushActive = false;
                bool modeDActive = false;
                bool arenaActive = false;

                try { bossRushActive = mod.IsActive; } catch {}
                try { modeDActive = mod.IsModeDActive; } catch {}
                try { arenaActive = mod.IsBossRushArenaActive; } catch {}

                return bossRushActive || modeDActive || arenaActive;
            }
            catch {}

            return false;
        }
    }
}
