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
