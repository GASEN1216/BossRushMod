// ============================================================================
// PetNestPersistence.cs - 遗种巢 Bundle_v2 权威存档管线
// ============================================================================
// 硬约束（tests/PetNestPersistenceGuard.py 守卫）：
//   - `BossRush_PetNest_Bundle_v2` 是唯一运行时权威状态；三个 v1 key 只在首次迁移时读取，
//     永不删除、永不再分拆写入。Bundle 不用 typed `Save<T>`：ES3 会把
//     assembly-qualified 类型名写进存档，程序集改名/重构会让老档读不回来；
//     整存字符串把这层耦合彻底切断。
//   - `OnCollectSaveData` / `OnSetFile` / `OnSaveDeleted` **幂等订阅**且必须成对退订；
//   - 写屏障：未知/更高 schemaVersion、payload 不可读时只读不写，**绝不覆盖该 key**；
//   - 战斗中不写盘：Store 只入队 pending，物理落盘统一由 PetNestSaveCoordinator 触发；
//   - 全程 no-throw：存档路径异常不得拖崩宿主。
// ============================================================================

using System;
using System.Collections.Generic;
using Saves;

namespace BossRush
{
    /// <summary>
    /// v1 兼容读取用的单 key store。运行时写入不再经过这里。
    /// </summary>
    internal sealed class PetNestKeyStore<T> where T : class
    {
        private readonly object _lock = new object();
        private readonly string _key;
        private readonly Func<T, string> _encode;
        private readonly Func<PetNestJsonNode, T> _decode;
        private readonly Func<T> _createDefault;

        private T _cache;
        /// <summary>
        /// _cache / _pendingJson 所属的存档槽。**命中缓存前必须校验**：
        /// 运行时关开关会把 OnSetFile 一起退订，之后玩家在主菜单换档没人清缓存，
        /// 重开开关时缓存里还是上一个档的崽与遗魂账本，一写就把 A 档覆盖到 B 档。
        /// 记槽位自校验之后，无论订阅是否还在都安全（与日报侧同一形态）。
        /// </summary>
        private int _cacheSlot;
        private string _pendingJson;
        private bool _pendingActive;
        private bool _writeBarrier;
        private bool _storeFaulted;
        private string _lastError;

        internal PetNestKeyStore(
            string key,
            Func<T, string> encode,
            Func<PetNestJsonNode, T> decode,
            Func<T> createDefault)
        {
            _key = key;
            _encode = encode;
            _decode = decode;
            _createDefault = createDefault;
        }

        /// <summary>存档 key。</summary>
        internal string StorageKey { get { return _key; } }

        /// <summary>单向故障：写入路径出过异常之后不再尝试写。</summary>
        internal bool IsStoreFaulted { get { return _storeFaulted; } }

        /// <summary>写屏障：未知版本 / 不可读 payload，只读不写。</summary>
        internal bool HasWriteBarrier { get { lock (_lock) { return _writeBarrier; } } }

        /// <summary>是否有待落盘批次。</summary>
        internal bool HasPendingWrite { get { lock (_lock) { return _pendingActive && _pendingJson != null; } } }

        /// <summary>
        /// 现在 Store 会不会成功。**多 key 事务的前置检查**：
        /// 一次业务操作要改多个 key 时，必须先把所有 key 都 CanStore 过一遍再开始 Store，
        /// 否则先成功的那个 key 会把 pending 留下来，被官方 OnCollectSaveData 独立落盘，
        /// 而调用方以为整个操作失败并回滚了内存——两边就此永久分叉。
        /// </summary>
        internal bool CanStore
        {
            get
            {
                if (_storeFaulted) return false;
                lock (_lock) { return !_writeBarrier; }
            }
        }

        /// <summary>
        /// 丢弃尚未落盘的 pending。只用于多 key 事务的失败回滚：
        /// 已经 Store 过的 key 必须把 pending 撤掉，否则会被独立落盘。
        /// 已经物理落盘的批次撤不回来——这正是必须用 CanStore 前置检查的原因。
        /// </summary>
        internal void DiscardPending()
        {
            lock (_lock)
            {
                _pendingJson = null;
                _pendingActive = false;
            }
        }

