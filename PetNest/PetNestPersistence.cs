// ============================================================================
// PetNestPersistence.cs - 遗种巢三 key 存档管线（实施计划 步骤 2）
// ============================================================================
// 硬约束（tests/PetNestPersistenceGuard.py 守卫）：
//   - 三个 key 一律 `SavesSystem.Save<string>` **JSON 整存** + `{schemaVersion, payload}`
//     envelope。不用 typed `Save<T>`：ES3 会把 assembly-qualified 类型名写进存档
//     （见 SavesSystem.UpgradeSaveFileAssemblyInfo 的存在本身），mod 程序集改名/重构
//     就会让老档读不回来；整存字符串把这层耦合彻底切断。
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
    /// 一个存档 key 的整存 store。三个 key 共用同一套写屏障 / pending / 故障语义。
    /// </summary>
    internal sealed class PetNestKeyStore<T> where T : class
    {
        private readonly object _lock = new object();
        private readonly string _key;
        private readonly Func<T, string> _encode;
        private readonly Func<PetNestJsonNode, T> _decode;
        private readonly Func<T> _createDefault;

        private T _cache;
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

        /// <summary>最后一次失败原因。</summary>
        internal string LastError { get { return _lastError; } }

        /// <summary>加载或初始化。幂等：已有缓存直接返回。</summary>
        internal T LoadOrInit()
        {
            lock (_lock)
            {
                if (_cache != null) return _cache;

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

    /// <summary>
    /// 遗种巢持久化门面：三个 key 的 store + 官方存档事件生命周期。
    /// </summary>
    internal static class PetNestPersistence
    {
        #region Store 实例

        private static readonly PetNestKeyStore<PetNestNestData> _nest =
            new PetNestKeyStore<PetNestNestData>(
                PetNestTuning.NestStorageKey,
                PetNestCodec.EncodeNest,
                PetNestCodec.DecodeNest,
                PetNestCodec.CreateDefaultNest);

        private static readonly PetNestKeyStore<PetNestExpeditionData> _expedition =
            new PetNestKeyStore<PetNestExpeditionData>(
                PetNestTuning.ExpeditionStorageKey,
                PetNestCodec.EncodeExpedition,
                PetNestCodec.DecodeExpedition,
                PetNestCodec.CreateDefaultExpedition);

        private static readonly PetNestKeyStore<PetNestMuseumData> _museum =
            new PetNestKeyStore<PetNestMuseumData>(
                PetNestTuning.MuseumStorageKey,
                PetNestCodec.EncodeMuseum,
                PetNestCodec.DecodeMuseum,
                PetNestCodec.CreateDefaultMuseum);

        internal static PetNestKeyStore<PetNestNestData> Nest { get { return _nest; } }
        internal static PetNestKeyStore<PetNestExpeditionData> Expedition { get { return _expedition; } }
        internal static PetNestKeyStore<PetNestMuseumData> Museum { get { return _museum; } }

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
                // 官方收集时把 pending 合并进存档，但**不单独** SaveFile
                _nest.FlushPending();
                _expedition.FlushPending();
                _museum.FlushPending();
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
                _nest.ResetForSlotChange();
                _expedition.ResetForSlotChange();
                _museum.ResetForSlotChange();
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
                _nest.ResetForSlotChange();
                _expedition.ResetForSlotChange();
                _museum.ResetForSlotChange();
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
            get { return _nest.IsStoreFaulted || _expedition.IsStoreFaulted || _museum.IsStoreFaulted; }
        }

        /// <summary>任一 store 处于写屏障（老档 / 未知版本，只读不写）。</summary>
        internal static bool HasAnyWriteBarrier
        {
            get { return _nest.HasWriteBarrier || _expedition.HasWriteBarrier || _museum.HasWriteBarrier; }
        }

        /// <summary>存在待落盘批次。</summary>
        internal static bool HasAnyPendingWrite
        {
            get { return _nest.HasPendingWrite || _expedition.HasPendingWrite || _museum.HasPendingWrite; }
        }

        #endregion

        #region 清理

        /// <summary>静态缓存重置（Mod 卸载 / 宿主重建）。会先退订。</summary>
        internal static void ResetStaticCaches()
        {
            ShutdownSubscription();
            _nest.ResetAll();
            _expedition.ResetAll();
            _museum.ResetAll();
        }

        #endregion
    }
}
