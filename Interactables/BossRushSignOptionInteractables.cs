// ============================================================================
// BossRushSignOptionInteractables.cs - 竞技场路牌的子选项交互
// ============================================================================
// 2026-11-25 从 BossRushInteractables.cs 原样提取（该文件到了 1200 行预算）：
//   - BossRushEntryInteractable: "哎哟~你干嘛~" 生小鸡（出征令升为路牌主交互后挪到子选项）
//   - BossRushNextWaveInteractable: 下一波交互
// 路牌主交互与其余选项仍在 BossRushInteractables.cs。
// ============================================================================

using System;
using UnityEngine;

namespace BossRush
{
    public class BossRushEntryInteractable : InteractableBase
    {
        protected override void Awake()
        {
            try
            {
                // 入口显示为“哎哟~你干嘛~”，鼓励玩家多按几次下蛋
                this.overrideInteractName = true;
                this._overrideInteractNameKey = "BossRush_Sign_Entry";
                this.InteractName = "BossRush_Sign_Entry";
            }
            catch {}
            try
            {
                this.interactCollider = GetComponent<Collider>();
            }
            catch {}
            try
            {
                // 将交互标记放在路牌中部（模型已向上偏移1米，所以这里用0）
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
                // 作为路牌子选项只经交互组菜单出现：碰撞体必须关掉。开着时它和路牌碰撞体同点，
                // 官方 CA_Interact 按距离挑目标会挑到这个非组交互，菜单只剩「哎哟~你干嘛~」、出征令消失。
                if (this.interactCollider != null)
                {
                    this.interactCollider.enabled = false;
                }
            }
            catch {}
            try
            {
                this.MarkerActive = false;
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
                this._overrideInteractNameKey = "BossRush_Sign_Entry";
                this.InteractName = "BossRush_Sign_Entry";
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
                if (BossRush.ModBehaviour.Instance != null)
                {
                    BossRush.ModBehaviour.Instance.TrySpawnEggForPlayer();
                }
            }
            catch {}
        }
    }

    public class BossRushNextWaveInteractable : InteractableBase
    {
        protected override void Awake()
        {
            try
            {
                // 使用本地化键，避免两边出现 *，并保持与其他难度选项一致的颜色风格
                this.overrideInteractName = true;
                this._overrideInteractNameKey = "BossRush_Sign_NextWave";
                this.InteractName = "BossRush_Sign_NextWave";
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
                if (this.interactCollider != null)
                {
                    this.interactCollider.enabled = false;
                }
            }
            catch {}
            try
            {
                this.MarkerActive = false;
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
                // 使用本地化键，避免两边出现 *，并保持与其他难度选项一致的颜色风格
                this.overrideInteractName = true;
                this._overrideInteractNameKey = "BossRush_Sign_NextWave";
                this.InteractName = "BossRush_Sign_NextWave";
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
                if (BossRush.ModBehaviour.Instance != null)
                {
                    BossRush.ModBehaviour.Instance.StartNextWaveCountdown();
                }
            }
            catch {}
            try
            {
                UnityEngine.Object.Destroy(base.gameObject);
            }
            catch {}
        }
    }
}
