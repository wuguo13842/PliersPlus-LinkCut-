using HarmonyLib;
using KMod;
using PeterHan.PLib.Actions;
using PeterHan.PLib.Core;
using PeterHan.PLib.PatchManager;
using UnityEngine;

namespace PliersPlus
{
    public class Mod : UserMod2
    {
        public static PAction ConnectAction { get; private set; }

        public override void OnLoad(Harmony harmony)
        {
            base.OnLoad(harmony);
            PUtil.InitLibrary();

            // 创建自定义动作（不绑定任何快捷键）
            ConnectAction = new PActionManager().CreateAction(
                "PliersPlus.Connect",
                "Connect Tool",
                new PKeyBinding()
            );

            // 使用 PPatchManager 在早期注册补丁
            new PPatchManager(harmony).RegisterPatchClass(typeof(Mod));
            Debug.Log("[PliersPlus] Mod loaded with PLib.");
        }
    }
}