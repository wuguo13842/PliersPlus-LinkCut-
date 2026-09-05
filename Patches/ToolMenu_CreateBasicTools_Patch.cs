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

            // 描述中包含 {Hotkey} 占位符，游戏会自动替换为当前快捷键
            string descWithHotkey = STRINGS.PLIERS_PLUS.ACTIONS.CONNECT_DESC + " {Hotkey}";

            var connectCollection = ToolMenu.CreateToolCollection(
                STRINGS.PLIERS_PLUS.ACTIONS.CONNECT,
                "ConnectIcon",
                Mod.ConnectAction.GetKAction(),  // 传入 KAction 让游戏识别
                nameof(ConnectTool),
                descWithHotkey,
                false
            );

            __instance.basicTools.Add(connectCollection);
            Debug.Log("[PliersPlus] Connect tool added in CreateBasicTools.");
        }
    }
}