        /// <summary>最后一次失败原因。</summary>
        internal string LastError { get { return _lastError; } }

        /// <summary>
        /// 当前存档槽。读失败时按"未变化"处理：诊断路径的失败绝不能反过来误清缓存。
        /// 必须在 _lock 内调用（要读 _cacheSlot）。
        /// </summary>
        private int ReadCurrentSlotOrCached()
        {
            try
            {
                return SavesSystem.CurrentSlot;
            }
            catch (Exception)
            {
                return _cacheSlot;
            }
        }

        /// <summary>加载或初始化。幂等：已有缓存且槽位一致时直接返回。</summary>
        internal T LoadOrInit()
        {
            lock (_lock)
            {
                int slot = ReadCurrentSlotOrCached();
                if (_cache != null)
                {
                    if (_cacheSlot == slot) return _cache;

                    // 槽位与缓存不符：自失效重载。pending 属于旧档，绝不能带进新档
                    // （写屏障也一并复位，新档要按它自己的 payload 重新判定）。
                    ModBehaviour.DevLog("[PetNest] 存档槽变化（" + _cacheSlot + " -> " + slot
                        + "），缓存自失效重载: " + _key);
                    _cache = null;
                    _pendingJson = null;
                    _pendingActive = false;
                    _writeBarrier = false;
                    _lastError = null;
                }
                _cacheSlot = slot;

                bool keyExists = false;
                try
                {
                    keyExists = SavesSystem.KeyExisits(_key);
                }
                catch (Exception e)
                {
                    _writeBarrier = true;
                    _lastError = "key_classification_failed:" + e.GetType().Name;
                    ModBehaviour.DevLog("[PetNest] [WARNING] 存档 key 分类失败，进入写屏障: " + _key);
                    _cache = _createDefault();
                    return _cache;
                }

                if (!keyExists)
                {
                    _cache = _createDefault();
                    return _cache;
                }

                string raw = null;
                try
                {
                    raw = SavesSystem.Load<string>(_key);
                }
                catch (Exception e)
                {
                    _writeBarrier = true;
                    _lastError = "payload_load_failed:" + e.GetType().Name;
                    ModBehaviour.DevLog("[PetNest] [WARNING] 存档读取失败，进入写屏障: " + _key);
                    _cache = _createDefault();
                    return _cache;
                }

                PetNestJsonNode envelope = PetNestJson.Parse(raw);
                if (envelope == null || envelope.Kind != PetNestJsonKind.Object)
                {
                    _writeBarrier = true;
                    _lastError = "payload_unreadable";
                    ModBehaviour.DevLog("[PetNest] [WARNING] 存档 payload 不可读，进入写屏障: " + _key);
                    _cache = _createDefault();
                    return _cache;
                }

                int version = envelope.GetInt("schemaVersion", -1);
                if (version != PetNestTuning.CurrentSchemaVersion)
                {
                    // 高版本 fail-closed 只读；低版本目前没有迁移路径，同样只读不覆盖。
                    _writeBarrier = true;
                    _lastError = "schema_mismatch:" + version;
                    ModBehaviour.DevLog("[PetNest] [WARNING] 存档 schemaVersion=" + version
                        + " 与当前 " + PetNestTuning.CurrentSchemaVersion + " 不符，只读不覆盖: " + _key);
                    _cache = _createDefault();
                    return _cache;
                }

                PetNestJsonNode payload = envelope.GetObject("payload");
                T decoded = null;
                try
                {
                    decoded = payload != null ? _decode(payload) : null;
                }
                catch (Exception e)
                {
                    decoded = null;
                    _lastError = "decode_failed:" + e.GetType().Name;
                }

                if (decoded == null)
                {
                    _writeBarrier = true;
                    ModBehaviour.DevLog("[PetNest] [WARNING] 存档解码失败，进入写屏障: " + _key);
                    _cache = _createDefault();
                    return _cache;
                }

                _cache = decoded;
                return _cache;
            }
        }

