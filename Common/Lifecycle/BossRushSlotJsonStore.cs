// ============================================================================
// BossRushSlotJsonStore.cs - 槽位级单 key JSON 整存门面的共享实现
// ============================================================================
// 为什么需要它（2026-09-06，D-2）：
//   征程 / 图鉴 / 日报三个 *Persistence 此前是同一份约 460 行状态机的逐字复制
//   （图鉴与日报归一化后只差 49 行）。现在实现只有这一份；子系统只保留 static 门面
//   （存档 key、schema、编解码、盖章、下游复位通知），API 名字不变。
//
// 硬约束（与三个原门面逐条对齐；由 CodexPersistenceGuard / DailyReportPersistenceGuard 守卫）：
//   - `SavesSystem.Save<string>` **JSON 整存**，不用 typed `Save<T>`：ES3 会把
//     assembly-qualified 类型名写进存档，mod 程序集改名/重构就会让老档读不回来；
//   - `OnCollectSaveData` / `OnSetFile` / `OnSaveDeleted` **幂等订阅**且成对退订，
//     一律用命名方法（AGENTS.md 4.6，禁 lambda，否则退订退不掉）；
//   - 缓存带槽位烙印：LoadOrInit 命中缓存也要比对 SavesSystem.CurrentSlot，
//     不一致就自失效重载并复位运行时。退订之后（开关关闭）换档没有任何回调，
//     只靠 OnSetFile 会把上一个槽的数据写进新档，因此校验必须在读取侧；
//   - 重新订阅时丢弃 dormant 期间可能过期的缓存：同槽删档重开靠槽号比对看不出来，
//     重新订阅这一刻是唯一确定的重新对齐点；
//   - 写屏障：未知 / 更高 schemaVersion、payload 不可读时只读不写，**绝不覆盖该 key**；
//   - 写入后回读核对（readback mismatch -> StoreFaulted）；
//   - 战斗中不写盘：Store 只入队 pending，物理落盘统一由子系统的协调器（引擎）触发；
//   - 全程 no-throw：存档路径异常不得拖崩宿主。
// ============================================================================

using System;
using Saves;

namespace BossRush
{
    /// <summary>单 key 整存门面的配置：存档 key、schema、编解码与下游钩子。全部由子系统门面在静态初始化时给出。</summary>
    internal sealed class BossRushSlotJsonStoreSpec<T> where T : class
    {
        /// <summary>SavesSystem key（发布后冻结）。</summary>
        public string StorageKey;

        /// <summary>当前 schemaVersion；读到的版本不等于它一律进写屏障。</summary>
        public int SchemaVersion;

        /// <summary>日志前缀（含尾随空格）。</summary>
        public string LogPrefix;

        /// <summary>日志里的中文名，例如「图鉴」。</summary>
        public string DisplayName;

        /// <summary>DTO -> JSON；失败返回 null。</summary>
        public Func<T, string> Encode;

        /// <summary>读 payload 的 schemaVersion；缺字段 / 读不动返回 -1。</summary>
        public Func<string, int> ReadSchemaVersion;

        /// <summary>JSON -> DTO；失败返回 null。</summary>
        public Func<string, T> Decode;

        /// <summary>新档默认值的唯一出处。</summary>
        public Func<T> CreateDefault;

        /// <summary>Store 前给 DTO 盖章（时间戳 / schemaVersion）。可为 null。</summary>
        public Action<T> BeforeStore;

        /// <summary>切档 / 删档 / 槽位漂移 / 重新订阅后的下游复位（协调器 / 采集器 / 目录）。可为 null。</summary>
        public Action NotifySlotChanged;

        /// <summary>
        /// 官方存盘采集点的前置步骤（同步运行时余数、采集现金快照）。
        /// 返回 false 表示前置未就绪，本次不把 pending 交给 SavesSystem。可为 null。
        /// </summary>
        public Func<bool> BeforeCollectSaveData;
    }

    /// <summary>槽位级单 key JSON 整存门面。一个子系统一个实例，由该子系统的 static 门面持有。</summary>
    internal sealed class BossRushSlotJsonStore<T> where T : class
    {
        /// <summary>槽位不可知的哨兵值（读 CurrentSlot 抛异常时用）。</summary>
        private const int SlotUnknown = int.MinValue;

        private readonly BossRushSlotJsonStoreSpec<T> _spec;
        private readonly object _lock = new object();
        private readonly object _subscriptionLock = new object();

