// ============================================================================
// DebugAndToolsPlacementAndInspection.cs - placement, FPS, map-click, and building debug helpers
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Duckov.Utilities;

namespace BossRush
{
    public partial class ModBehaviour
    {
        /// <summary>
        /// F5 调试：输出玩家脚下/最近的建筑物详细信息
        /// 使用反射访问 Building 类，避免直接引用导致的程序集依赖问题
        /// </summary>
        private void LogNearbyBuildingInfo(Vector3 playerPos, float radius = 10f)
        {
            try
            {
                string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

                // 通过反射获取 Building 类型
                Type buildingType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        buildingType = asm.GetType("Duckov.Buildings.Building");
                        if (buildingType != null) break;
                    }
                    catch { }
                }

                if (buildingType == null)
                {
                    DevLog("[BossRush] F5 建筑调试：未找到 Building 类型");
                    return;
                }

                // 查找场景中所有 Building 组件
                UnityEngine.Object[] allBuildings = UnityEngine.Object.FindObjectsOfType(buildingType);

                if (allBuildings == null || allBuildings.Length == 0)
                {
                    DevLog("[BossRush] F5 建筑调试：场景 " + sceneName + " 中未找到任何 Building 组件");
                    return;
                }

                // 按距离排序，找到最近的建筑
                List<Component> nearbyBuildings = new List<Component>();
                foreach (var obj in allBuildings)
                {
                    Component building = obj as Component;
                    if (building == null || building.gameObject == null) continue;

                    float dist = Vector3.Distance(playerPos, building.transform.position);
                    if (dist <= radius)
                    {
                        nearbyBuildings.Add(building);
                    }
                }

                // 按距离排序
                nearbyBuildings.Sort((a, b) =>
                    Vector3.Distance(playerPos, a.transform.position).CompareTo(
                    Vector3.Distance(playerPos, b.transform.position)));

                if (nearbyBuildings.Count == 0)
                {
                    DevLog("[BossRush] F5 建筑调试：玩家周围 " + radius + "m 内未找到任何建筑物（场景共有 " + allBuildings.Length + " 个建筑）");
                    return;
                }

                DevLog("[BossRush] F5 建筑调试：场景=" + sceneName + ", 玩家位置=" + playerPos + ", 半径=" + radius + "m, 找到 " + nearbyBuildings.Count + " 个建筑");

                // 获取 Building 类的属性和方法
                PropertyInfo idProp = buildingType.GetProperty("ID");
                PropertyInfo displayNameProp = buildingType.GetProperty("DisplayName");
                PropertyInfo descriptionProp = buildingType.GetProperty("Description");
                PropertyInfo guidProp = buildingType.GetProperty("GUID");
                PropertyInfo dimensionsProp = buildingType.GetProperty("Dimensions");

