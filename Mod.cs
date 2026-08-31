using HarmonyLib;
using KMod;
using PeterHan.PLib.Actions;
using PeterHan.PLib.Core;
using PeterHan.PLib.Database;
using PeterHan.PLib.PatchManager;
using System.Reflection;
using UnityEngine;

namespace PliersPlus
{
    public class Mod : UserMod2
    {
        public static PAction ConnectAction { get; private set; }
        public static Sprite ConnectIconSprite { get; private set; }
        public static Sprite ConnectVisualizerSprite { get; private set; }

        // 在游戏早期（Db 初始化之前）加载图标
        [PLibMethod(RunAt.BeforeDbInit)]
        internal static void BeforeDbInit()
        {
            // 加载图标
            var assembly = Assembly.GetExecutingAssembly();
            ConnectIconSprite = Utilities.CreateSpriteDxt5(
                assembly.GetManifestResourceStream("PliersPlus.images.image_wirecutter_button.dds"),
                32, 32
            );
            ConnectIconSprite.name = "ConnectIcon";
            
            // 注册到全局 Sprite 集合
            if (Assets.Sprites.ContainsKey(ConnectIconSprite.name))
                Assets.Sprites.Remove(ConnectIconSprite.name);
            Assets.Sprites.Add(ConnectIconSprite.name, ConnectIconSprite);
        }

        public override void OnLoad(Harmony harmony)
        {
            base.OnLoad(harmony);
            PUtil.InitLibrary();

            // 创建自定义动作
            ConnectAction = new PActionManager().CreateAction(
                "PliersPlus.Connect",
                "Connect Tool",
                new PKeyBinding()
            );

            // 使用 PPatchManager 在早期注册补丁
            new PPatchManager(harmony).RegisterPatchClass(typeof(Mod));
            
            // 注册本地化（如果有）
            new PLocalization().Register();
            
            Debug.Log("[PliersPlus] Mod loaded with PLib.");
        }
    }

    // 辅助类：加载 .dds 文件
    public static class Utilities
    {
        public static Sprite CreateSpriteDxt5(System.IO.Stream inputStream, int width, int height)
        {
            if (inputStream == null) return null;
            
            byte[] buffer = new byte[inputStream.Length - 128];
            inputStream.Seek(128, System.IO.SeekOrigin.Current);
            inputStream.Read(buffer, 0, buffer.Length);

            Texture2D texture = new Texture2D(width, height, TextureFormat.DXT5, false);
            texture.LoadRawTextureData(buffer);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f));
        }
    }
}