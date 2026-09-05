using HarmonyLib;
using PliersPlus.Tools;
using STRINGS;
using UnityEngine;

namespace PliersPlus.Patches
{
    [HarmonyPatch(typeof(ToolMenu))]
    [HarmonyPatch("CreateBasicTools")]
    public static class ToolMenu_CreateBasicTools_Patch
    {
        public static void Postfix(ToolMenu __instance)
        {
            foreach (var collection in __instance.basicTools)
            {
                if (collection.text == "Connect")
                {
                    Debug.Log("[PliersPlus] Connect tool already exists.");
                    return;
                }
            }

            var connectCollection = ToolMenu.CreateToolCollection(
                STRINGS.PLIERS_PLUS.ACTIONS.CONNECT,
                "ConnectIcon",
                Mod.ConnectAction.GetKAction(),  // 使用 PLib 创建的有效动作
                nameof(ConnectTool),
                STRINGS.PLIERS_PLUS.ACTIONS.CONNECT_DESC,
                false
            );

            __instance.basicTools.Add(connectCollection);
            Debug.Log("[PliersPlus] Connect tool added in CreateBasicTools.");
        }
    }
}