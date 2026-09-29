// 遗种崽独立背包：官方 Inventory / LootView.LootItem 格子与拖拽，物品树按 pet.id 随 Bundle_v2 保存。
// 仅在点击“背包”时恢复物品；切图 / 重伤先封存再回收，异步恢复必须核对 owner 和存档槽。
using System;
using System.Collections.Generic;
using BossRush.Utils;
using Cysharp.Threading.Tasks;
using Duckov.UI;
using ItemStatsSystem;
using ItemStatsSystem.Data;
using Saves;
using UnityEngine;

namespace BossRush
{
    internal sealed class PetNestBackpack : InteractableBase
    {
        private const string InteractKey = "BossRush_PetNest_Backpack";
        private static readonly HashSet<PetNestBackpack> Active = new HashSet<PetNestBackpack>();
        private string _petId;
        private int _slot;
        private int _generation;
        private Item _container;
        private Inventory _inventory;
        private bool _loading;
        private bool _ready;
        private bool _disposed;
        private bool _subscribed;
        private bool _dirty;
        private bool _wasOpen;
        private bool _retired;
        private bool _durable;
        private bool _destroying;
        private CharacterMainControl _assetOwner;
        private float _nextSaveTime;

        internal static void Attach(CharacterMainControl character, PetNestPetRecord pet)
        {
            if (character == null || pet == null || string.IsNullOrEmpty(pet.id)) return;
            // 未保存的旧容器仍拥有这些物品，不允许按旧磁盘记录再生成第二份。
            foreach (PetNestBackpack existing in Active)
                if (existing != null && !existing._disposed && existing._slot == SavesSystem.CurrentSlot
                    && existing._petId == pet.id) return;
            GameObject root = new GameObject("PetNestBackpack");
            root.transform.SetParent(character.transform, false);
            SphereCollider collider = root.AddComponent<SphereCollider>();
            collider.radius = 1.4f;
            collider.center = new Vector3(0f, 0.5f, 0f);
            collider.isTrigger = true;
            PetNestBackpack backpack = root.AddComponent<PetNestBackpack>();
            backpack._petId = pet.id;
            backpack._slot = SavesSystem.CurrentSlot;
            // 角色 modelRoot 已按幼体缩放；交互点不继承模型的再次缩放。
            float height = 1.2f;
            if (character.modelRoot != null) height = Mathf.Max(0.7f, character.modelRoot.localScale.y * 1.6f);
            backpack.interactMarkerOffset = new Vector3(0f, height, 0f);
            Active.Add(backpack);
        }

        protected override void Awake()
        {
            overrideInteractName = true;
            _overrideInteractNameKey = InteractKey;
            interactCollider = GetComponent<Collider>();
            NPCInteractionGroupHelper.GetOrCreateGroupList(this, "[PetNestBackpack]");
            base.Awake();
        }

        protected override bool IsInteractable()
        {
            return !_disposed && !_retired && !_loading && _slot == SavesSystem.CurrentSlot
                && !SceneLoader.IsSceneLoading && !string.IsNullOrEmpty(_petId) && PetNestPersistenceAccess.CanStoreAll;
        }

        protected override void OnTimeOut()
        {
            base.OnTimeOut();
            OpenAsync().Forget();
        }

