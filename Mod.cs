using HarmonyLib;
using KMod;
using PeterHan.PLib.Actions;
using PeterHan.PLib.Core;
using PeterHan.PLib.Database;
using System.Reflection;
using UnityEngine;

namespace PliersPlus
{
    public class Mod : UserMod2
    {
        public static PAction ConnectAction { get; private set; }
        public static Sprite ConnectIconSprite { get; private set; }
        public static Sprite ConnectVisualizerSprite { get; private set; }

        private static bool spritesLoaded = false;

        // 在 Db 初始化完成后加载图标，此时 Assets.Sprites 已可用
        [HarmonyPatch(typeof(Db), nameof(Db.Initialize))]
        public static class Db_Initialize_Patch
        {
            public static void Postfix()
            {
                LoadSprites();
            }
        }

        // 在 Db 初始化完成后加载图标
        internal static void LoadSprites()
        {
            if (spritesLoaded) return;

            if (Assets.Sprites == null)
            {
                Debug.LogWarning("[PliersPlus] Assets.Sprites not ready yet, retrying next frame");
                GameScheduler.Instance.ScheduleNextFrame(
                    "PliersPlus_LoadSprites",
                    _ => LoadSprites(),
                    null);
                return;
            }

            spritesLoaded = true;

            var assembly = Assembly.GetExecutingAssembly();
            var resourcePrefix = $"{assembly.GetName().Name}.ModAssets.assets.";

            // 按钮图标（32x32）
            ConnectIconSprite = Utilities.CreateSpriteDxt5(
                assembly.GetManifestResourceStream(resourcePrefix + "image_wirecutter_button.dds"),
                32, 32
            );
            ConnectIconSprite.name = "ConnectIcon";
            if (Assets.Sprites.ContainsKey(ConnectIconSprite.name))
                Assets.Sprites.Remove(ConnectIconSprite.name);
            Assets.Sprites.Add(ConnectIconSprite.name, ConnectIconSprite);

            // 可视化图标（256x256）- 用于鼠标指针
            ConnectVisualizerSprite = Utilities.CreateSpriteDxt5(
                assembly.GetManifestResourceStream(resourcePrefix + "image_wirecutter_visualizer.dds"),
                256, 256
            );
            ConnectVisualizerSprite.name = "ConnectVisualizerIcon";
            if (Assets.Sprites.ContainsKey(ConnectVisualizerSprite.name))
                Assets.Sprites.Remove(ConnectVisualizerSprite.name);
            Assets.Sprites.Add(ConnectVisualizerSprite.name, ConnectVisualizerSprite);
        }

        public override void OnLoad(Harmony harmony)
        {
            base.OnLoad(harmony);
            PUtil.InitLibrary();

            // 创建自定义动作
            ConnectAction = new PActionManager().CreateAction(
                "PliersPlus.Connect",
                STRINGS.PLIERS_PLUS.ACTIONS.CONNECT_TOOL,
                new PKeyBinding(KKeyCode.C, Modifier.Shift)
            );

            // 注册本地化（如果有）
            new PLocalization().Register();
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