                // 获取 BuildingManager 类型
                Type buildingManagerType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        buildingManagerType = asm.GetType("Duckov.Buildings.BuildingManager");
                        if (buildingManagerType != null) break;
                    }
                    catch { }
                }
                MethodInfo getBuildingInfoMethod = null;
                if (buildingManagerType != null)
                {
                    getBuildingInfoMethod = buildingManagerType.GetMethod("GetBuildingInfo", BindingFlags.Public | BindingFlags.Static);
                }

                // 输出每个建筑的详细信息
                int outputCount = Math.Min(nearbyBuildings.Count, 5); // 最多输出5个
                for (int i = 0; i < outputCount; i++)
                {
                    Component building = nearbyBuildings[i];
                    if (building == null) continue;

                    float dist = Vector3.Distance(playerPos, building.transform.position);
                    string path = GetTransformPath(building.transform);

                    // 基本信息（通过反射获取）
                    string id = "";
                    string displayName = "";
                    string description = "";
                    int guid = 0;
                    string dimensions = "";

                    try
                    {
                        if (idProp != null)
                        {
                            object val = idProp.GetValue(building, null);
                            if (val != null) id = val.ToString();
                        }
                    }
                    catch { }

                    try
                    {
                        if (displayNameProp != null)
                        {
                            object val = displayNameProp.GetValue(building, null);
                            if (val != null) displayName = val.ToString();
                        }
                    }
                    catch { }

                    try
                    {
                        if (descriptionProp != null)
                        {
                            object val = descriptionProp.GetValue(building, null);
                            if (val != null) description = val.ToString();
                        }
                    }
                    catch { }

                    try
                    {
                        if (guidProp != null)
                        {
                            object val = guidProp.GetValue(building, null);
                            if (val != null) guid = (int)val;
                        }
                    }
                    catch { }

                    try
                    {
                        if (dimensionsProp != null)
                        {
                            object val = dimensionsProp.GetValue(building, null);
                            if (val != null) dimensions = val.ToString();
                        }
                    }
                    catch { }

                    // 获取 BuildingInfo 详细信息
                    string costInfo = "";
                    string requireBuildingsInfo = "";
                    int maxAmount = 0;
                    int currentAmount = 0;
                    bool reachedLimit = false;

                    if (getBuildingInfoMethod != null && !string.IsNullOrEmpty(id))
                    {
                        try
                        {
                            object infoObj = getBuildingInfoMethod.Invoke(null, new object[] { id });
                            if (infoObj != null)
                            {
                                Type infoType = infoObj.GetType();

                                // 检查 Valid 属性
                                PropertyInfo validProp = infoType.GetProperty("Valid");
                                bool valid = validProp != null && (bool)validProp.GetValue(infoObj, null);

                                if (valid)
                                {
                                    // 获取 maxAmount
                                    FieldInfo maxAmountField = infoType.GetField("maxAmount");
                                    if (maxAmountField != null) maxAmount = (int)maxAmountField.GetValue(infoObj);

                                    // 获取 CurrentAmount
                                    PropertyInfo currentAmountProp = infoType.GetProperty("CurrentAmount");
                                    if (currentAmountProp != null) currentAmount = (int)currentAmountProp.GetValue(infoObj, null);

                                    // 获取 ReachedAmountLimit
                                    PropertyInfo reachedLimitProp = infoType.GetProperty("ReachedAmountLimit");
                                    if (reachedLimitProp != null) reachedLimit = (bool)reachedLimitProp.GetValue(infoObj, null);

                                    // 获取 cost
                                    FieldInfo costField = infoType.GetField("cost");
                                    if (costField != null)
                                    {
                                        object costObj = costField.GetValue(infoObj);
                                        if (costObj != null)
                                        {
                                            Type costType = costObj.GetType();
                                            FieldInfo moneyField = costType.GetField("money");
                                            FieldInfo itemsField = costType.GetField("items");

                                            long money = moneyField != null ? (long)moneyField.GetValue(costObj) : 0;
                                            Array items = null;
                                            if (itemsField != null)
                                            {
                                                items = itemsField.GetValue(costObj) as Array;
                                            }
                                            int itemCount = items != null ? items.Length : 0;

                                            if (money > 0 || itemCount > 0)
                                            {
                                                costInfo = "金钱=" + money;
                                                if (itemCount > 0) costInfo += ", 物品数=" + itemCount;
                                            }
                                        }
                                    }

                                    // 获取 requireBuildings
                                    FieldInfo requireBuildingsField = infoType.GetField("requireBuildings");
                                    if (requireBuildingsField != null)
                                    {
                                        string[] requireBuildings = requireBuildingsField.GetValue(infoObj) as string[];
                                        if (requireBuildings != null && requireBuildings.Length > 0)
                                        {
                                            requireBuildingsInfo = string.Join(",", requireBuildings);
                                        }
                                    }
                                }
                            }
                        }
                        catch { }
                    }

                    // 获取子对象信息
                    int childCount = building.transform.childCount;
                    string childrenInfo = "";
                    for (int c = 0; c < Math.Min(childCount, 5); c++)
                    {
                        Transform child = building.transform.GetChild(c);
                        if (child != null)
                        {
                            childrenInfo += (c > 0 ? ", " : "") + child.name;
                        }
                    }
                    if (childCount > 5) childrenInfo += "...";

                    // 获取组件列表
                    Component[] components = building.GetComponents<Component>();
                    string componentsInfo = "";
                    foreach (var comp in components)
                    {
                        if (comp == null) continue;
                        string typeName = comp.GetType().Name;
                        componentsInfo += (componentsInfo.Length > 0 ? ", " : "") + typeName;
                    }

                    DevLog("[BossRush] F5 建筑 #" + (i + 1) +
                           ": ID=" + id +
                           ", 名称=" + displayName +
                           ", GUID=" + guid +
                           ", 距离=" + dist.ToString("F2") + "m" +
                           ", 位置=" + building.transform.position +
                           ", 尺寸=" + dimensions +
                           ", 数量=" + currentAmount + "/" + maxAmount +
                           ", 达上限=" + reachedLimit);

                    DevLog("[BossRush]   路径=" + path +
                           ", 费用=[" + costInfo + "]" +
                           ", 前置建筑=[" + requireBuildingsInfo + "]");

                    if (!string.IsNullOrEmpty(description))
                    {
                        DevLog("[BossRush]   描述=" + description);
                    }

                    DevLog("[BossRush]   子对象(" + childCount + ")=[" + childrenInfo + "]" +
                           ", 组件=[" + componentsInfo + "]");
                }

                if (nearbyBuildings.Count > outputCount)
                {
                    DevLog("[BossRush] F5 建筑调试：还有 " + (nearbyBuildings.Count - outputCount) + " 个建筑未显示");
                }
            }
            catch (System.Exception e)
            {
                DevLog("[BossRush] F5 建筑调试出错: " + e.Message + "\n" + e.StackTrace);
            }
        }

        // ============================================================================
        // 放置模式功能
        // ============================================================================

        /// <summary>
        /// 切换放置模式（F6 调用）
        /// </summary>
        private void TogglePlacementMode()
        {
            if (placementModeActive)
            {
                // 退出放置模式
                ExitPlacementMode();
            }
            else
            {
                // 进入放置模式
                EnterPlacementMode();
            }
        }

        /// <summary>
        /// 进入放置模式
        /// </summary>
        private void EnterPlacementMode()
        {
            // 检查是否有 F5 记住的对象
            if (lastNearestGameObject == null)
            {
                DevLog("[BossRush] 放置模式：没有记住的对象，请先按 F5 选择一个对象");
                return;
            }

            // 获取玩家
            CharacterMainControl main = CharacterMainControl.Main;
            if (main == null)
            {
                DevLog("[BossRush] 放置模式：未找到玩家");
                return;
            }

            try
            {
                // 收起武器
                main.ChangeHoldItem(null);
                DevLog("[BossRush] 放置模式：已收起武器");

                // 初始化旋转角度为原对象的Y轴旋转
                placementRotationY = lastNearestGameObject.transform.eulerAngles.y;

                // 创建预览对象
                CreatePreviewObject();

                if (placementPreviewObject != null)
                {
                    placementModeActive = true;
                    DevLog("[BossRush] 放置模式：已进入，鼠标左键确认放置，滚轮旋转，再次按 F6 退出");
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] 进入放置模式失败: " + e.Message);
                ExitPlacementMode();
            }
        }

        /// <summary>
        /// 退出放置模式
        /// </summary>
        private void ExitPlacementMode()
        {
            placementModeActive = false;

            // 销毁预览对象
            if (placementPreviewObject != null)
            {
                UnityEngine.Object.Destroy(placementPreviewObject);
                placementPreviewObject = null;
            }

            // 清理材质缓存
            originalMaterials.Clear();

            // 清除待删除选中状态
            ClearPendingDelete();

            // 清空预制体列表
            nearbyPrefabList.Clear();
            prefabListInitialized = false;
            currentPrefabIndex = 0;

            DevLog("[BossRush] 放置模式：已退出");
        }

        /// <summary>
        /// 切换预制体（滚轮触发）
        /// </summary>
        /// <param name="direction">方向：1=下一个，-1=上一个</param>
        private void SwitchPrefab(int direction)
        {
            // 如果列表未初始化，先扫描附近预制体
            if (!prefabListInitialized)
            {
                ScanNearbyPrefabs();
            }

            // 如果列表为空，无法切换
            if (nearbyPrefabList.Count == 0)
            {
                DevLog("[BossRush] 放置模式：附近没有可用的预制体");
                return;
            }

            // 计算新索引
            currentPrefabIndex += direction;

            // 循环索引
            if (currentPrefabIndex >= nearbyPrefabList.Count)
            {
                currentPrefabIndex = 0;
            }
            else if (currentPrefabIndex < 0)
            {
                currentPrefabIndex = nearbyPrefabList.Count - 1;
            }

            // 获取新的预制体
            GameObject newPrefab = nearbyPrefabList[currentPrefabIndex];
            if (newPrefab == null)
            {
                // 如果对象已被销毁，从列表中移除并重试
                nearbyPrefabList.RemoveAt(currentPrefabIndex);
                if (nearbyPrefabList.Count > 0)
                {
                    currentPrefabIndex = currentPrefabIndex % nearbyPrefabList.Count;
                    SwitchPrefab(0); // 重新获取当前索引的对象
                }
                return;
            }

            // 保存当前预览对象的位置和旋转
            Vector3 currentPos = Vector3.zero;
            if (placementPreviewObject != null)
            {
                currentPos = placementPreviewObject.transform.position;
                UnityEngine.Object.Destroy(placementPreviewObject);
                placementPreviewObject = null;
            }

            // 切换到新预制体
            lastNearestGameObject = newPrefab;
            placementRotationY = newPrefab.transform.eulerAngles.y;

            // 创建新的预览对象
            CreatePreviewObject();

            // 恢复位置
            if (placementPreviewObject != null && currentPos != Vector3.zero)
            {
                placementPreviewObject.transform.position = currentPos;
            }

            DevLog("[BossRush] 放置模式：切换到预制体 [" + (currentPrefabIndex + 1) + "/" + nearbyPrefabList.Count + "] " + newPrefab.name);
        }

        /// <summary>
        /// 扫描玩家附近的预制体，构建可切换列表（按基础名称去重）
        /// </summary>
        private void ScanNearbyPrefabs()
        {
            nearbyPrefabList.Clear();

            // 获取玩家位置
            CharacterMainControl main = CharacterMainControl.Main;
            if (main == null)
            {
                DevLog("[BossRush] 放置模式：无法获取玩家位置");
                prefabListInitialized = true;
                return;
            }

            Vector3 playerPos = main.transform.position;
            Transform playerTransform = main.transform;

            // 扫描附近所有带碰撞器的对象
            Collider[] colliders = Physics.OverlapSphere(playerPos, PREFAB_SCAN_RADIUS);
            HashSet<GameObject> addedRoots = new HashSet<GameObject>();
            HashSet<string> addedBaseNames = new HashSet<string>(); // 用于按基础名称去重

            foreach (var col in colliders)
            {
                if (col == null) continue;

                // 跳过玩家自身
                if (IsChildOf(col.transform, playerTransform)) continue;

                // 找到根对象
                GameObject root = FindTrueRootObject(col.transform);
                if (root == null) continue;

                // 跳过已添加的对象实例
                if (addedRoots.Contains(root)) continue;

                // 跳过预览对象
                if (root.name.EndsWith("_Preview")) continue;

                // 跳过没有渲染器的对象（可能是纯碰撞体）
                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) continue;

                // 获取基础名称（去掉 _Clone 后缀和数字后缀）
                string baseName = GetBasePrefabName(root.name);

                // 按基础名称去重，只保留第一个遇到的
                if (addedBaseNames.Contains(baseName)) continue;

                // 添加到列表
                nearbyPrefabList.Add(root);
                addedRoots.Add(root);
                addedBaseNames.Add(baseName);
            }

            // 按基础名称排序，方便查找
            nearbyPrefabList.Sort((a, b) => string.Compare(GetBasePrefabName(a.name), GetBasePrefabName(b.name), StringComparison.Ordinal));

            // 如果当前选中的对象在列表中，设置为当前索引
            if (lastNearestGameObject != null)
            {
                int idx = nearbyPrefabList.IndexOf(lastNearestGameObject);
                if (idx >= 0)
                {
                    currentPrefabIndex = idx;
                }
                else
                {
                    // 尝试按基础名称查找
                    string targetBaseName = GetBasePrefabName(lastNearestGameObject.name);
                    for (int i = 0; i < nearbyPrefabList.Count; i++)
                    {
                        if (GetBasePrefabName(nearbyPrefabList[i].name) == targetBaseName)
                        {
                            currentPrefabIndex = i;
                            break;
                        }
                    }
                }
            }

            prefabListInitialized = true;
            DevLog("[BossRush] 放置模式：扫描到 " + nearbyPrefabList.Count + " 个不同类型的预制体（半径 " + PREFAB_SCAN_RADIUS + "m）");
        }

        /// <summary>
        /// 获取预制体的基础名称（去掉 _Clone、_Preview 和末尾数字后缀）
        /// 例如：Prfb_BoxGroup_16_Clone -> Prfb_BoxGroup_16
        ///       Prfb_Wall_01 -> Prfb_Wall_01
        /// </summary>
        private string GetBasePrefabName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;

            // 去掉 _Clone 后缀
            if (name.EndsWith("_Clone"))
            {
                name = name.Substring(0, name.Length - 6);
            }

            // 去掉 _Preview 后缀
            if (name.EndsWith("_Preview"))
            {
                name = name.Substring(0, name.Length - 8);
            }

            // 去掉末尾的 (数字) 格式，如 "Object (1)"
            int parenIdx = name.LastIndexOf(" (");
            if (parenIdx > 0 && name.EndsWith(")"))
            {
                name = name.Substring(0, parenIdx);
            }

            return name;
        }

        /// <summary>
        /// 创建预览对象（完整克隆，只禁用碰撞器）
        /// 简化方案：直接实例化原始对象，保持完整渲染，只禁用碰撞器避免物理干扰
        /// 确认放置时复制一份真实的，然后销毁预览
        /// </summary>
        private void CreatePreviewObject()
        {
            if (lastNearestGameObject == null) return;

            // 克隆对象（完整克隆，保持所有渲染效果）
            placementPreviewObject = UnityEngine.Object.Instantiate(lastNearestGameObject);
            placementPreviewObject.name = lastNearestGameObject.name + "_Preview";

            // 只禁用碰撞器，避免物理干扰，但保持完整渲染
            Collider[] colliders = placementPreviewObject.GetComponentsInChildren<Collider>(true);
            foreach (var col in colliders)
            {
                col.enabled = false;
            }

            // 禁用 Rigidbody 避免物理模拟
            Rigidbody[] rigidbodies = placementPreviewObject.GetComponentsInChildren<Rigidbody>(true);
            foreach (var rb in rigidbodies)
            {
                rb.isKinematic = true;
            }

            DevLog("[BossRush] 放置模式：预览对象已创建（完整渲染模式）");
        }


        /// <summary>
        /// 更新放置模式（在 Update 中调用）
        /// </summary>
        private void UpdatePlacementMode()
        {
            if (!placementModeActive || placementPreviewObject == null) return;

            try
            {
                // 获取相机
                Camera cam = null;
                try
                {
                    if (GameCamera.Instance != null)
                    {
                        cam = GameCamera.Instance.renderCamera;
                    }
                }
                catch { }

                if (cam == null)
                {
                    cam = Camera.main;
                }

                if (cam == null) return;

                // 处理滚轮：默认旋转预览对象，按住Shift时切换预制体
                float scroll = UnityEngine.Input.GetAxis("Mouse ScrollWheel");
                if (scroll != 0f)
                {
                    bool shiftHeld = UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift);

                    if (shiftHeld)
                    {
                        // 按住Shift：切换预制体
                        SwitchPrefab(scroll > 0f ? 1 : -1);
                    }
                    else
                    {
                        // 不按Shift：旋转预览对象
                        if (scroll > 0f)
                        {
                            placementRotationY += ROTATION_STEP;
                        }
                        else
                        {
                            placementRotationY -= ROTATION_STEP;
                        }
                        // 保持角度在 0-360 范围内
                        if (placementRotationY >= 360f) placementRotationY -= 360f;
                        if (placementRotationY < 0f) placementRotationY += 360f;

                        DevLog("[BossRush] 放置模式：旋转角度 = " + placementRotationY.ToString("F0") + "°");
                    }
                }

                // 获取鼠标位置并转换为射线
                Vector3 mousePos = UnityEngine.Input.mousePosition;
                Ray ray = cam.ScreenPointToRay(mousePos);

                // 射线检测地板和墙体（扩大检测范围）
                LayerMask groundMask = Duckov.Utilities.GameplayDataSettings.Layers.groundLayerMask |
                                       Duckov.Utilities.GameplayDataSettings.Layers.wallLayerMask;
                RaycastHit hit;

                // 先尝试检测地板/墙体
                bool hitSomething = Physics.Raycast(ray, out hit, 500f, groundMask, QueryTriggerInteraction.Ignore);

                // 如果没有命中，尝试用更宽泛的检测（所有碰撞体）
                if (!hitSomething)
                {
                    hitSomething = Physics.Raycast(ray, out hit, 500f, ~0, QueryTriggerInteraction.Ignore);
                }

                if (hitSomething)
                {
                    // 移动预览对象到命中点
                    placementPreviewObject.transform.position = hit.point;

                    // 应用旋转（保持原始X和Z轴旋转，只修改Y轴）
                    if (lastNearestGameObject != null)
                    {
                        Vector3 originalEuler = lastNearestGameObject.transform.eulerAngles;
                        placementPreviewObject.transform.rotation = Quaternion.Euler(originalEuler.x, placementRotationY, originalEuler.z);
                    }
                }

                // 检测鼠标左键点击 - 确认放置
                if (UnityEngine.Input.GetMouseButtonDown(0))
                {
                    ConfirmPlacement();
                }

                // 检测鼠标右键点击 - 选中/删除建筑物（两次确认）
                if (UnityEngine.Input.GetMouseButtonDown(1))
                {
                    HandleRightClick(cam);
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] 更新放置模式失败: " + e.Message);
            }
        }

        /// <summary>
        /// 处理右键点击（两次确认删除：第一次描边选中，第二次删除）
        /// </summary>
        private void HandleRightClick(Camera cam)
        {
            if (cam == null) return;

            try
            {
                Vector3 mousePos = UnityEngine.Input.mousePosition;
                Ray ray = cam.ScreenPointToRay(mousePos);

                // 检测所有碰撞体
                RaycastHit hit;
                if (Physics.Raycast(ray, out hit, 500f, ~0, QueryTriggerInteraction.Ignore))
                {
                    // 找到命中对象的真正根对象
                    Transform hitTransform = hit.collider.transform;
                    GameObject targetObj = FindTrueRootObject(hitTransform);

                    if (targetObj != null)
                    {
                        // 检查是否点击的是同一个对象
                        if (pendingDeleteObject != null && pendingDeleteObject == targetObj)
                        {
                            // 第二次右键点击同一对象 - 执行删除
                            ConfirmDeleteObject(targetObj);
                        }
                        else
                        {
                            // 第一次右键点击或点击了不同对象 - 选中并描边
                            SelectObjectForDelete(targetObj);
                        }
                    }
                    else
                    {
                        // 点击空白处，取消选中
                        ClearPendingDelete();
                        DevLog("[BossRush] 放置模式：未找到可选中的对象");
                    }
                }
                else
                {
                    // 点击空白处，取消选中
                    ClearPendingDelete();
                    DevLog("[BossRush] 放置模式：右键未命中任何对象，已取消选中");
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] 右键处理失败: " + e.Message);
            }
        }

        /// <summary>
        /// 找到真正的根对象（向上查找直到没有父对象或父对象是场景根）
        /// </summary>
        private GameObject FindTrueRootObject(Transform hitTransform)
        {
            if (hitTransform == null) return null;

            Transform current = hitTransform;

            // 向上查找，直到找到场景根下的第一层对象
            while (current.parent != null)
            {
                string parentName = current.parent.name;
                // 如果父对象是场景根容器，当前对象就是我们要的根对象
                if (parentName == "Scene" || parentName.StartsWith("Scene_") ||
                    parentName.StartsWith("Level_") || parentName.StartsWith("Group_") ||
                    current.parent.parent == null)
                {
                    return current.gameObject;
                }
                current = current.parent;
            }

            // 如果没有父对象，返回自身
            return current.gameObject;
        }

        /// <summary>
        /// 选中对象并添加描边效果
        /// </summary>
        private void SelectObjectForDelete(GameObject targetObj)
        {
            // 先清除之前的选中
            ClearPendingDelete();

            pendingDeleteObject = targetObj;
            string objName = targetObj.name;
            string objPath = GetTransformPath(targetObj.transform);
            Vector3 objPos = targetObj.transform.position;

            DevLog("[BossRush] 放置模式选中：");
            DevLog("[BossRush]   对象名称: " + objName);
            DevLog("[BossRush]   对象路径: " + objPath);
            DevLog("[BossRush]   对象位置: " + objPos);
            DevLog("[BossRush]   再次右键此对象可删除，右键其他位置取消选中");

            // 创建描边材质（红色高亮）
            if (outlineMaterial == null)
            {
                Shader urpShader = Shader.Find("Universal Render Pipeline/Lit");
                if (urpShader != null)
                {
                    outlineMaterial = new Material(urpShader);
                    outlineMaterial.SetFloat("_Surface", 1); // Transparent
                    outlineMaterial.SetFloat("_Blend", 0);
                    outlineMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    outlineMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    outlineMaterial.SetFloat("_ZWrite", 0);
                    outlineMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    outlineMaterial.EnableKeyword("_EMISSION");
                    outlineMaterial.SetColor("_BaseColor", new Color(1f, 0.3f, 0.3f, 0.7f));
                    outlineMaterial.SetColor("_EmissionColor", new Color(1f, 0f, 0f, 1f));
                    outlineMaterial.renderQueue = 3100;
                }
                else
                {
                    outlineMaterial = new Material(Shader.Find("Standard"));
                    outlineMaterial.SetFloat("_Mode", 3);
                    outlineMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    outlineMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    outlineMaterial.SetInt("_ZWrite", 0);
                    outlineMaterial.EnableKeyword("_ALPHABLEND_ON");
                    outlineMaterial.EnableKeyword("_EMISSION");
                    outlineMaterial.color = new Color(1f, 0.3f, 0.3f, 0.7f);
                    outlineMaterial.SetColor("_EmissionColor", new Color(1f, 0f, 0f, 1f));
                    outlineMaterial.renderQueue = 3100;
                }
            }

            // 应用描边材质到所有渲染器
            Renderer[] renderers = targetObj.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;

                // 保存原始材质
                pendingDeleteOriginalMaterials[renderer] = renderer.sharedMaterials;

                // 创建混合材质数组（原始材质 + 描边材质叠加效果）
                Material[] newMats = new Material[renderer.sharedMaterials.Length];
                for (int i = 0; i < newMats.Length; i++)
                {
                    // 创建原始材质的副本并添加红色叠加
                    if (renderer.sharedMaterials[i] != null)
                    {
                        newMats[i] = new Material(renderer.sharedMaterials[i]);
                        // 尝试设置颜色叠加
                        if (newMats[i].HasProperty("_BaseColor"))
                        {
                            Color origColor = newMats[i].GetColor("_BaseColor");
                            newMats[i].SetColor("_BaseColor", new Color(
                                Mathf.Min(1f, origColor.r + 0.5f),
                                origColor.g * 0.5f,
                                origColor.b * 0.5f,
                                origColor.a
                            ));
                        }
                        else if (newMats[i].HasProperty("_Color"))
                        {
                            Color origColor = newMats[i].GetColor("_Color");
                            newMats[i].SetColor("_Color", new Color(
                                Mathf.Min(1f, origColor.r + 0.5f),
                                origColor.g * 0.5f,
                                origColor.b * 0.5f,
                                origColor.a
                            ));
                        }
                        // 添加自发光
                        if (newMats[i].HasProperty("_EmissionColor"))
                        {
                            newMats[i].EnableKeyword("_EMISSION");
                            newMats[i].SetColor("_EmissionColor", new Color(0.5f, 0f, 0f, 1f));
                        }
                    }
                    else
                    {
                        newMats[i] = outlineMaterial;
                    }
                }
                renderer.materials = newMats;
            }
        }

        /// <summary>
        /// 确认删除选中的对象
        /// </summary>
        private void ConfirmDeleteObject(GameObject targetObj)
        {
            string objName = targetObj.name;
            string objPath = GetTransformPath(targetObj.transform);
            Vector3 objPos = targetObj.transform.position;

            DevLog("[BossRush] 放置模式删除确认：");
            DevLog("[BossRush]   对象名称: " + objName);
            DevLog("[BossRush]   对象路径: " + objPath);
            DevLog("[BossRush]   对象位置: " + objPos);

            // 清除待删除状态
            pendingDeleteObject = null;
            pendingDeleteOriginalMaterials.Clear();

            // 销毁对象
            UnityEngine.Object.Destroy(targetObj);

            DevLog("[BossRush] 放置模式：已删除 " + objName + " 位于 " + objPos);
        }

        /// <summary>
        /// 清除待删除选中状态，恢复原始材质
        /// </summary>
        private void ClearPendingDelete()
        {
            if (pendingDeleteObject != null)
            {
                // 恢复原始材质
                foreach (var kvp in pendingDeleteOriginalMaterials)
                {
                    Renderer renderer = kvp.Key;
                    Material[] originalMats = kvp.Value;

                    if (renderer != null && originalMats != null)
                    {
                        renderer.sharedMaterials = originalMats;
                    }
                }

                DevLog("[BossRush] 放置模式：已取消选中 " + pendingDeleteObject.name);
                pendingDeleteObject = null;
            }
            pendingDeleteOriginalMaterials.Clear();
        }

        /// <summary>
        /// 确认放置（鼠标左键点击时调用）
        /// </summary>
        private void ConfirmPlacement()
        {
            if (!placementModeActive || placementPreviewObject == null || lastNearestGameObject == null)
            {
                return;
            }

            try
            {
                // 在预览位置创建真正的克隆
                Vector3 placePos = placementPreviewObject.transform.position;

                // 使用当前旋转角度
                Vector3 originalEuler = lastNearestGameObject.transform.eulerAngles;
                Quaternion placeRot = Quaternion.Euler(originalEuler.x, placementRotationY, originalEuler.z);

                GameObject clone = UnityEngine.Object.Instantiate(lastNearestGameObject);
                clone.name = lastNearestGameObject.name + "_Clone";
                clone.transform.position = placePos;
                clone.transform.rotation = placeRot;
                clone.transform.localScale = lastNearestGameObject.transform.localScale;

                DevLog("[BossRush] 放置模式：已在 " + placePos + " 放置 " + clone.name + "，旋转=" + placementRotationY.ToString("F0") + "°");

                // 不退出放置模式，允许继续放置
            }
            catch (Exception e)
            {
                DevLog("[BossRush] 确认放置失败: " + e.Message);
            }
        }

        /// <summary>
        /// 检查放置模式是否激活（供外部调用）
        /// </summary>
        public static bool IsPlacementModeActive()
        {
            return placementModeActive;
        }

        // ============================================================================
        // 帧率显示（DevMode）
        // ============================================================================

        // 帧率计算变量
        private float fpsUpdateInterval = 0.5f;  // 更新间隔（秒）
        private float fpsAccumulator = 0f;       // 帧时间累加器
        private int fpsFrameCount = 0;           // 帧计数
        private float currentFps = 0f;           // 当前帧率
        private float fpsTimeSinceLastUpdate = 0f;  // 距上次更新的时间
        private GUIStyle fpsLabelStyle = null;   // 帧率显示样式（缓存）

        /// <summary>
        /// 更新帧率计算（在 Update 中调用）
        /// </summary>
        private void UpdateFpsCounter()
        {
            if (!DevModeEnabled) return;

            fpsTimeSinceLastUpdate += Time.unscaledDeltaTime;
            fpsAccumulator += Time.unscaledDeltaTime;
            fpsFrameCount++;

            // 每隔 fpsUpdateInterval 秒更新一次帧率
            if (fpsTimeSinceLastUpdate >= fpsUpdateInterval)
            {
                currentFps = fpsFrameCount / fpsAccumulator;
                fpsFrameCount = 0;
                fpsAccumulator = 0f;
                fpsTimeSinceLastUpdate = 0f;
            }
        }

        /// <summary>
        /// 绘制帧率显示（在 OnGUI 中调用）
        /// </summary>
        private void DrawFpsCounter()
        {
            if (!DevModeEnabled) return;

            // 初始化样式（仅一次）
            if (fpsLabelStyle == null)
            {
                fpsLabelStyle = new GUIStyle(GUI.skin.label);
                fpsLabelStyle.fontSize = 16;
                fpsLabelStyle.fontStyle = FontStyle.Bold;
                fpsLabelStyle.normal.textColor = Color.white;
            }

            // 根据帧率设置颜色
            if (currentFps >= 60f)
            {
                fpsLabelStyle.normal.textColor = Color.green;
            }
            else if (currentFps >= 30f)
            {
                fpsLabelStyle.normal.textColor = Color.yellow;
            }
            else
            {
                fpsLabelStyle.normal.textColor = Color.red;
            }

            // 在屏幕左上角显示帧率
            string fpsText = "FPS: " + currentFps.ToString("F1");
            GUI.Label(new Rect(10, 10, 120, 30), fpsText, fpsLabelStyle);
        }

        // ============================================================================
        // 地图点击坐标输出（DevMode 专用）
        // ============================================================================
        // 在打开地图时，鼠标左键点击地图即可输出对应的世界坐标
        // 省去跑图按 F7 的麻烦，方便采集刷怪点坐标
        // ============================================================================

        /// <summary>
        /// 缓存的 MiniMapDisplay 反射字段（display 是 MiniMapView 的 private 字段）
        /// </summary>
        private static FieldInfo cachedDisplayField = null;

        /// <summary>
        /// 检测地图打开时的鼠标点击，输出对应世界坐标
        /// 仅在 DevModeEnabled = true 时生效
        /// </summary>
        internal void UpdateMapClickDebug()
        {
            if (!DevModeEnabled) return;

            // 仅在鼠标左键按下时处理
            if (!UnityEngine.Input.GetMouseButtonDown(0)) return;

            try
            {
                // 获取 MiniMapView 实例，检查地图是否打开
                var mapView = Duckov.MiniMaps.UI.MiniMapView.Instance;
                if (mapView == null || !mapView.open) return;

                // 通过反射获取 private 的 display 字段
                if (cachedDisplayField == null)
                {
                    cachedDisplayField = typeof(Duckov.MiniMaps.UI.MiniMapView).GetField(
                        "display",
                        BindingFlags.NonPublic | BindingFlags.Instance
                    );
                    if (cachedDisplayField == null)
                    {
                        DevLog("[地图坐标] 反射获取 display 字段失败");
                        return;
                    }
                }

                var display = cachedDisplayField.GetValue(mapView) as Duckov.MiniMaps.UI.MiniMapDisplay;
                if (display == null) return;

                // 将鼠标屏幕坐标转换为 display 的世界坐标（UI 空间）
                RectTransform displayRect = display.transform as RectTransform;
                if (displayRect == null) return;

                // 获取用于 UI 射线检测的相机
                Canvas canvas = mapView.GetComponentInParent<Canvas>();
                Camera uiCamera = null;
                if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                {
                    uiCamera = canvas.worldCamera;
                }

                // 屏幕坐标转 display 世界坐标
                if (!RectTransformUtility.ScreenPointToWorldPointInRectangle(
                    displayRect,
                    UnityEngine.Input.mousePosition,
                    uiCamera,
                    out Vector3 worldPoint))
                {
                    return;
                }

                // 调用原版的坐标转换方法：display 世界坐标 → 游戏世界坐标
                Vector3 worldPos;
                if (!display.TryConvertToWorldPosition(worldPoint, out worldPos))
                {
                    DevLog("[地图坐标] 坐标转换失败（可能当前场景无地图数据）");
                    return;
                }

                // 输出世界坐标，格式方便直接复制到代码中
                string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                DevLog("[地图坐标] 场景=" + sceneName +
                    " | 世界坐标=(" + worldPos.x.ToString("F2") + "f, " +
                    worldPos.y.ToString("F2") + "f, " +
                    worldPos.z.ToString("F2") + "f)" +
                    " | new Vector3(" + worldPos.x.ToString("F2") + "f, " +
                    worldPos.y.ToString("F2") + "f, " +
                    worldPos.z.ToString("F2") + "f)");
            }
            catch (Exception e)
            {
                DevLog("[地图坐标] 异常: " + e.Message);
            }
        }
    }
}
