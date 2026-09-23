using System;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// Mode H 普通观战租约（设计提案 §17.1、§25.1）。
    ///
    /// 冻结契约：
    /// - 不得借用只面向暂停菜单的 ZombieModeUIHelper.ModalInputLease（它会把 Time.timeScale 设为 0）；
    /// - 获取顺序固定：创建专用 token 并 DisableInput -> 零移动/攻击输入 -> SetInvincible(true)
    ///   -> 设为 Teams.middle -> 移动到审计后的 modeHSpectatorPos -> 隐藏/锁定持有物表现
    ///   并确认控制目标仍为玩家身体；任一步失败严格逆序回滚并拒绝开战；
    /// - 释放顺序固定：停止接收拍铃 -> 恢复控制目标与持有物显示 -> 仍无敌且中立时恢复原位置
    ///   -> 恢复原 team -> 恢复原 invincible -> ActiveInput(token) -> 恢复光标并销毁 token；
    /// - InputManager.ActiveInput 只检查 source、不检查 instance，因此释放必须自行判空，
    ///   instance 已消失时只丢弃内存 token；
    /// - 观战期间不清空、不复制、不使用玩家物品；拍铃只能由 Mode H UI 命令提交，
    ///   不得短暂恢复角色输入。
    /// </summary>
    internal sealed class ModeHSpectatorLease
    {
        #region 状态

        private GameObject _inputToken;
        private CharacterMainControl _player;
        private Teams _originalTeam;
        private Vector3 _originalPosition;
        private bool _originalInvincible;
        private bool _originalCursorVisible;
        private CursorLockMode _originalCursorLock;
        private bool _hadPlayerReference;

        private bool _acquired;
        private bool _released;
        private bool _inputDisabled;

        /// <summary>
        /// ERROR 互换期间是否已把输入让渡给玩家。与 _inputDisabled 分开：
        /// 后者表示「本租约仍欠一次恢复」，是 Release 的判据，让渡期间必须保持 true。
        /// </summary>
        private bool _inputYielded;
        private bool _teamChanged;
        private bool _invincibleChanged;
        private bool _positionChanged;
        private bool _cursorChanged;

        private int _sceneGeneration;
        private long _ownerToken;
        private bool _bellAccepting;
        private string _lastError;

        /// <summary>官方镜头当前是否被本租约指到了选手身上（还原的唯一判据，幂等）。</summary>
        private bool _cameraRedirected;
        /// <summary>是否把官方战争迷雾临时放宽成「看台周围全视野」，以及放宽前的原值。</summary>
        private bool _visionWidened;
        private bool _originalAllVision;
        private FogOfWarManager _widenedFogManager;
        private static System.Reflection.FieldInfo _allVisionField;
        private static bool _allVisionResolved;

        /// <summary>
        /// 反射缓存的统一清理入口（ModeHRuntimeModule.ResetModeHStaticCaches 调用）：Mod 卸载 / 宿主重建后
        /// 重新解析 FogOfWarManager.allVision，不握着旧程序集的 FieldInfo（2026-09-23 复核 V6-7）。
        /// </summary>
        internal static void ResetStaticCaches()
        {
            _allVisionField = null;
            _allVisionResolved = false;
        }

        #endregion

        #region 只读

        /// <summary>租约是否有效。</summary>
        public bool IsActive { get { return _acquired && !_released; } }

        /// <summary>当前是否接收拍铃命令。</summary>
        public bool IsBellAccepting { get { return IsActive && _bellAccepting; } }

        /// <summary>观战中的玩家身体。</summary>
        public CharacterMainControl PlayerBody { get { return _player; } }

        /// <summary>最后一次失败原因。</summary>
        public string LastError { get { return _lastError; } }

        #endregion

        #region 获取

        /// <summary>按冻结顺序取得观战租约；失败严格逆序回滚。</summary>
        public bool TryAcquire(Vector3 spectatorPos, int sceneGeneration, long ownerToken, out string failureReasonId)
        {
            failureReasonId = null;
            if (_acquired)
            {
                failureReasonId = "spectator_already_acquired";
                return false;
            }

            _sceneGeneration = sceneGeneration;
            _ownerToken = ownerToken;

            try
            {
                _player = CharacterMainControl.Main;
            }
            catch (Exception)
            {
                _player = null;
            }
            if (_player == null)
            {
                failureReasonId = "spectator_player_missing";
                return false;
            }
            _hadPlayerReference = true;

            // 快照：team / position / invincible / cursor
            try
            {
                _originalTeam = _player.Team;
                _originalPosition = _player.transform.position;
                _originalInvincible = _player.Health != null && _player.Health.Invincible;
                _originalCursorVisible = Cursor.visible;
                _originalCursorLock = Cursor.lockState;
            }
            catch (Exception e)
            {
                failureReasonId = "spectator_snapshot_failed:" + e.GetType().Name;
                return false;
            }

            int step = 0;
            try
            {
                // 步骤 1：创建专用 token 并阻断角色输入（必须先于移动与阵营变更）
                _inputToken = new GameObject("ModeH_SpectatorInputToken");
                UnityEngine.Object.DontDestroyOnLoad(_inputToken);
                InputManager.DisableInput(_inputToken);
                _inputDisabled = true;
                step = 1;

                // 步骤 2：无敌
                if (_player.Health != null)
                {
                    _player.Health.SetInvincible(true);
                    _invincibleChanged = true;
                }
                step = 2;

                // 步骤 3：中立阵营（避免成为友军/敌军/第三方单位）
                _player.SetTeam(Teams.middle);
                _teamChanged = true;
                step = 3;

                // 步骤 4：移动到审计后的看台位置
                _player.SetPosition(spectatorPos);
                _positionChanged = true;
                step = 4;

                // 步骤 5：保持光标可见（拍铃按钮需要点击）
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
                _cursorChanged = true;

                _acquired = true;
                _released = false;
                _bellAccepting = true;
                _lastError = null;
                ModBehaviour.DevLog("[ModeH] 观战租约已取得");
                return true;
            }
            catch (Exception e)
            {
                failureReasonId = "spectator_acquire_failed:" + e.GetType().Name;
                _lastError = failureReasonId;
                RollbackTo(step);
                return false;
            }
        }

        #endregion

        #region 拍铃门控

        /// <summary>停止接收拍铃（结算、倒地、技术中止、离场前调用）。</summary>
        public void StopAcceptingBell()
        {
            _bellAccepting = false;
        }

        #endregion

        #region 释放

        /// <summary>
        /// 幂等释放。scene generation 已变化时不向旧 Unity 引用写值，
        /// 只释放仍存活的输入 token 与内存 owner。
        /// </summary>
        public void Release(int currentSceneGeneration)
        {
            if (_released) return;
            _released = true;
            _acquired = false;

            // 1) 停止接收拍铃
            _bellAccepting = false;

            // 1.5) 恢复控制目标的观感：官方镜头对回玩家身体、战争迷雾还原（幂等，场景已换时只丢引用）
            RestoreCameraTarget();

            bool sameGeneration = currentSceneGeneration == _sceneGeneration;
            bool playerAlive = _hadPlayerReference && _player != null;

            if (sameGeneration && playerAlive)
            {
                // 2) 恢复位置（此时仍处于无敌 + 中立保护下）
                if (_positionChanged)
                {
                    try { _player.SetPosition(_originalPosition); }
                    catch (Exception)
                    {
                        // 位置恢复失败不阻断后续步骤
                    }
                }

                // 3) 恢复阵营
                if (_teamChanged)
                {
                    try { _player.SetTeam(_originalTeam); }
                    catch (Exception)
                    {
                        // 阵营恢复失败不阻断后续步骤
                    }
                }

                // 4) 恢复无敌
                if (_invincibleChanged && _player.Health != null)
                {
                    try { _player.Health.SetInvincible(_originalInvincible); }
                    catch (Exception)
                    {
                        // 无敌恢复失败不阻断后续步骤
                    }
                }
            }

            // 5) 恢复输入：ActiveInput 只检查 source，不检查 instance，必须自行判空
            if (_inputDisabled)
            {
                try
                {
                    if (IsInputManagerAlive() && _inputToken != null)
                    {
                        InputManager.ActiveInput(_inputToken);
                    }
                }
                catch (Exception)
                {
                    // instance 已消失时只丢弃内存 token
                }
                _inputDisabled = false;
            }

            // 6) 恢复光标并销毁 token
            if (_cursorChanged)
            {
                try
                {
                    Cursor.visible = _originalCursorVisible;
                    Cursor.lockState = _originalCursorLock;
                }
                catch (Exception)
                {
                    // 光标恢复失败不阻断销毁
                }
                _cursorChanged = false;
            }

            DestroyToken();

            _player = null;
            _hadPlayerReference = false;
            _ownerToken = 0;
        }

        private void RollbackTo(int completedStep)
        {
            // 严格逆序回滚
            if (completedStep >= 5 && _cursorChanged)
            {
                try
                {
                    Cursor.visible = _originalCursorVisible;
                    Cursor.lockState = _originalCursorLock;
                }
                catch (Exception)
                {
                    // 光标恢复失败不阻断后续回滚
                }
                _cursorChanged = false;
            }
            if (completedStep >= 4 && _positionChanged && _player != null)
            {
                try { _player.SetPosition(_originalPosition); }
                catch (Exception)
                {
                    // 位置恢复失败不阻断后续回滚
                }
                _positionChanged = false;
            }
            if (completedStep >= 3 && _teamChanged && _player != null)
            {
                try { _player.SetTeam(_originalTeam); }
                catch (Exception)
                {
                    // 阵营恢复失败不阻断后续回滚
                }
                _teamChanged = false;
            }
            if (completedStep >= 2 && _invincibleChanged && _player != null && _player.Health != null)
            {
                try { _player.Health.SetInvincible(_originalInvincible); }
                catch (Exception)
                {
                    // 无敌恢复失败不阻断后续回滚
                }
                _invincibleChanged = false;
            }
            if (completedStep >= 1 && _inputDisabled)
            {
                try
                {
                    if (IsInputManagerAlive() && _inputToken != null)
                    {
                        InputManager.ActiveInput(_inputToken);
                    }
                }
                catch (Exception)
                {
                    // 输入恢复失败时只丢弃内存 token
                }
                _inputDisabled = false;
            }
            DestroyToken();
            _player = null;
            _hadPlayerReference = false;
        }

        /// <summary>
        /// InputManager 的内部 instance 是私有静态成员，Mod 侧的等价判空是
        /// LevelManager.Instance.InputManager；ActiveInput 不做该判空，必须由调用方保证。
        /// </summary>
        private static bool IsInputManagerAlive()
        {
            try
            {
                return LevelManager.Instance != null && LevelManager.Instance.InputManager != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void DestroyToken()
        {
            if (_inputToken == null) return;
            try
            {
                UnityEngine.Object.Destroy(_inputToken);
            }
            catch (Exception)
            {
                // token 销毁失败只丢弃引用
            }
            _inputToken = null;
        }

        #endregion

        #region 观战镜头（2026-09-23 owner 实测：「选完后完全看不到有斗蛐蛐」）

        /// <summary>
        /// 每帧由模块调用：交战期间把官方 GameCamera 对准当前登场选手，离开交战相位对回玩家身体。
        ///
        /// 只调官方公开的 <c>GameCamera.SetTarget</c>，不动 <c>LevelManager.ControllingCharacter</c>——
        /// 后者会把选手交给玩家操控、关掉它的 AI（那是 ERROR 互换的事）。输入仍由本租约阻断，
        /// 镜头跟着谁都不会让玩家动到选手。ERROR 互换期间官方自己把镜头交给受控选手，与这里目标一致；
        /// 互换结束官方把镜头还给玩家身体，下一帧这里再指回选手。
        ///
        /// 交战中选手引用为空或已销毁（先发倒下、接力正在入场）时保持当前镜头不动，不闪回看台。
        /// O(1)、零分配：只做两次引用比较，目标变化时才调 SetTarget（它会重置 0.6 秒的镜头过渡）。
        /// </summary>
        public void SyncCameraTarget(CharacterMainControl fighter, bool matchLive)
        {
            if (!IsActive) return;
            if (!matchLive)
            {
                if (_cameraRedirected || _visionWidened) RestoreCameraTarget();
                return;
            }
            if (fighter == null) return;
            try
            {
                GameCamera camera = GameCamera.Instance;
                if (camera == null) return;
                if (!ReferenceEquals(camera.target, fighter)) camera.SetTarget(fighter);
                _cameraRedirected = true;
                if (!_visionWidened) WidenSpectatorVision();
            }
            catch (Exception)
            {
                // 镜头跟不上只是看不清，不影响比赛与还原
            }
        }

        /// <summary>
        /// 幂等还原：镜头对回当前受控角色（正常就是玩家身体），战争迷雾恢复原值。
        /// 结算、技术中止、倒地收尾、离场与租约释放都会走到这里；场景已换时旧引用判空后只清标记。
        /// </summary>
        public void RestoreCameraTarget()
        {
            if (_cameraRedirected)
            {
                _cameraRedirected = false;
                try
                {
                    GameCamera camera = GameCamera.Instance;
                    CharacterMainControl body = null;
                    if (LevelManager.Instance != null) body = LevelManager.Instance.ControllingCharacter;
                    if (body == null) body = _player != null ? _player : CharacterMainControl.Main;
                    if (camera != null && body != null && !ReferenceEquals(camera.target, body)) camera.SetTarget(body);
                }
                catch (Exception)
                {
                    // 关卡已卸载：镜头随场景一起重建，无需补偿
                }
            }
            RestoreSpectatorVision();
        }

        /// <summary>
        /// 战争迷雾以受控角色（看台上的玩家身体）为中心，只露出它朝向的扇形。选手在十几到四十多米外开打，
        /// 在 Raid 图上很可能整场被 DuckovHider 藏起来。官方 FogOfWarManager 自带「全视野」分支
        /// （非 Raid 图或关闭迷雾规则时 360° / 50 米），这里只在观战期间临时打开它，结束还原原值。
        /// 字段是私有的，读不到就什么都不做（镜头仍然跟随，最坏只是 Raid 图上看不到人）。
        /// </summary>
        private void WidenSpectatorVision()
        {
            try
            {
                FogOfWarManager fog = LevelManager.Instance != null ? LevelManager.Instance.FogOfWarManager : null;
                if (fog == null) return;
                if (!_allVisionResolved)
                {
                    _allVisionResolved = true;
                    _allVisionField = typeof(FogOfWarManager).GetField("allVision",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                }
                if (_allVisionField == null || _allVisionField.FieldType != typeof(bool)) return;
                _originalAllVision = (bool)_allVisionField.GetValue(fog);
                _widenedFogManager = fog;
                _visionWidened = true;
                if (!_originalAllVision) _allVisionField.SetValue(fog, true);
            }
            catch (Exception)
            {
                _visionWidened = false;
                _widenedFogManager = null;
            }
        }

        private void RestoreSpectatorVision()
        {
            if (!_visionWidened) return;
            _visionWidened = false;
            FogOfWarManager fog = _widenedFogManager;
            _widenedFogManager = null;
            try
            {
                if (fog != null && _allVisionField != null && !_originalAllVision)
                {
                    _allVisionField.SetValue(fog, false);
                }
            }
            catch (Exception)
            {
                // 迷雾管理器已随场景销毁：新场景的实例本来就是原值
            }
        }

        #endregion

        #region ERROR 互换期间的输入让渡（§17.6.5）

        /// <summary>
        /// 互换生效时临时解除本租约的输入阻断，让玩家真的能操纵被接管的选手。
        ///
        /// 【为什么必须有这一步】TryAcquire 的步骤 1 就 DisableInput 了，而且只在
        /// Release / RollbackTo 里恢复。不让渡的话，接通后的 ERROR 互换会把一个
        /// **动不了的选手**交到玩家手上，§17.6.5 整条退化成一次镜头切换。
        ///
        /// 【为什么不破坏 Release 的对称性】只操作本租约自己的 token，而
        /// InputManager.blockInputSources 是 HashSet，同一 token 的增删幂等可重复。
        /// _inputDisabled 保持 true，Release 照走同一条恢复分支（届时是 no-op），
        /// 最终态恒为「输入已恢复」——安全方向。
        ///
        /// 让渡失败只意味着玩家仍动不了，绝不升级为技术中止。
        /// </summary>
        public void YieldInputForErrorSwap()
        {
            if (_inputYielded) return;
            try
            {
                if (IsInputManagerAlive() && _inputToken != null)
                {
                    InputManager.ActiveInput(_inputToken);
                    _inputYielded = true;
                }
            }
            catch (Exception)
            {
                // instance 已消失：玩家仍动不了，但比赛与还原链不受影响
            }
        }

        /// <summary>互换结束后立刻收回输入阻断。幂等，可被释放路径重复调用。</summary>
        public void ReclaimInputAfterErrorSwap()
        {
            if (!_inputYielded) return;
            _inputYielded = false;
            try
            {
                if (IsInputManagerAlive() && _inputToken != null)
                {
                    InputManager.DisableInput(_inputToken);
                }
            }
            catch (Exception)
            {
                // 收不回来时看台身体可动，但它是中立无敌的，不影响比赛结算
            }
        }

        #endregion
    }
}
