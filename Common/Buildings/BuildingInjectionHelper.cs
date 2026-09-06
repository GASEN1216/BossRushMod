// 共享官方建筑反射绑定。由婚礼注入器原样抽取，包含失败结果的一次解析缓存语义不变。
// 新建筑模块直接调用这里；旧 ModBehaviour 建筑入口只保留转发，不持有第二份缓存。
using System;
using System.Reflection;
using UnityEngine;

namespace BossRush
{
    internal static class BuildingInjectionHelper
    {
        /// <summary>BuildingManager 类型缓存</summary>
        private static Type cachedBuildingManagerType = null;
        private static bool buildingManagerTypeResolved = false;

        /// <summary>BuildingManager.Any 方法缓存</summary>
        private static MethodInfo cachedBuildingManagerAnyMethod = null;
        private static bool buildingManagerAnyMethodResolved = false;

        /// <summary>BuildingManager.GetBuildingData 方法缓存</summary>
        private static MethodInfo cachedGetBuildingDataMethod = null;
        private static bool getBuildingDataMethodResolved = false;

        /// <summary>Building 类型缓存</summary>
        private static Type cachedBuildingType = null;
        private static bool buildingTypeResolved = false;

        /// <summary>Building.ID 属性缓存</summary>
        private static PropertyInfo cachedBuildingIdProperty = null;
        private static bool buildingIdPropertyResolved = false;

        internal static Type FindGameType(string fullTypeName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type t = asm.GetType(fullTypeName);
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }

        internal static Type GetBuildingManagerType()
        {
            if (!buildingManagerTypeResolved)
            {
                cachedBuildingManagerType = FindGameType("Duckov.Buildings.BuildingManager");
                buildingManagerTypeResolved = true;
            }

            return cachedBuildingManagerType;
        }

        internal static MethodInfo GetBuildingManagerAnyMethod()
        {
            if (!buildingManagerAnyMethodResolved)
            {
                Type buildingManagerType = GetBuildingManagerType();
                if (buildingManagerType != null)
                {
                    cachedBuildingManagerAnyMethod = buildingManagerType.GetMethod(
                        "Any",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        new Type[] { typeof(string), typeof(bool) },
                        null);
                }

                buildingManagerAnyMethodResolved = true;
            }

            return cachedBuildingManagerAnyMethod;
        }

        internal static MethodInfo GetBuildingDataMethod()
        {
            if (!getBuildingDataMethodResolved)
            {
                Type buildingManagerType = GetBuildingManagerType();
                if (buildingManagerType != null)
                {
                    cachedGetBuildingDataMethod = buildingManagerType.GetMethod(
                        "GetBuildingData",
                        BindingFlags.NonPublic | BindingFlags.Static);
                }

                getBuildingDataMethodResolved = true;
            }

            return cachedGetBuildingDataMethod;
        }

        internal static Type GetBuildingType()
        {
            if (!buildingTypeResolved)
            {
                cachedBuildingType = FindGameType("Duckov.Buildings.Building");
                buildingTypeResolved = true;
            }

            return cachedBuildingType;
        }

        internal static PropertyInfo GetBuildingIdProperty()
        {
            if (!buildingIdPropertyResolved)
            {
                Type buildingType = GetBuildingType();
                if (buildingType != null)
                {
                    cachedBuildingIdProperty = buildingType.GetProperty("ID");
                }

                buildingIdPropertyResolved = true;
            }

            return cachedBuildingIdProperty;
        }

        internal static void AssignBuildingContainerField(FieldInfo field, Component buildingComp, Transform container)
        {
            if (field == null || buildingComp == null)
            {
                return;
            }

            if (container == null)
            {
                field.SetValue(buildingComp, null);
                return;
            }

            Type fieldType = field.FieldType;
            if (typeof(Transform).IsAssignableFrom(fieldType))
            {
                field.SetValue(buildingComp, container);
            }
            else if (typeof(GameObject).IsAssignableFrom(fieldType))
            {
                field.SetValue(buildingComp, container.gameObject);
            }
            else if (typeof(Component).IsAssignableFrom(fieldType))
            {
                field.SetValue(buildingComp, container.GetComponent(fieldType));
            }
            else
            {
                field.SetValue(buildingComp, container);
            }
        }
    }
}