        private T _cache;

        /// <summary>
        /// _cache 归属的存档槽位。SlotUnknown = 未加载 / 槽位读不到。
        /// 存在的唯一理由：ShutdownSubscription 会退订 OnSetFile，此后玩家在主菜单换档
        /// 没有任何回调会重置缓存；槽位校验发生在读取侧，无论订阅是否还在都安全。
        /// </summary>
        private int _cacheSlot = SlotUnknown;

        private string _pendingJson;
        private bool _pendingActive;
        private bool _writeBarrier;
        private bool _storeFaulted;
        private string _lastError;
        private bool _subscribed;

        internal BossRushSlotJsonStore(BossRushSlotJsonStoreSpec<T> spec)
        {
            if (spec == null) throw new ArgumentNullException("spec");
            if (string.IsNullOrEmpty(spec.StorageKey)) throw new ArgumentException("StorageKey");
            if (spec.Encode == null || spec.Decode == null || spec.ReadSchemaVersion == null || spec.CreateDefault == null)
            {
                throw new ArgumentException("codec");
            }
            _spec = spec;
        }

        #region 只读查询

        /// <summary>是否已订阅官方存档事件。</summary>
        internal bool IsSubscribed { get { return _subscribed; } }

        /// <summary>单向故障：写入路径出过异常之后不再尝试写。</summary>
        internal bool IsStoreFaulted { get { return _storeFaulted; } }

        /// <summary>写屏障：未知版本 / 不可读 payload，只读不写。</summary>
        internal bool HasWriteBarrier { get { lock (_lock) { return _writeBarrier; } } }

        /// <summary>是否有待落盘批次。</summary>
        internal bool HasPendingWrite
        {
            get { lock (_lock) { return _pendingActive && _pendingJson != null; } }
        }

        /// <summary>最后一次失败原因（诊断用）。</summary>
        internal string LastError { get { return _lastError; } }

        #endregion

        #region 订阅（幂等）

        /// <summary>幂等订阅官方存档事件。模块 bootstrap 调一次。</summary>
        internal void EnsureSubscribed()
        {
            bool becameSubscribed = false;
            lock (_subscriptionLock)
            {
                if (_subscribed) return;
                try
                {
                    SavesSystem.OnCollectSaveData += HandleCollectSaveData;
                    SavesSystem.OnSetFile += HandleSetFile;
                    SavesSystem.OnSaveDeleted += HandleSaveDeleted;
                    _subscribed = true;
                    becameSubscribed = true;
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog(_spec.LogPrefix + "[WARNING] 存档订阅失败: " + e.Message);
                }
            }

            // 复位通知放在订阅锁外，避免与运行时侧的锁产生嵌套顺序。
            if (becameSubscribed) DiscardCacheOnResubscribe();
        }

        /// <summary>
        /// 重新挂上监听时丢弃上一段「无人监听」期间可能已经过期的缓存。
        /// 代价是一次重读；首次订阅（无缓存）直接早返，正常开局零成本。
        /// 关停路径已经先做过一次绕闸的最后落盘，此处不再有可救的进度。
        /// </summary>
        private void DiscardCacheOnResubscribe()
        {
            bool hadCache;
            lock (_lock)
            {
                hadCache = _cache != null;
            }
            if (!hadCache) return;

            ResetForSlotChange();
            ModBehaviour.DevLog(_spec.LogPrefix + "重新订阅存档事件，dormant 期间的缓存已丢弃并将从当前槽重读");
            NotifyDownstream();
        }

