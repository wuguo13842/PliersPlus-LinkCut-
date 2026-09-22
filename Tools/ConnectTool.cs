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
        private List<VisData> visualizersInUse = new List<VisData>();
        private GameObjectPool connectVisPool;
        private int lastRefreshedCell = -1;
        private HashSet<IUtilityNetworkMgr> dirtyMgrs = new HashSet<IUtilityNetworkMgr>();
		private static readonly int LAYER_COUNT = (int)ObjectLayer.NumLayers;

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

    // 4. 添加悬停卡片
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
            // 所有 SetConnections 完成后统一刷新一次区域内所有可视器。
            // 原因：SetConnections 内部对 physicalGrid 做邻居掩码（GetNeighboursAsConnections），
            // 若处理顺序靠后的 cell 在 Reconnect 时回填了前一个 cell 的 physicalGrid，
            // 前一个 cell 早已 Refresh 过，动画会停在错误状态直到下一次全局刷新。
            RefreshRegionVisuals(downPos, upPos);
            // 本次拖拽涉及的所有 network manager 统一标记为 dirty，
            // 避免在 ConnectCellsAction 里逐格调用（ForceRebuildNetworks 只是置 dirty = true，重复调用无意义）。
            foreach (var m in dirtyMgrs) m.ForceRebuildNetworks();
            dirtyMgrs.Clear();
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
                        // 只有邻居格确实存在同类型网络建筑时才加连接位，
                        // 否则 visualGrid 会被写入幽灵位（SetConnections 对 visualGrid 不做邻居掩码）。
                        if ((connections & UtilityConnections.Left) == 0 && IsConnectableNeighbour(min, max, cell, -1, 0, networkMgr))
                            toAdd |= UtilityConnections.Left;
                        if ((connections & UtilityConnections.Right) == 0 && IsConnectableNeighbour(min, max, cell, 1, 0, networkMgr))
                            toAdd |= UtilityConnections.Right;
                        if ((connections & UtilityConnections.Up) == 0 && IsConnectableNeighbour(min, max, cell, 0, 1, networkMgr))
                            toAdd |= UtilityConnections.Up;
                        if ((connections & UtilityConnections.Down) == 0 && IsConnectableNeighbour(min, max, cell, 0, -1, networkMgr))
                            toAdd |= UtilityConnections.Down;
                        if (toAdd != 0)
                            action(cell, go, networkMgr, toAdd);
                    }
                }
            }
        }

        private bool IsConnectableNeighbour(Vector2I min, Vector2I max, int cell, int xoff, int yoff,
                                            IHaveUtilityNetworkMgr srcComponent)
        {
            int nc = Grid.OffsetCell(cell, xoff, yoff);
            if (!Grid.IsValidCell(nc)) return false;

            // 1. 必须在框选矩形内
            Grid.CellToXY(nc, out int nx, out int ny);
            if (nx < min.x || nx >= max.x || ny < min.y || ny >= max.y) return false;

            // 2. 必须可见
            if (!Grid.IsVisible(nc)) return false;

            // 3. 邻居格上必须存在同类型的网络建筑（电线对电线、管道对管道）
            var srcMgr = srcComponent.GetNetworkManager();
            if (srcMgr == null) return false;

            for (int layer = 0; layer < LAYER_COUNT; layer++)
            {
                GameObject ngo = Grid.Objects[nc, layer];
                if (ngo == null) continue;
                Building b = ngo.GetComponent<Building>();
                if (b == null || b.Def == null || b.Def.BuildingComplete == null) continue;
                var n = b.Def.BuildingComplete.GetComponent<IHaveUtilityNetworkMgr>();
                if (n.IsNullOrDestroyed()) continue;
                var nm = n.GetNetworkManager();
                if (nm == null) continue;
                // 同类型网络管理器（WireNetworkManager 对 WireNetworkManager 等）
                if (nm.GetType() == srcMgr.GetType()) return true;
            }
            return false;
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
                    // KAnimGraphTileVisualizer.UpdateConnections 内部会调用
                    //   connectionManager.SetConnections(new_connections, cell, isPhysicalBuilding)
                    // 这一步才是真正把连接写进 UtilityNetworkManager（包括 physicalGrid 掩码 + Reconnect 回填邻居）。
                    vis.UpdateConnections(newConnections);
                    vis.Refresh();
                    // 收集本次涉及的所有 network manager，OnDragComplete 末尾统一 ForceRebuildNetworks。
                    dirtyMgrs.Add(mgr);
                }
            }
            var building = objectOnCell.GetComponent<Building>();
            if (building != null)
                TileVisualizer.RefreshCell(cell, building.Def.TileLayer, building.Def.ReplacementLayer);
        }

        private void VisualizeAction(int cell, GameObject objectOnCell, IHaveUtilityNetworkMgr utilityComponent, UtilityConnections addConnections)
        {
            // 四个方向都要画：ConnectTool 是新增连接，addConnections 可能包含任意方向位。
            if ((addConnections & UtilityConnections.Down) != 0)
                CreateVisualizer(cell, Grid.CellBelow(cell), true);
            if ((addConnections & UtilityConnections.Up) != 0)
                CreateVisualizer(cell, Grid.CellAbove(cell), true);
            if ((addConnections & UtilityConnections.Right) != 0)
                CreateVisualizer(cell, Grid.CellRight(cell), false);
            if ((addConnections & UtilityConnections.Left) != 0)
                CreateVisualizer(cell, Grid.CellLeft(cell), false);
        }

        // 在 OnDragComplete 末尾统一调用，重刷区域内所有 KAnimGraphTileVisualizer。
        // 原因见 OnDragComplete 注释：SetConnections 的邻居掩码 + Reconnect 回填有时间差，
        // 每个 cell 各自 Refresh 会读到中间状态。
        private void RefreshRegionVisuals(Vector3 pos1, Vector3 pos2)
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
                        var vis = go.GetComponent<KAnimGraphTileVisualizer>();
                        if (vis != null)
                        {
                            // Refresh 内部从 connectionManager.GetConnections 重新拉最新状态，
                            // 此刻 Reconnect 已把所有邻居的 physicalGrid 补齐，读到的是最终值。
                            vis.Refresh();
                        }
                    }
                }
            }
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