        /// <summary>当前缓存（未加载时先加载）。</summary>
        internal T Current { get { return LoadOrInit(); } }

        /// <summary>
        /// 入队一次写入。战斗中不落盘，只更新缓存与 pending；
        /// 物理落盘由 PetNestSaveCoordinator 统一触发。
        /// </summary>
        internal bool Store(T value)
        {
            if (value == null) return false;
            if (_storeFaulted) return false;
            if (HasWriteBarrier) return false;

            try
            {
                string json = BuildEnvelope(value);
                lock (_lock)
                {
                    _cache = value;
                    // 缓存与 pending 一起打上槽位戳，供 LoadOrInit / FlushPending 校验
                    _cacheSlot = ReadCurrentSlotOrCached();
                    // 每 key 至多一个 pending：合并覆盖，不叠加
                    _pendingJson = json;
                    _pendingActive = true;
                }
                return true;
            }
            catch (Exception e)
            {
                _storeFaulted = true;
                _lastError = "encode_failed:" + e.GetType().Name;
                ModBehaviour.DevLog("[PetNest] [ERROR] 存档编码异常，进入 StoreFaulted: " + _key + ", " + e.Message);
                return false;
            }
        }

        private string BuildEnvelope(T value)
        {
            string payload = _encode(value);
            PetNestJsonBuilder sb = new PetNestJsonBuilder();
            sb.BeginObject()
              .Int("schemaVersion", PetNestTuning.CurrentSchemaVersion)
              // payload 已是完整 JSON 对象文本，内联即可，不做二次转义
              .Raw("payload", payload)
              .EndObject();
            return sb.ToString();
        }

        /// <summary>
        /// 落盘 pending。IsSaving 时返回 false 并保留 pending（由协调器重试）。
        /// 不在这里调 SaveFile：那是协调器的唯一职责。
        /// </summary>
        internal bool FlushPending()
        {
            lock (_lock)
            {
                if (!_pendingActive || _pendingJson == null) return true;
                if (_writeBarrier) { _pendingActive = false; _pendingJson = null; return true; }

                // 槽位保险：pending 属于它入队时的那个档。官方采集与协调器都可能在
                // 换档之后才触发 flush，这里不拦就会把旧档的巢数据写进新档。
                if (_cacheSlot != ReadCurrentSlotOrCached())
                {
                    _pendingJson = null;
                    _pendingActive = false;
                    _cache = null;
                    ModBehaviour.DevLog("[PetNest] 存档槽已变化，丢弃跨档 pending: " + _key);
                    return true;
                }

                try
                {
                    if (SavesSystem.IsSaving)
                    {
                        _lastError = "flush_deferred_is_saving";
                        return false;
                    }

                    SavesSystem.Save<string>(_key, _pendingJson);

                    // 回读核对：写进去的字符串必须能原样读回来
                    string readback = SavesSystem.Load<string>(_key);
                    if (!string.Equals(readback, _pendingJson, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("petnest save readback mismatch: " + _key);
                    }

                    _pendingJson = null;
                    _pendingActive = false;
                    _lastError = null;
                    return true;
                }
                catch (Exception e)
                {
                    _storeFaulted = true;
                    _lastError = "flush_failed:" + e.GetType().Name;
                    ModBehaviour.DevLog("[PetNest] [ERROR] 存档 flush 异常，进入 StoreFaulted: "
                        + _key + ", " + e.Message);
                    return false;
                }
            }
        }

        /// <summary>切档：丢弃内存状态，从新档重新加载。</summary>
        internal void ResetForSlotChange()
        {
            lock (_lock)
            {
                _cache = null;
                _pendingJson = null;
                _pendingActive = false;
                _writeBarrier = false;
                _lastError = null;
            }
        }

        /// <summary>删档 / Mod 卸载：全量重置（含故障标记）。</summary>
        internal void ResetAll()
        {
            lock (_lock)
            {
                _cache = null;
                _pendingJson = null;
                _pendingActive = false;
                _writeBarrier = false;
                _storeFaulted = false;
                _lastError = null;
            }
        }
    }

