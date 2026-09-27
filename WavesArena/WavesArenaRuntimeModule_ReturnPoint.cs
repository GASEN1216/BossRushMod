using System;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class WavesArenaRuntimeModule
    {
        internal void TryCreateReturnInteractable_WavesArena()
        {
            try
            {
                if (GameObject.Find("BossRushReturnButton_DemoChallenge") != null)
                {
                    return;
                }

                CharacterMainControl main = null;
                try
                {
                    main = CharacterMainControl.Main;
                }
                catch {}

                if (main == null)
                {
                    try
                    {
                        main = PlayerCharacter as CharacterMainControl;
                    }
                    catch {}
                }

                if (main == null)
                {
                    ModBehaviour.DevLog("[BossRush] [WARNING] TryCreateReturnInteractable: 无法找到玩家角色");
                    return;
                }

                Vector3 pos = main.transform.position + main.transform.forward * 2f;
                pos.y += 0.5f;

                GameObject returnButton = GameObject.CreatePrimitive(PrimitiveType.Cube);
                returnButton.name = "BossRushReturnButton_DemoChallenge";
                returnButton.transform.position = pos;

                var renderer = returnButton.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.material.color = Color.green;
                }

                var col = returnButton.GetComponent<Collider>();
                if (col != null)
                {
                    col.isTrigger = true;
                }

                returnButton.AddComponent<BossRushReturnInteractable>();

                ModBehaviour.DevLog("[BossRush] 已创建 BossRush 返回出生点交互点");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [ERROR] TryCreateReturnInteractable 出错: " + e.Message);
            }
        }
    }
}