        private async UniTaskVoid OpenAsync()
        {
            if (!IsInteractable() || LootView.Instance == null) return;
            string error;
            if (!PetNestSaveCoordinator.RequireBackpackAssetSnapshot(out error))
            {
                NotifyFailure(error);
                return;
            }
            int generation = _generation;
            _assetOwner = CharacterMainControl.Main;
            _loading = true;
            Item pending = null;
            try
            {
                PetNestPetRecord pet = PetNestService.TryGetPet(_petId);
                if (pet == null) return;
                if (!_ready)
                {
                    PetNestBackpackSnapshot snapshot = PetNestBackpackSnapshot.Decode(pet.backpackJson);
                    int capacity = Mathf.Max(1, PetNestCompanionRuntime.ResolveCapacityBonus(pet));
                    foreach (PetNestBackpackEntry entry in snapshot.Items)
                    {
                        foreach (ItemTreeData.DataEntry node in entry.Tree.entries)
                        {
                            BossRushDynamicItemRegistry.EnsureRegistered(node.typeID);
                            if (ItemAssetsCollection.GetPrefab(node.typeID) == null)
                                throw new InvalidOperationException("backpack_prefab_missing:" + node.typeID);
                        }
                        // 旧物品槽位永不因成长规则改变而被截断。
                        capacity = Mathf.Max(capacity, entry.Position + 1);
                    }
                    GameObject storage = new GameObject("PetNestBackpackItems");
                    // 存储与角色分开销毁：场景卸载时 Unity 不保证父子 OnDestroy 顺序，
                    // 角色回收前仍必须能读到完整物品树。Dispose 是这份临时根的唯一 owner。
                    UnityEngine.Object.DontDestroyOnLoad(storage);
                    _container = storage.AddComponent<Item>();
                    _container.CreateInventoryComponent();
                    _inventory = _container.Inventory;
                    _inventory.DisplayNameKey = InteractKey;
                    _inventory.NeedInspection = false;
                    _inventory.SetCapacity(capacity);
                    foreach (PetNestBackpackEntry entry in snapshot.Items)
                    {
                        using (PetNestBackpackRestoration restoration = new PetNestBackpackRestoration())
                        {
                            foreach (ItemTreeData.DataEntry node in entry.Tree.entries)
                            {
                                restoration.CreateNode(node);
                                // 一帧只恢复一个节点；取消时 using 回收本次全部已创建节点。
                                await UniTask.Yield();
                                if (this == null || _disposed || generation != _generation || _slot != SavesSystem.CurrentSlot
                                    || _assetOwner != CharacterMainControl.Main)
                                    return;
                            }
                            pending = restoration.Connect(entry.Tree);
                            if (pending == null || !_inventory.AddAt(pending, entry.Position))
                                throw new InvalidOperationException("backpack_restore_failed");
                            restoration.Transfer();
                            pending = null;
                        }
                    }
                    if (snapshot.Locks != null)
                        foreach (int index in snapshot.Locks)
                            _inventory.LockIndex(index);
                    SubscribeInventory();
                    _ready = true;
                    _durable = true;
                }
                if (_disposed || generation != _generation || _slot != SavesSystem.CurrentSlot
                    || _assetOwner != CharacterMainControl.Main) return;
                _inventory.SetCapacity(Mathf.Max(_inventory.GetLastItemPosition() + 1,
                    Mathf.Max(1, PetNestCompanionRuntime.ResolveCapacityBonus(pet))));
                if (LootView.Instance.open) LootView.Instance.Close();
                LootView.LootItem(_container);
                _wasOpen = true;
            }
            catch (Exception e)
            {
                if (pending != null) pending.DestroyTree();
                // 部分恢复失败绝不写回空背包，原快照保持可重试。
                if (!_ready) DestroyContainer();
                Debug.LogWarning("[PetNest] 背包打开失败: " + e.Message);
                NotifyFailure("backpack_unavailable");
            }
            finally { _loading = false; }
        }

        private void SubscribeInventory()
        {
            if (_subscribed || _inventory == null) return;
            _inventory.onContentChanged += OnContentChanged;
            _inventory.onInventorySorted += OnInventorySorted;
            _inventory.onSetIndexLock += OnContentChanged;
            _container.onItemTreeChanged += OnItemTreeChanged;
            _container.onChildChanged += OnItemTreeChanged;
            _subscribed = true;
        }

