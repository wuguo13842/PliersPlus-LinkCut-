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
        private int lineModeMaxLength = 9999;
        private List<VisData> visualizersInUse = new List<VisData>();
        private GameObjectPool connectVisPool;
        private int lastRefreshedCell = -1;
		private static readonly int LAYER_COUNT = (int)ObjectLayer.NumLayers;

        [SerializeField]
        private GameObject connectVisPrefab;

        public static void DestroyInstance() => Instance = null;

protected override void OnPrefabInit()
{
    base.OnPrefabInit();
    Instance = this;

    // 1. visualizer（鼠标图标）
    visualizer = new GameObject("ConnectVisualizer");
    visualizer.SetActive(false);
    var offset = new GameObject();
    var sr = offset.AddComponent<SpriteRenderer>();
    var icon = Assets.GetSprite("ConnectVisualizerIcon");
    if (icon != null)
        sr.sprite = icon;
    else
        sr.sprite = CreateFallbackSprite();
    sr.color = new Color32(0, 119, 145, 255);
    offset.transform.SetParent(visualizer.transform);
    offset.transform.localPosition = new Vector3(0, Grid.HalfCellSizeInMeters);
    if (sr.sprite != null)
    {
        var s = sr.sprite;
        offset.transform.localScale = new Vector3(
            Grid.CellSizeInMeters / (s.texture.width / s.pixelsPerUnit),
            Grid.CellSizeInMeters / (s.texture.height / s.pixelsPerUnit)
        );
    }
    offset.SetLayerRecursively(LayerMask.NameToLayer("Overlay"));
    visualizer.transform.SetParent(transform);

    // 2. GameObjectPool
    connectVisPool = new GameObjectPool(
        () =>
        {
            var prefab = Assets.GetPrefab("DisconnectVisSingleLine");
            if (prefab == null)
                prefab = Assets.GetPrefab("DisconnectVis");
            GameObject go;
            if (prefab != null)
                go = GameUtil.KInstantiate(prefab, Grid.SceneLayer.FXFront, null, 0);
            else
                go = new GameObject("ConnectVis");
            go.SetActive(false);
            var rend = go.GetComponent<SpriteRenderer>();
            if (rend != null)
                rend.color = new Color32(0, 180, 0, 255);
            return go;
        },
        null,
        1
    );

    // 3. areaVisualizer 初始化（通过反射从 DeconstructTool 获取预制体）
    var deconstructTool = DeconstructTool.Instance;
    if (deconstructTool != null)
    {
        // 获取 DeconstructTool 的私有 areaVisualizer 字段
        var areaField = typeof(DeconstructTool).GetField("areaVisualizer", BindingFlags.NonPublic | BindingFlags.Instance);
        if (areaField == null)
            areaField = typeof(DragTool).GetField("areaVisualizer", BindingFlags.NonPublic | BindingFlags.Instance);
        var areaPrefab = areaField?.GetValue(deconstructTool) as GameObject;
        if (areaPrefab != null)
        {
            GameObject areaVisualizer = Util.KInstantiate(areaPrefab, null);
            areaVisualizer.SetActive(false);
            areaVisualizer.name = "ConnectAreaVisualizer";
            var sr2 = areaVisualizer.GetComponent<SpriteRenderer>();
            if (sr2 != null)
            {
                sr2.color = new Color32(0, 119, 145, 255);
                sr2.material.color = new Color32(0, 119, 145, 255);
            }
            areaVisualizer.transform.SetParent(transform);

            // 通过反射设置基类 DragTool 的私有字段
            var baseAreaField = typeof(DragTool).GetField("areaVisualizer", BindingFlags.NonPublic | BindingFlags.Instance);
            if (baseAreaField != null)
                baseAreaField.SetValue(this, areaVisualizer);
            var baseSrField = typeof(DragTool).GetField("areaVisualizerSpriteRenderer", BindingFlags.NonPublic | BindingFlags.Instance);
            if (baseSrField != null)
                baseSrField.SetValue(this, sr2);
        }
    }

    // 4. 设置单线模式
    var modeField = typeof(FilteredDragTool).GetField("singleDisconnectMode", BindingFlags.NonPublic | BindingFlags.Instance);
    if (modeField != null)
        modeField.SetValue(this, true);

    // 5. 添加悬停卡片
    gameObject.AddComponent<ConnectToolHoverCard>();
}

        protected override void OnActivateTool()
        {
            base.OnActivateTool();
            lastRefreshedCell = -1;
        }

        protected override DragTool.Mode GetMode() => DragTool.Mode.Line;

        protected override void OnDragComplete(Vector3 downPos, Vector3 upPos)
        {
            if (singleConnectMode)
                upPos = SnapToLine(upPos);
            RunOnRegion(downPos, upPos, ConnectCellsAction);
            ClearVisualizers();
        }

        public override void OnMouseMove(Vector3 cursorPos)
        {
            base.OnMouseMove(cursorPos);
            if (!Dragging) return;
            cursorPos = ClampPositionToWorld(cursorPos, ClusterManager.Instance.activeWorld);
            if (singleConnectMode)
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
                    for (int layer = 0; layer < LAYER_COUNT; layer++)
                    {
                        GameObject go = Grid.Objects[cell, layer];
                        if (go == null) continue;
                        if (!IsActiveLayer(GetFilterLayerFromGameObject(go))) continue;
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
                        if (toAdd != 0)
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
            var vis = objectOnCell.GetComponent<KAnimGraphTileVisualizer>();
            if (vis != null)
            {
                var mgr = utilityComponent.GetNetworkManager();
                if (mgr != null)
                {
                    UtilityConnections newConnections = mgr.GetConnections(cell, false) | addConnections;
                    vis.UpdateConnections(newConnections);
                    vis.Refresh();
                    mgr.ForceRebuildNetworks();
                }
            }
            var building = objectOnCell.GetComponent<Building>();
            if (building != null)
                TileVisualizer.RefreshCell(cell, building.Def.TileLayer, building.Def.ReplacementLayer);
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
            foreach (var vd in visualizersInUse)
                if (vd.Equals(cell1, cell2)) return;
            Vector3 a = Grid.CellToPosCCC(cell1, Grid.SceneLayer.FXFront);
            Vector3 b = Grid.CellToPosCCC(cell2, Grid.SceneLayer.FXFront);
            GameObject go = connectVisPool.GetInstance();
            if (go == null) return;
            go.transform.rotation = Quaternion.Euler(0, 0, rotate ? 90 : 0);
            go.transform.SetPosition(Vector3.Lerp(a, b, 0.5f));
            go.SetActive(true);
            visualizersInUse.Add(new VisData(cell1, cell2, go));
        }

        private void ClearVisualizers()
        {
            foreach (var vd in visualizersInUse)
            {
                if (vd.go != null)
                {
                    vd.go.SetActive(false);
                    connectVisPool.ReleaseInstance(vd.go);
                }
            }
            visualizersInUse.Clear();
        }

        protected override void OnDeactivateTool(InterfaceTool new_tool)
        {
            base.OnDeactivateTool(new_tool);
            ClearVisualizers();
        }

        protected override string GetConfirmSound() => "OutletConnected";
        protected override string GetDragSound() => "Tile_Drag_NegativeTool";

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

        private Sprite CreateFallbackSprite()
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
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