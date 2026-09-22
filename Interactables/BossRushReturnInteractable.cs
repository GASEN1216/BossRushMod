using UnityEngine;

namespace BossRush
{
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
            if (BossRushUI.IsGamePaused() || BossRushUI.IsOfficialHudHidden() || !InputManager.InputActived) return;
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

}