        private void OnContentChanged(Inventory inventory, int index) { MarkDirty(); }
        private void OnInventorySorted(Inventory inventory) { MarkDirty(); }
        private void OnItemTreeChanged(Item item) { MarkDirty(); }
        private void MarkDirty()
        {
            _dirty = true;
            _durable = false;
            // 宿主销毁可能发生在本帧 LateUpdate 前，必须先登记采集义务。
            PetNestSaveCoordinator.MarkBackpackAssetsChanged();
        }

        private void LateUpdate()
        {
            if (_disposed || !_ready) return;
            if (_retired)
            {
                if (_slot != SavesSystem.CurrentSlot) { Dispose(false); return; }
                if (CharacterMainControl.Main != _assetOwner || _assetOwner == null
                    || SavesSystem.IsSaving || Time.unscaledTime < _nextSaveTime) return;
                _nextSaveTime = Time.unscaledTime + 2f;
                Dispose(true);
                return;
            }
            bool open = LootView.Instance != null && LootView.Instance.open
                && LootView.Instance.TargetInventory == _inventory;
            bool closed = _wasOpen && !open;
            _wasOpen = open;
            if ((!_dirty && !closed) || SavesSystem.IsSaving || Time.unscaledTime < _nextSaveTime) return;
            // Inventory 的移出事件发生在移入事件前；LateUpdate 才能采到拖拽完成后的两端。
            _nextSaveTime = Time.unscaledTime + 0.5f;
            string error;
            if (!Capture(out error) || !PetNestSaveCoordinator.RequestAssetFlush(out error))
            {
                _dirty = true;
                _nextSaveTime = Time.unscaledTime + 2f;
                // 故障后先关闭物品转移，避免继续修改一个已进入持久化屏障的背包。
                if (open && LootView.Instance != null) LootView.Instance.Close();
                NotifyFailure(error);
            }
        }

        private bool Capture(out string error)
        {
            error = null;
            if (!_ready || _disposed || _inventory == null || _slot != SavesSystem.CurrentSlot) return true;
            if (_assetOwner == null || CharacterMainControl.Main != _assetOwner)
            {
                if (_durable) return true;
                error = "backpack_save_failed:asset_owner_changed";
                return false;
            }
            try
            {
                PetNestBackpackSnapshot snapshot = new PetNestBackpackSnapshot();
                for (int i = 0; i <= _inventory.GetLastItemPosition(); i++)
                {
                    Item item = _inventory.GetItemAt(i);
                    if (item != null) snapshot.Items.Add(new PetNestBackpackEntry { Position = i, Tree = ItemTreeData.FromItem(item) });
                }
                snapshot.Locks.AddRange(_inventory.lockedIndexes);
                string json = snapshot.Items.Count == 0 && snapshot.Locks.Count == 0 ? null : snapshot.Encode();
                if (!PetNestService.StoreBackpack(_petId, json, out error)) return false;
                _dirty = false;
                return true;
            }
            catch (Exception e) { error = "backpack_save_failed:" + e.GetType().Name; return false; }
        }

        internal static bool CollectAll(out string error)
        {
            error = null;
            foreach (PetNestBackpack backpack in Active)
                if (backpack != null && !backpack.Capture(out error)) return false;
            return true;
        }

        internal static bool TryGetLiveItemCount(string petId, out int count)
        {
            count = 0;
            foreach (PetNestBackpack backpack in Active)
            {
                if (backpack == null || backpack._disposed || !backpack._ready
                    || backpack._slot != SavesSystem.CurrentSlot || backpack._petId != petId) continue;
                count = backpack._inventory.GetItemCount();
                return true;
            }
            return false;
        }

        internal static void ResetStaticCaches()
        {
            foreach (PetNestBackpack backpack in new List<PetNestBackpack>(Active))
                if (backpack != null) backpack.Dispose(true);
            // 写失败的容器仍由独立对象拥有，不能用清静态表把唯一实物遗失。
            Active.RemoveWhere(backpack => backpack == null || backpack._disposed);
        }

