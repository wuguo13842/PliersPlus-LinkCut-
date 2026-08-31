using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace PliersPlus.Tools
{
    public class ConnectTool : FilteredDragTool
    {
        public static ConnectTool Instance { get; private set; }
        private bool singleConnectMode = true;
        private new int lineModeMaxLength = 9999;
        private List<VisData> visualizersInUse = new List<VisData>();
        private GameObjectPool connectVisPool;
        private int lastRefreshedCell = -1;

        [SerializeField]
        private GameObject connectVisPrefab;

        public static void DestroyInstance()
        {
            Instance = null;
        }

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            Instance = this;

            // ========== 1. 初始化 visualizer（鼠标光标图标） ==========
            visualizer = new GameObject("ConnectVisualizer");
            visualizer.SetActive(false);

            GameObject offsetObject = new GameObject();
            SpriteRenderer spriteRenderer = offsetObject.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = Assets.GetSprite("icon_wirecutter_button");
            spriteRenderer.color = new Color32(0, 119, 145, 255);

            offsetObject.transform.SetParent(visualizer.transform);
            offsetObject.transform.localPosition = new Vector3(0, Grid.HalfCellSizeInMeters);
            var sprite = spriteRenderer.sprite;
            if (sprite != null)
            {
                offsetObject.transform.localScale = new Vector3(
                    Grid.CellSizeInMeters / (sprite.texture.width / sprite.pixelsPerUnit),
                    Grid.CellSizeInMeters / (sprite.texture.height / sprite.pixelsPerUnit)
                );
            }
            offsetObject.SetLayerRecursively(LayerMask.NameToLayer("Overlay"));
            visualizer.transform.SetParent(transform);

            // ========== 2. 初始化 areaVisualizer（框选区域） ==========
            GameObject areaVisualizer = new GameObject("ConnectAreaVisualizer");
            areaVisualizer.SetActive(false);
            areaVisualizer.transform.SetParent(transform);

            var renderer = areaVisualizer.AddComponent<SpriteRenderer>();
            Texture2D tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            renderer.sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
            renderer.color = new Color32(0, 119, 145, 255);
            renderer.material.color = new Color32(0, 119, 145, 255);
            areaVisualizer.layer = LayerMask.NameToLayer("Overlay");
            areaVisualizer.transform.localScale = new Vector3(Grid.CellSizeInMeters, Grid.CellSizeInMeters, 1);

            var areaField = typeof(DragTool).GetField("areaVisualizer", BindingFlags.NonPublic | BindingFlags.Instance);
            var srField = typeof(DragTool).GetField("areaVisualizerSpriteRenderer", BindingFlags.NonPublic | BindingFlags.Instance);
            if (areaField != null) areaField.SetValue(this, areaVisualizer);
            if (srField != null) srField.SetValue(this, renderer);

            // ========== 3. 创建可视化对象池（自定义实现，不依赖不存在的预制体） ==========
            connectVisPool = new GameObjectPool(
                () => {
                    GameObject go = new GameObject("ConnectVis");
                    go.SetActive(false);
                    var visRenderer = go.AddComponent<SpriteRenderer>();
                    // 使用剪刀图标作为标记
                    var icon = Assets.GetSprite("icon_wirecutter_button");
                    if (icon != null)
                        visRenderer.sprite = icon;
                    else
                    {
                        // 后备：创建空白纹理
                        Texture2D fallbackTex = new Texture2D(1, 1);
                        fallbackTex.SetPixel(0, 0, Color.white);
                        fallbackTex.Apply();
                        visRenderer.sprite = Sprite.Create(fallbackTex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
                    }
                    visRenderer.color = new Color32(0, 119, 145, 255);
                    visRenderer.material.color = new Color32(0, 119, 145, 255);
                    go.transform.localScale = new Vector3(Grid.CellSizeInMeters * 0.5f, Grid.CellSizeInMeters * 0.5f, 1);
                    go.layer = LayerMask.NameToLayer("FXFront");
                    return go;
                },
                null,
                1
            );

            // ========== 4. 设置单线模式 ==========
            var modeField = typeof(FilteredDragTool).GetField("singleDisconnectMode", BindingFlags.NonPublic | BindingFlags.Instance);
            if (modeField != null) modeField.SetValue(this, true);

            // ========== 5. 添加悬停卡片 ==========
            gameObject.AddComponent<ConnectToolHoverCard>();
        }

        protected override void OnActivateTool()
        {
            base.OnActivateTool();
            lastRefreshedCell = -1;

            // 确保 areaVisualizer 处于激活状态
            var areaVisualizer = typeof(DragTool).GetField("areaVisualizer", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(this) as GameObject;
            if (areaVisualizer != null && !areaVisualizer.activeSelf)
            {
                areaVisualizer.SetActive(true);
            }
        }

        protected override DragTool.Mode GetMode()
        {
            return DragTool.Mode.Line;
        }

        protected override void OnDragComplete(Vector3 downPos, Vector3 upPos)
        {
            upPos = SnapToLine(upPos);
            RunOnRegion(downPos, upPos, ConnectCellsAction);
            ClearVisualizers();
        }

        public override void OnMouseMove(Vector3 cursorPos)
        {
            base.OnMouseMove(cursorPos);
            if (!Dragging) return;
            cursorPos = ClampPositionToWorld(cursorPos, ClusterManager.Instance.activeWorld);
            cursorPos = SnapToLine(cursorPos);
            int cell = Grid.PosToCell(cursorPos);
            if (lastRefreshedCell == cell) return;
            lastRefreshedCell = cell;
            ClearVisualizers();
            RunOnRegion(downPos, cursorPos, VisualizeAction);
        }

        private void RunOnRegion(Vector3 pos1, Vector3 pos2, Action<int, GameObject, IHaveUtilityNetworkMgr, UtilityConnections> action)
        {
            Vector2 reg1 = GetRegularizedPos(Vector2.Min(pos1, pos2), true);
            Vector2 reg2 = GetRegularizedPos(Vector2.Max(pos1, pos2), false);
            Vector2I min = new Vector2I((int)reg1.x, (int)reg1.y);
            Vector2I max = new Vector2I((int)reg2.x, (int)reg2.y);

            for (int x = min.x; x < max.x; x++)
            {
                for (int y = min.y; y < max.y; y++)
                {
                    int cell = Grid.XYToCell(x, y);
                    if (!Grid.IsVisible(cell)) continue;
                    for (int layer = 0; layer < 45; layer++)
                    {
                        GameObject go = Grid.Objects[cell, layer];
                        if (go == null) continue;
                        string filterLayer = GetFilterLayerFromGameObject(go);
                        if (!IsActiveLayer(filterLayer)) continue;
                        Building building = go.GetComponent<Building>();
                        if (building == null) continue;
                        IHaveUtilityNetworkMgr networkMgr = building.Def.BuildingComplete.GetComponent<IHaveUtilityNetworkMgr>();
                        if (networkMgr.IsNullOrDestroyed()) continue;
                        UtilityConnections connections = networkMgr.GetNetworkManager().GetConnections(cell, false);
                        UtilityConnections toAdd = 0;
                        if ((connections & UtilityConnections.Left) == 0 && IsInsideRegion(min, max, cell, -1, 0))
                            toAdd |= UtilityConnections.Left;
                        if ((connections & UtilityConnections.Right) == 0 && IsInsideRegion(min, max, cell, 1, 0))
                            toAdd |= UtilityConnections.Right;
                        if ((connections & UtilityConnections.Up) == 0 && IsInsideRegion(min, max, cell, 0, 1))
                            toAdd |= UtilityConnections.Up;
                        if ((connections & UtilityConnections.Down) == 0 && IsInsideRegion(min, max, cell, 0, -1))
                            toAdd |= UtilityConnections.Down;
                        if (toAdd > 0)
                            action(cell, go, networkMgr, toAdd);
                    }
                }
            }
        }

        private bool IsInsideRegion(Vector2I min, Vector2I max, int cell, int xoff, int yoff)
        {
            Grid.CellToXY(Grid.OffsetCell(cell, xoff, yoff), out int x, out int y);
            return x >= min.x && x < max.x && y >= min.y && y < max.y;
        }

        private void ConnectCellsAction(int cell, GameObject objectOnCell, IHaveUtilityNetworkMgr utilityComponent, UtilityConnections addConnections)
        {
            KAnimGraphTileVisualizer vis = objectOnCell.GetComponent<KAnimGraphTileVisualizer>();
            if (vis != null)
            {
                UtilityConnections newConnections = utilityComponent.GetNetworkManager().GetConnections(cell, false) | addConnections;
                vis.UpdateConnections(newConnections);
                vis.Refresh();
            }
            Building component = objectOnCell.GetComponent<Building>();
            if (component != null)
                TileVisualizer.RefreshCell(cell, component.Def.TileLayer, component.Def.ReplacementLayer);
            utilityComponent.GetNetworkManager().ForceRebuildNetworks();
        }

        private void VisualizeAction(int cell, GameObject objectOnCell, IHaveUtilityNetworkMgr utilityComponent, UtilityConnections addConnections)
        {
            if ((addConnections & UtilityConnections.Down) != 0)
                CreateVisualizer(cell, Grid.CellBelow(cell), true);
            if ((addConnections & UtilityConnections.Right) != 0)
                CreateVisualizer(cell, Grid.CellRight(cell), false);
        }

        private void CreateVisualizer(int cell1, int cell2, bool rotate)
        {
            foreach (VisData vd in visualizersInUse)
                if (vd.Equals(cell1, cell2)) return;
            Vector3 a = Grid.CellToPosCCC(cell1, Grid.SceneLayer.FXFront);
            Vector3 b = Grid.CellToPosCCC(cell2, Grid.SceneLayer.FXFront);
            GameObject go = connectVisPool.GetInstance();
            go.transform.rotation = Quaternion.Euler(0, 0, rotate ? 90 : 0);
            go.transform.SetPosition(Vector3.Lerp(a, b, 0.5f));
            go.SetActive(true);
            visualizersInUse.Add(new VisData(cell1, cell2, go));
        }

        private void ClearVisualizers()
        {
            foreach (VisData vd in visualizersInUse)
            {
                vd.go.SetActive(false);
                connectVisPool.ReleaseInstance(vd.go);
            }
            visualizersInUse.Clear();
        }

        protected override void OnDeactivateTool(InterfaceTool new_tool)
        {
            base.OnDeactivateTool(new_tool);
            ClearVisualizers();
        }

        protected override string GetConfirmSound() => "OutletConnected";
        protected override string GetDragSound() => "Tile_Drag_PositiveTool";

        protected override void GetDefaultFilters(out ToolParameterMenu.ToggleData[] filters)
        {
            filters = new ToolParameterMenu.ToggleData[]
            {
                new ToolParameterMenu.ToggleData(ToolParameterMenu.FILTERLAYERS.ALL, ToolParameterMenu.ToggleState.On, false),
                new ToolParameterMenu.ToggleData(ToolParameterMenu.FILTERLAYERS.WIRES, ToolParameterMenu.ToggleState.Off, false),
                new ToolParameterMenu.ToggleData(ToolParameterMenu.FILTERLAYERS.LIQUIDCONDUIT, ToolParameterMenu.ToggleState.Off, false),
                new ToolParameterMenu.ToggleData(ToolParameterMenu.FILTERLAYERS.GASCONDUIT, ToolParameterMenu.ToggleState.Off, false),
                new ToolParameterMenu.ToggleData(ToolParameterMenu.FILTERLAYERS.SOLIDCONDUIT, ToolParameterMenu.ToggleState.Off, false),
                new ToolParameterMenu.ToggleData(ToolParameterMenu.FILTERLAYERS.BUILDINGS, ToolParameterMenu.ToggleState.Off, false),
                new ToolParameterMenu.ToggleData(ToolParameterMenu.FILTERLAYERS.LOGIC, ToolParameterMenu.ToggleState.Off, false)
            };
        }

        public struct VisData
        {
            public readonly int cell1;
            public readonly int cell2;
            public GameObject go;
            public VisData(int c1, int c2, GameObject g) { cell1 = c1; cell2 = c2; go = g; }
            public bool Equals(int c1, int c2) => (cell1 == c1 && cell2 == c2) || (cell1 == c2 && cell2 == c1);
        }
    }
}