    /// <summary>v2 单包 store。所有运行时写入只经过这一份 pending。</summary>
    internal sealed class PetNestBundleStore
    {
        private readonly object _lock = new object();
        private PetNestBundleData _cache;
        private int _cacheSlot;
        private string _pendingJson;
        private bool _pendingActive;
        private bool _writeBarrier;
        private bool _storeFaulted;
        private string _lastError;

        internal bool IsStoreFaulted { get { return _storeFaulted; } }
        internal bool HasWriteBarrier { get { lock (_lock) { return _writeBarrier; } } }
        internal bool HasPendingWrite { get { lock (_lock) { return _pendingActive && _pendingJson != null; } } }
        internal bool CanStore { get { return !_storeFaulted && !HasWriteBarrier; } }
        internal string LastError { get { return _lastError; } }

        private int ReadCurrentSlotOrCached()
        {
            try { return SavesSystem.CurrentSlot; }
            catch (Exception) { return _cacheSlot; }
        }

        internal PetNestBundleData LoadOrInit()
        {
            lock (_lock)
            {
                int slot = ReadCurrentSlotOrCached();
                if (_cache != null && _cacheSlot == slot) return _cache;
                if (_cache != null && _cacheSlot != slot) ResetForSlotChangeLocked();
                _cacheSlot = slot;

                bool exists;
                try { exists = SavesSystem.KeyExisits(PetNestTuning.BundleStorageKey); }
                catch (Exception e)
                {
                    EnterWriteBarrier("bundle_key_classification_failed:" + e.GetType().Name);
                    return _cache;
                }

                if (exists)
                {
                    try
                    {
                        string raw = SavesSystem.Load<string>(PetNestTuning.BundleStorageKey);
                        PetNestJsonNode root = PetNestJson.Parse(raw);
                        int version = root != null ? root.GetInt("schemaVersion", -1) : -1;
                        if (version != PetNestTuning.BundleSchemaVersion)
                        {
                            EnterWriteBarrier("bundle_schema_mismatch:" + version);
                            return _cache;
                        }
                        PetNestBundleData decoded = PetNestCodec.DecodeBundle(root);
                        if (decoded == null)
                        {
                            EnterWriteBarrier("bundle_decode_failed");
                            return _cache;
                        }
                        _cache = decoded;
                        return _cache;
                    }
                    catch (Exception e)
                    {
                        EnterWriteBarrier("bundle_load_failed:" + e.GetType().Name);
                        return _cache;
                    }
                }

                PetNestBundleData legacy;
                bool hasLegacy;
                string migrationError;
                if (!PetNestPersistence.TryBuildLegacyBundle(out legacy, out hasLegacy, out migrationError))
                {
                    EnterWriteBarrier(migrationError ?? "legacy_migration_failed");
                    return _cache;
                }

                _cache = legacy ?? PetNestCodec.CreateDefaultBundle();
                if (hasLegacy)
                {
                    // 先入队，再统一由协调器 Save + 回读；失败时 v1 原键仍完整保留。
                    if (!Store(_cache))
                    {
                        EnterWriteBarrier("legacy_migration_stage_failed");
                        return _cache;
                    }
                    string ignored;
                    PetNestSaveCoordinator.RequestFlush(out ignored);
                    if (_pendingActive || _storeFaulted)
                    {
                        EnterWriteBarrier("legacy_migration_readback_failed");
                    }
                    else
                    {
                        ModBehaviour.DevLog("[PetNest] v1 三键已迁移到 Bundle_v2，旧键保留只读");
                    }
                }
                return _cache;
            }
        }

        internal PetNestBundleData Current { get { return LoadOrInit(); } }