        internal static void NotifyPhysicalSaveSucceeded()
        {
            foreach (PetNestBackpack backpack in Active)
                if (backpack != null && backpack._ready && backpack._slot == SavesSystem.CurrentSlot
                    && backpack._assetOwner == CharacterMainControl.Main) backpack._durable = true;
        }

        internal static void DiscardForSlotChange()
        {
            foreach (PetNestBackpack backpack in new List<PetNestBackpack>(Active))
                if (backpack != null) backpack.Dispose(false);
            Active.Clear();
        }

        internal void Dispose(bool save)
        {
            if (_disposed) return;
            if (LootView.Instance != null && LootView.Instance.open && LootView.Instance.TargetInventory == _inventory)
                LootView.Instance.Close();
            if (save && _slot == SavesSystem.CurrentSlot)
            {
                string error = null;
                if ((!_durable && !Capture(out error))
                    || (_ready && !_durable && !PetNestSaveCoordinator.RequestAssetFlush(out error)))
                {
                    if (!_retired) Debug.LogWarning("[PetNest] 背包封存失败，保留物品容器: " + error);
                    _retired = true;
                    _dirty = true;
                    _generation++;
                    if (_destroying)
                    {
                        // Unity 已进入 OnDestroy 时不能挽救原组件；把唯一容器交给新 owner。
                        GameObject recovery = new GameObject("PetNestBackpackRecovery");
                        UnityEngine.Object.DontDestroyOnLoad(recovery);
                        PetNestBackpack retained = recovery.AddComponent<PetNestBackpack>();
                        UnsubscribeInventory();
                        retained._petId = _petId; retained._slot = _slot;
                        retained._assetOwner = _assetOwner;
                        retained._container = _container; retained._inventory = _inventory;
                        retained._ready = _ready; retained._dirty = true; retained._retired = true;
                        retained._nextSaveTime = Time.unscaledTime + 2f;
                        retained.SubscribeInventory();
                        retained.MarkerActive = false;
                        if (retained.interactCollider != null) retained.interactCollider.enabled = false;
                        Active.Add(retained);
                        Active.Remove(this);
                        _container = null; _inventory = null; _ready = false; _disposed = true;
                    }
                    else
                    {
                        transform.SetParent(null, true);
                        UnityEngine.Object.DontDestroyOnLoad(gameObject);
                    }
                    if (interactCollider != null) interactCollider.enabled = false;
                    MarkerActive = false;
                    _nextSaveTime = Time.unscaledTime + 2f;
                    PetNestSaveCoordinator.MarkBackpackAssetsChanged();
                    return;
                }
            }
            _disposed = true;
            _generation++;
            Active.Remove(this);
            DestroyContainer();
            if (interactCollider != null) interactCollider.enabled = false;
            MarkerActive = false;
            if (_retired && !_destroying) UnityEngine.Object.Destroy(gameObject);
        }

        private void UnsubscribeInventory()
        {
            if (_subscribed && _inventory != null)
            {
                _inventory.onContentChanged -= OnContentChanged;
                _inventory.onInventorySorted -= OnInventorySorted;
                _inventory.onSetIndexLock -= OnContentChanged;
            }
            if (_subscribed && _container != null)
            {
                _container.onItemTreeChanged -= OnItemTreeChanged;
                _container.onChildChanged -= OnItemTreeChanged;
            }
            _subscribed = false;
        }

        private void DestroyContainer()
        {
            UnsubscribeInventory();
            _ready = false;
            if (_container != null) _container.DestroyTree();
            _container = null;
            _inventory = null;
        }

        private static void NotifyFailure(string error)
        {
            if (CharacterMainControl.Main != null)
                CharacterMainControl.Main.PopText(PetNestLocalization.DescribeFailure(error), -1f);
        }

        protected override void OnDestroy()
        {
            _destroying = true;
            Dispose(true);
            base.OnDestroy();
        }
    }
}