        /// <summary>幂等退订。宿主销毁 / 开关关闭时调用。</summary>
        internal void ShutdownSubscription()
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
                catch (Exception e)
                {
                    // 退订失败也要把 _subscribed 置回 false，避免重复订阅越滚越多
                    ModBehaviour.DevLog(_spec.LogPrefix + "[WARNING] 存档退订异常: " + e.Message);
                }
                _subscribed = false;
            }
        }

        /// <summary>
        /// 官方存盘前的采集点：先跑子系统的前置步骤（同步余数 / 采集现金），再把 pending
        /// 合并进 ES3 缓存。**不单独** SaveFile（那是协调器的唯一职责）。
        /// </summary>
        private void HandleCollectSaveData()
        {
            try
            {
                if (_spec.BeforeCollectSaveData != null && !_spec.BeforeCollectSaveData()) return;
                FlushPending();
            }
            catch (Exception e)
            {
                // no-throw：存档收集路径不得抛
                ModBehaviour.DevLog(_spec.LogPrefix + "[WARNING] 存档收集回调失败: " + e.Message);
            }
        }

        private void HandleSetFile()
        {
            try
            {
                ResetForSlotChange();
                NotifyDownstream();
            }
            catch (Exception e)
            {
                // no-throw：切档回调不得抛，异常时保持「已重置」状态即可
                ModBehaviour.DevLog(_spec.LogPrefix + "[WARNING] 换槽回调失败: " + e.Message);
            }
        }

        private void HandleSaveDeleted()
        {
            try
            {
                ResetForSlotChange();
                NotifyDownstream();
            }
            catch (Exception e)
            {
                // no-throw：删档回调不得抛
                ModBehaviour.DevLog(_spec.LogPrefix + "[WARNING] 删档回调失败: " + e.Message);
            }
        }

        /// <summary>
        /// 换槽 / 删档 / 槽位漂移后的下游复位。与 HandleSetFile 同一组下游，
        /// 保证「数据换了、协调器 / 采集器 / 目录也换」。
        /// </summary>
        private void NotifyDownstream()
        {
            try
            {
                if (_spec.NotifySlotChanged != null) _spec.NotifySlotChanged();
            }
            catch (Exception e)
            {
                // no-throw：复位失败也不能拖崩读取路径
                ModBehaviour.DevLog(_spec.LogPrefix + "[WARNING] 换槽下游通知失败: " + e.Message);
            }
        }

        #endregion

        #region 加载 / 入队

        /// <summary>当前缓存（未加载时先加载）。</summary>
        internal T Current { get { return LoadOrInit(); } }

        /// <summary>
        /// 加载或初始化。幂等：缓存命中且**槽位一致**时直接返回。
        /// 槽位不一致说明中途换过档（典型是开关关闭期间退订了 OnSetFile），
        /// 此时缓存与 pending 全部作废并从新槽重读，避免把上一个槽的数据写进新档。
        /// </summary>
        internal T LoadOrInit()
        {
            bool slotDrifted = false;
            T loaded = LoadOrInitCore(ref slotDrifted);

            // 槽位漂移的复位通知放在锁外：运行时侧各有自己的锁，在持久层锁内回调会引入跨锁顺序。
            if (slotDrifted)
            {
                ModBehaviour.DevLog(_spec.LogPrefix + "[WARNING] 检测到存档槽位已变更但未收到切档回调，"
                    + _spec.DisplayName + "缓存已自失效并从新槽重载");
                NotifyDownstream();
            }
            return loaded;
        }

        /// <summary>LoadOrInit 的锁内主体。slotDrifted 回报是否命中了槽位漂移。</summary>
        private T LoadOrInitCore(ref bool slotDrifted)
        {
            lock (_lock)
            {
                int slot = ReadCurrentSlotSafe();

                if (_cache != null)
                {
                    // 绝大多数调用走这条：一次 int 比较，无 IO 无分配。
                    if (_cacheSlot == slot) return _cache;
                    slotDrifted = true;
                    ResetForSlotChangeLocked();
                }

                _cacheSlot = slot;

                bool keyExists;
                try
                {
                    // 注意官方拼写是 KeyExisits（少一个 t），不是笔误
                    keyExists = SavesSystem.KeyExisits(_spec.StorageKey);
                }
                catch (Exception e)
                {
                    _writeBarrier = true;
                    _lastError = "key_classification_failed:" + e.GetType().Name;
                    ModBehaviour.DevLog(_spec.LogPrefix + "[WARNING] 存档 key 分类失败，进入写屏障");
                    _cache = _spec.CreateDefault();
                    return _cache;
                }

                if (!keyExists)
                {
                    _cache = _spec.CreateDefault();
                    return _cache;
                }

                string raw;
                try
                {
                    raw = SavesSystem.Load<string>(_spec.StorageKey);
                }
                catch (Exception e)
                {
                    _writeBarrier = true;
                    _lastError = "payload_load_failed:" + e.GetType().Name;
                    ModBehaviour.DevLog(_spec.LogPrefix + "[WARNING] 存档读取失败，进入写屏障");
                    _cache = _spec.CreateDefault();
                    return _cache;
                }

                int version = _spec.ReadSchemaVersion(raw);
                if (version != _spec.SchemaVersion)
                {
                    // 高版本 fail-closed 只读；低版本目前没有迁移路径，同样只读不覆盖。
                    _writeBarrier = true;
                    _lastError = "schema_mismatch:" + version;
                    ModBehaviour.DevLog(_spec.LogPrefix + "[WARNING] 存档 schemaVersion=" + version
                        + " 与当前 " + _spec.SchemaVersion + " 不符，只读不覆盖");
                    _cache = _spec.CreateDefault();
                    return _cache;
                }

                T decoded = _spec.Decode(raw);
                if (decoded == null)
                {
                    _writeBarrier = true;
                    _lastError = "decode_failed";
                    ModBehaviour.DevLog(_spec.LogPrefix + "[WARNING] 存档解码失败，进入写屏障");
                    _cache = _spec.CreateDefault();
                    return _cache;
                }

                _cache = decoded;
                return _cache;
            }
        }

        /// <summary>
        /// no-throw 读当前槽位。官方 SavesSystem.CurrentSlot 只是一个静态可空 int 的读取
        /// （首次访问才读 PlayerPrefs），放在读取热路径上没有代价。
        /// 读不到时返回哨兵值：此后哨兵与哨兵自比一致，不会反复自失效。
        /// </summary>
        private static int ReadCurrentSlotSafe()
        {
            try
            {
                return SavesSystem.CurrentSlot;
            }
            catch (Exception)
            {
                return SlotUnknown;
            }
        }

        /// <summary>
        /// 入队一次写入。战斗中不落盘，只更新缓存与 pending；物理落盘由协调器统一触发。
        /// 写屏障时拒写：否则会用默认值覆盖掉读不动的存档。
        /// </summary>
        internal bool Store(T value)
        {
            if (value == null) return false;
            if (_storeFaulted) return false;
            if (HasWriteBarrier) return false;

            try
            {
                if (_spec.BeforeStore != null) _spec.BeforeStore(value);
                string json = _spec.Encode(value);
                if (json == null) return false;

                lock (_lock)
                {
                    _cache = value;
                    // 至多一个 pending：合并覆盖，不叠加
                    _pendingJson = json;
                    _pendingActive = true;
                }
                return true;
            }
            catch (Exception e)
            {
                _storeFaulted = true;
                _lastError = "encode_failed:" + e.GetType().Name;
                ModBehaviour.DevLog(_spec.LogPrefix + "[ERROR] 存档编码异常，进入 StoreFaulted: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 把 pending 写进 ES3 缓存。IsSaving 时返回 false 并保留 pending（由协调器重试）。
        /// 不在这里调 SaveFile：那是协调器的唯一职责。
        /// </summary>
        internal bool FlushPending()
        {
            lock (_lock)
            {
                if (!_pendingActive || _pendingJson == null) return true;
                if (_writeBarrier)
                {
                    _pendingActive = false;
                    _pendingJson = null;
                    return true;
                }

                try
                {
                    if (SavesSystem.IsSaving)
                    {
                        _lastError = "flush_deferred_is_saving";
                        return false;
                    }

                    SavesSystem.Save<string>(_spec.StorageKey, _pendingJson);

                    // 回读核对：写进去的字符串必须能原样读回来
                    string readback = SavesSystem.Load<string>(_spec.StorageKey);
                    if (!string.Equals(readback, _pendingJson, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("save readback mismatch: " + _spec.StorageKey);
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
                    ModBehaviour.DevLog(_spec.LogPrefix + "[ERROR] 存档 flush 异常，进入 StoreFaulted: " + e.Message);
                    return false;
                }
            }
        }

        #endregion

        #region 清理

        /// <summary>切档 / 删档：丢弃内存状态，从新档重新加载。故障标记不清（跨槽保守）。</summary>
        internal void ResetForSlotChange()
        {
            lock (_lock)
            {
                ResetForSlotChangeLocked();
            }
        }

        private void ResetForSlotChangeLocked()
        {
            _cache = null;
            _cacheSlot = SlotUnknown;
            _pendingJson = null;
            _pendingActive = false;
            _writeBarrier = false;
            _lastError = null;
        }

        /// <summary>静态缓存重置（Mod 卸载 / 宿主重建）。会先退订，并连故障标记一起清。</summary>
        internal void ResetAll()
        {
            ShutdownSubscription();
            lock (_lock)
            {
                ResetForSlotChangeLocked();
                _storeFaulted = false;
            }
        }

        #endregion
    }
}