        internal bool Store(PetNestBundleData value)
        {
            if (value == null || _storeFaulted || HasWriteBarrier) return false;
            try
            {
                PetNestBundleData staged = PetNestCodec.CloneBundle(value);
                int currentGeneration = _cache != null ? _cache.generation : 0;
                staged.generation = Math.Max(staged.generation, currentGeneration) + 1;
                string json = PetNestCodec.EncodeBundle(staged);
                if (string.IsNullOrEmpty(json)) return false;
                lock (_lock)
                {
                    _cache = staged;
                    _cacheSlot = ReadCurrentSlotOrCached();
                    _pendingJson = json;
                    _pendingActive = true;
                }
                return true;
            }
            catch (Exception e)
            {
                _storeFaulted = true;
                _lastError = "bundle_encode_failed:" + e.GetType().Name;
                ModBehaviour.DevLog("[PetNest] [ERROR] Bundle_v2 编码异常: " + e.Message);
                return false;
            }
        }

        internal bool FlushPending()
        {
            lock (_lock)
            {
                if (!_pendingActive || _pendingJson == null) return true;
                if (_writeBarrier) return false;
                if (_cacheSlot != ReadCurrentSlotOrCached())
                {
                    ResetForSlotChangeLocked();
                    return true;
                }
                try
                {
                    if (SavesSystem.IsSaving)
                    {
                        _lastError = "flush_deferred_is_saving";
                        return false;
                    }
                    SavesSystem.Save<string>(PetNestTuning.BundleStorageKey, _pendingJson);
                    string readback = SavesSystem.Load<string>(PetNestTuning.BundleStorageKey);
                    if (!string.Equals(readback, _pendingJson, StringComparison.Ordinal))
                        throw new InvalidOperationException("petnest bundle readback mismatch");
                    _pendingJson = null;
                    _pendingActive = false;
                    _lastError = null;
                    return true;
                }
                catch (Exception e)
                {
                    _storeFaulted = true;
                    _lastError = "bundle_flush_failed:" + e.GetType().Name;
                    ModBehaviour.DevLog("[PetNest] [ERROR] Bundle_v2 flush 异常: " + e.Message);
                    return false;
                }
            }
        }

        internal void DiscardPending()
        {
            lock (_lock) { _pendingJson = null; _pendingActive = false; }
        }

        internal void ResetForSlotChange()
        {
            lock (_lock) { ResetForSlotChangeLocked(); }
        }

        private void ResetForSlotChangeLocked()
        {
            _cache = null;
            _pendingJson = null;
            _pendingActive = false;
            _writeBarrier = false;
            _lastError = null;
        }

        internal void ResetAll()
        {
            lock (_lock)
            {
                ResetForSlotChangeLocked();
                _storeFaulted = false;
            }
        }

        private void EnterWriteBarrier(string error)
        {
            _writeBarrier = true;
            _lastError = error;
            _pendingJson = null;
            _pendingActive = false;
            _cache = PetNestCodec.CreateDefaultBundle();
            ModBehaviour.DevLog("[PetNest] [WARNING] Bundle_v2 进入写屏障: " + error);
        }
    }

    /// <summary>保持既有服务调用形态的聚合包分区门面；Store 实际写入同一个 Bundle_v2。</summary>
    internal sealed class PetNestBundlePartStore<T> where T : class
    {
        private readonly Func<PetNestBundleData, T> _get;
        private readonly Action<PetNestBundleData, T> _set;

        internal PetNestBundlePartStore(
            Func<PetNestBundleData, T> get,
            Action<PetNestBundleData, T> set)
        {
            _get = get;
            _set = set;
        }

