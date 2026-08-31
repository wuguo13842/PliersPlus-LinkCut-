using HarmonyLib;
using PliersPlus.Tools;
using System.Collections.Generic;
using UnityEngine;

namespace PliersPlus.Patches
{
[HarmonyPatch(typeof(PlayerController), "OnPrefabInit")]
public static class PlayerController_OnPrefabInit_Patch
{
    public static void Postfix(PlayerController __instance)
    {
        var connectToolGO = new GameObject("ConnectTool", typeof(ConnectTool));
        connectToolGO.transform.SetParent(__instance.transform);
        connectToolGO.SetActive(true);
        connectToolGO.SetActive(false);
        var tools = new List<InterfaceTool>(__instance.tools);
        tools.Add(connectToolGO.GetComponent<ConnectTool>());
        __instance.tools = tools.ToArray();
    }
}
}