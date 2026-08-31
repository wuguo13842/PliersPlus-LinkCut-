using System.Collections.Generic;
using UnityEngine;
using static STRINGS.UI;

namespace PliersPlus.Tools
{
    public class ConnectToolHoverCard : HoverTextConfiguration
    {
        public ConnectToolHoverCard()
        {
            ToolName = "Connect Tool";
        }

        public override void UpdateHoverElements(List<KSelectable> hoveredObjects)
        {
            HoverTextDrawer drawer = HoverTextScreen.Instance.BeginDrawing();
            drawer.BeginShadowBar();
            DrawTitle(HoverTextScreen.Instance, drawer);
            drawer.NewLine();
            drawer.DrawIcon(HoverTextScreen.Instance.GetSprite("icon_mouse_left"), 20);
            drawer.DrawText("Connect", Styles_Instruction.Standard);
            drawer.AddIndent(8);
            drawer.DrawIcon(HoverTextScreen.Instance.GetSprite("icon_mouse_right"), 20);
            drawer.DrawText("Cancel", Styles_Instruction.Standard);
            drawer.EndShadowBar();
            drawer.EndDrawing();
        }
    }
}