        internal T Current { get { return _get(PetNestPersistence.GetActiveBundle()); } }
        internal bool IsStoreFaulted { get { return PetNestPersistence.Bundle.IsStoreFaulted; } }
        internal bool HasWriteBarrier { get { return PetNestPersistence.Bundle.HasWriteBarrier; } }
        internal bool CanStore { get { return PetNestPersistence.Bundle.CanStore; } }
        internal bool HasPendingWrite { get { return PetNestPersistence.Bundle.HasPendingWrite; } }
        internal string LastError { get { return PetNestPersistence.Bundle.LastError; } }

        internal bool Store(T value)
        {
            if (value == null) return false;
            if (PetNestPersistence.IsTransactionActive) return true;
            PetNestBundleData candidate = PetNestCodec.CloneBundle(PetNestPersistence.Bundle.Current);
            _set(candidate, value);
            return PetNestPersistence.Bundle.Store(candidate);
        }

        internal bool FlushPending() { return PetNestPersistence.Bundle.FlushPending(); }
        internal void DiscardPending() { PetNestPersistence.Bundle.DiscardPending(); }
        internal void ResetForSlotChange() { PetNestPersistence.Bundle.ResetForSlotChange(); }
        internal void ResetAll() { PetNestPersistence.Bundle.ResetAll(); }
    }

    /// <summary>
    /// 遗种巢持久化门面：Bundle_v2 权威 store + 三个 v1 只读迁移源。
    /// </summary>
    internal static class PetNestPersistence
    {
        #region Store 实例

        private static readonly PetNestKeyStore<PetNestNestData> _legacyNest =
            new PetNestKeyStore<PetNestNestData>(
                PetNestTuning.NestStorageKey,
                PetNestCodec.EncodeNest,
                PetNestCodec.DecodeNest,
                PetNestCodec.CreateDefaultNest);

        private static readonly PetNestKeyStore<PetNestExpeditionData> _legacyExpedition =
            new PetNestKeyStore<PetNestExpeditionData>(
                PetNestTuning.ExpeditionStorageKey,
                PetNestCodec.EncodeExpedition,
                PetNestCodec.DecodeExpedition,
                PetNestCodec.CreateDefaultExpedition);

        private static readonly PetNestKeyStore<PetNestMuseumData> _legacyMuseum =
            new PetNestKeyStore<PetNestMuseumData>(
                PetNestTuning.MuseumStorageKey,
                PetNestCodec.EncodeMuseum,
                PetNestCodec.DecodeMuseum,
                PetNestCodec.CreateDefaultMuseum);

        private static readonly PetNestBundleStore _bundle = new PetNestBundleStore();
        [ThreadStatic]
        private static PetNestBundleData _activeTransaction;
        private static readonly PetNestBundlePartStore<PetNestNestData> _nest =
            new PetNestBundlePartStore<PetNestNestData>(
                b => b.nest, (b, v) => b.nest = PetNestCodec.CloneNest(v));
        private static readonly PetNestBundlePartStore<PetNestExpeditionData> _expedition =
            new PetNestBundlePartStore<PetNestExpeditionData>(
                b => b.expedition, (b, v) => b.expedition = PetNestCodec.CloneExpedition(v));
        private static readonly PetNestBundlePartStore<PetNestMuseumData> _museum =
            new PetNestBundlePartStore<PetNestMuseumData>(
                b => b.museum, (b, v) => b.museum = PetNestCodec.CloneMuseum(v));

        internal static PetNestBundleStore Bundle { get { return _bundle; } }
        internal static bool IsTransactionActive { get { return _activeTransaction != null; } }
        internal static PetNestBundlePartStore<PetNestNestData> Nest { get { return _nest; } }
        internal static PetNestBundlePartStore<PetNestExpeditionData> Expedition { get { return _expedition; } }
        internal static PetNestBundlePartStore<PetNestMuseumData> Museum { get { return _museum; } }

        internal static PetNestBundleData GetActiveBundle()
        {
            return _activeTransaction ?? _bundle.Current;
        }

