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
        public static Sprite ConnectDragSprite { get; private set; }

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

            // 三个 dds 走同一条加载 + 注册流程：
            //   读嵌入式资源 → CreateSpriteDxt5 → 注册到 Assets.Sprites
            // 尺寸按各自的原图设置：
            //   - image_wirecutter_button.dds    32x32   工具菜单按钮
            //   - image_wirecutter_visualizer.dds 256x256 鼠标跟随图标
            //   - image_connect_drag.dds         200x200 拖拽预览（对齐原版 DisconnectVis 的 mask 尺寸）
            ConnectIconSprite = LoadAndRegisterSprite(
                assembly, resourcePrefix, "image_wirecutter_button.dds",
                32, 32, "ConnectIcon");

            ConnectVisualizerSprite = LoadAndRegisterSprite(
                assembly, resourcePrefix, "image_wirecutter_visualizer.dds",
                256, 256, "ConnectVisualizerIcon");

            ConnectDragSprite = LoadAndRegisterSprite(
                assembly, resourcePrefix, "image_connect_drag.dds",
                200, 200, "ConnectDragIcon");
        }

        /// <summary>
        /// 统一入口：读嵌入式 dds → 包成 Sprite → 注册到 Assets.Sprites。
        /// 名字相同的旧 sprite 会被先移除，避免重复注册。
        /// </summary>
        private static Sprite LoadAndRegisterSprite(
            Assembly assembly, string prefix, string fileName,
            int width, int height, string spriteName)
        {
            var stream = assembly.GetManifestResourceStream(prefix + fileName);
            if (stream == null)
            {
                Debug.LogWarning($"[PliersPlus] Embedded resource not found: {prefix}{fileName}");
                return null;
            }

            Sprite sprite;
            try
            {
                sprite = Utilities.CreateSpriteDxt5(stream, width, height);
            }
            finally
            {
                stream.Dispose();
            }

            if (sprite == null)
            {
                Debug.LogWarning($"[PliersPlus] CreateSpriteDxt5 returned null for {fileName}");
                return null;
            }

            sprite.name = spriteName;
            if (Assets.Sprites.ContainsKey(spriteName))
                Assets.Sprites.Remove(spriteName);
            Assets.Sprites.Add(spriteName, sprite);

            return sprite;
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