using HarmonyLib;
using System.Reflection;
using UnityEngine;

namespace PliersPlus.Patches
{
    [HarmonyPatch(typeof(DisconnectTool))]
    [HarmonyPatch("OnPrefabInit")]
    public static class DisconnectToolPatch
    {
        public static void Postfix(DisconnectTool __instance)
        {
            // 1. 将模式改为框选（Box）
            var singleModeField = typeof(DisconnectTool).GetField("singleDisconnectMode",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (singleModeField != null)
            {
                singleModeField.SetValue(__instance, false);
            }

            // // 2. 取消距离限制（设置极大值，例如 9999）
            // var maxLengthField = typeof(DisconnectTool).GetField("lineModeMaxLength",
                // BindingFlags.NonPublic | BindingFlags.Instance);
            // if (maxLengthField != null)
            // {
                // maxLengthField.SetValue(__instance, 9999); // 即使 Line 模式不用，也改一下以防万一
            // }

            // // 3. 如果基类 DragTool 有 maxDragDistance 字段，也一并修改
            // var baseField = typeof(DragTool).GetField("maxDragDistance",
                // BindingFlags.NonPublic | BindingFlags.Instance);
            // if (baseField != null)
            // {
                // baseField.SetValue(__instance, 9999f);
            // }

            // 4. 确保可视化对象池容量足够（Box 模式会用多模式预制体，池大小已自动设为10，但我们可以扩充）
            // 如果需要更多可视化对象，可重新创建池，但一般10个够用
            // 不建议修改池，以免出现问题
        }
    }
}