        /// <summary>开启主线程候选包事务。事务内三个分区的 Current 全部指向候选副本。</summary>
        internal static bool BeginTransaction(out string error)
        {
            error = null;
            if (_activeTransaction != null)
            {
                error = "nested_transaction";
                return false;
            }
            try
            {
                PetNestBundleData current = _bundle.Current;
                if (!_bundle.CanStore)
                {
                    error = _bundle.HasWriteBarrier ? "save_write_barrier" : "save_store_faulted";
                    return false;
                }
                _activeTransaction = PetNestCodec.CloneBundle(current);
                return true;
            }
            catch (Exception e)
            {
                error = "transaction_clone_failed:" + e.GetType().Name;
                _activeTransaction = null;
                return false;
            }
        }

        internal static bool CommitTransaction(out string error)
        {
            error = null;
            PetNestBundleData candidate = _activeTransaction;
            if (candidate == null)
            {
                error = "transaction_missing";
                return false;
            }
            _activeTransaction = null;
            if (!_bundle.Store(candidate))
            {
                error = _bundle.HasWriteBarrier ? "save_write_barrier" : "save_store_faulted";
                return false;
            }
            return true;
        }

        internal static void AbortTransaction()
        {
            _activeTransaction = null;
        }

        internal static bool TryBuildLegacyBundle(
            out PetNestBundleData bundle, out bool hasLegacy, out string error)
        {
            bundle = PetNestCodec.CreateDefaultBundle();
            hasLegacy = false;
            error = null;
            try
            {
                bool hasNest = SavesSystem.KeyExisits(PetNestTuning.NestStorageKey);
                bool hasExpedition = SavesSystem.KeyExisits(PetNestTuning.ExpeditionStorageKey);
                bool hasMuseum = SavesSystem.KeyExisits(PetNestTuning.MuseumStorageKey);
                hasLegacy = hasNest || hasExpedition || hasMuseum;
                if (!hasLegacy) return true;

                bundle.nest = PetNestCodec.CloneNest(_legacyNest.LoadOrInit());
                bundle.expedition = PetNestCodec.CloneExpedition(_legacyExpedition.LoadOrInit());
                bundle.museum = PetNestCodec.CloneMuseum(_legacyMuseum.LoadOrInit());
                if (_legacyNest.HasWriteBarrier || _legacyExpedition.HasWriteBarrier
                    || _legacyMuseum.HasWriteBarrier || _legacyNest.IsStoreFaulted
                    || _legacyExpedition.IsStoreFaulted || _legacyMuseum.IsStoreFaulted)
                {
                    error = "legacy_payload_unreadable";
                    return false;
                }
                bundle.Normalize();
                return true;
            }
            catch (Exception e)
            {
                error = "legacy_migration_exception:" + e.GetType().Name;
                return false;
            }
        }

        internal static bool StoreBundle(
            PetNestNestData nest, PetNestExpeditionData expedition, PetNestMuseumData museum)
        {
            if (!_bundle.CanStore) return false;
            PetNestBundleData candidate = PetNestCodec.CloneBundle(_bundle.Current);
            candidate.nest = PetNestCodec.CloneNest(nest);
            candidate.expedition = PetNestCodec.CloneExpedition(expedition);
            candidate.museum = PetNestCodec.CloneMuseum(museum);
            candidate.Normalize();
            return _bundle.Store(candidate);
        }

        #endregion

        #region 订阅（幂等）

        private static readonly object _subscriptionLock = new object();
        private static bool _subscribed;

        /// <summary>是否已订阅官方存档事件。</summary>
        internal static bool IsSubscribed { get { return _subscribed; } }

        /// <summary>幂等订阅官方存档事件。模块 Awake 调一次。</summary>
        internal static void EnsureSubscribed()
        {
            lock (_subscriptionLock)
            {
                if (_subscribed) return;
                try
                {
                    SavesSystem.OnCollectSaveData += HandleCollectSaveData;
                    SavesSystem.OnSetFile += HandleSetFile;
                    SavesSystem.OnSaveDeleted += HandleSaveDeleted;
                    _subscribed = true;
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[PetNest] [WARNING] 存档订阅失败: " + e.Message);
                }
            }
        }

        /// <summary>幂等退订。宿主销毁 / Mod 卸载调用。</summary>
        internal static void ShutdownSubscription()
        {
            lock (_subscriptionLock)
            {
                if (!_subscribed) return;
                try
                {
                    SavesSystem.OnCollectSaveData -= HandleCollectSaveData;
                    SavesSystem.OnSetFile -= HandleSetFile;
                    SavesSystem.OnSaveDeleted -= HandleSaveDeleted;
                }
                catch (Exception)
                {
                    // 退订失败也要把 _subscribed 置回 false，避免重复订阅越滚越多
                }
                _subscribed = false;
            }
        }

        private static void HandleCollectSaveData()
        {
            try
            {
                // 官方收集时把唯一 Bundle_v2 pending 合并进存档，但不单独 SaveFile。
                _bundle.FlushPending();
            }
            catch (Exception)
            {
                // no-throw：存档收集路径不得抛
            }
        }

        private static void HandleSetFile()
        {
            try
            {
                _bundle.ResetForSlotChange();
                _legacyNest.ResetForSlotChange();
                _legacyExpedition.ResetForSlotChange();
                _legacyMuseum.ResetForSlotChange();
                PetNestSaveCoordinator.NotifySlotChanged();
            }
            catch (Exception)
            {
                // no-throw：切档回调不得抛，异常时保持"已重置"状态即可
            }
        }

        private static void HandleSaveDeleted()
        {
            try
            {
                _bundle.ResetForSlotChange();
                _legacyNest.ResetForSlotChange();
                _legacyExpedition.ResetForSlotChange();
                _legacyMuseum.ResetForSlotChange();
                PetNestSaveCoordinator.NotifySlotChanged();
            }
            catch (Exception)
            {
                // no-throw：删档回调不得抛，异常时保持"已重置"状态即可
            }
        }

        #endregion

        #region 状态查询

        /// <summary>任一 store 进入单向故障。入口据此 fail-closed。</summary>
        internal static bool IsAnyStoreFaulted
        {
            get { return _bundle.IsStoreFaulted; }
        }

        /// <summary>任一 store 处于写屏障（老档 / 未知版本，只读不写）。</summary>
        internal static bool HasAnyWriteBarrier
        {
            get { return _bundle.HasWriteBarrier; }
        }

        /// <summary>存在待落盘批次。</summary>
        internal static bool HasAnyPendingWrite
        {
            get { return _bundle.HasPendingWrite; }
        }

        /// <summary>权威 Bundle 当前是否可写。保留旧属性名以减少服务层改动。</summary>
        internal static bool CanStoreAll
        {
            get { return _bundle.CanStore; }
        }

        /// <summary>丢弃权威 Bundle 的 pending。</summary>
        internal static void DiscardAllPending()
        {
            _bundle.DiscardPending();
        }

        /// <summary>
        /// 丢弃 Bundle 与 v1 迁移源的内存缓存，下次访问从当前存档槽重新加载。
        ///
        /// **运行时关开关必须调它**：关开关会连 OnSetFile 一起退订，之后玩家切档时
        /// 没人清缓存；再打开开关时缓存里还是上一个档的崽与遗魂账本，一旦写入就会把
        /// 旧档数据整体覆盖到新档上。
        /// </summary>
        internal static void ResetCachesForSlotReload()
        {
            _bundle.ResetForSlotChange();
            _legacyNest.ResetForSlotChange();
            _legacyExpedition.ResetForSlotChange();
            _legacyMuseum.ResetForSlotChange();
        }

        #endregion

        #region 清理

        /// <summary>静态缓存重置（Mod 卸载 / 宿主重建）。会先退订。</summary>
        internal static void ResetStaticCaches()
        {
            ShutdownSubscription();
            _bundle.ResetAll();
            _legacyNest.ResetAll();
            _legacyExpedition.ResetAll();
            _legacyMuseum.ResetAll();
        }

        #endregion
    }
}
