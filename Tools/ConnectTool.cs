using System;
using System.Collections.Generic;
using System.IO;
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

            // 0. 从 dds 头里读实际尺寸并加载（不再硬编码 64x64）
            Texture2D dragTexture = null;
            {
                var assembly = Assembly.GetExecutingAssembly();
                var stream = assembly.GetManifestResourceStream(
                    $"{assembly.GetName().Name}.ModAssets.assets.image_connect_drag.dds");
                if (stream != null)
                {
                    try
                    {
                        byte[] all = new byte[stream.Length];
                        int read = 0;
                        while (read < all.Length)
                        {
                            int n = stream.Read(all, read, all.Length - read);
                            if (n <= 0) break;
                            read += n;
                        }

                        // DDS 头：magic(4) + headerSize(4) + flags(4) + height(4) + width(4) + ...
                        // height 在偏移 12，width 在偏移 16（都是 uint32 小端）
                        uint height = BitConverter.ToUInt32(all, 12);
                        uint width = BitConverter.ToUInt32(all, 16);
                        Debug.Log($"[PliersPlus] dds 文件总长 {all.Length} 字节，header 里 width={width} height={height}");

                        // 检查是否带 DX10 扩展头（DDS magic 后面第一个 dword 若是 DX10 fourCC，则头是 148 字节）
                        // 简化判断：如果没有额外扩展，数据从偏移 128 开始
                        int dataOffset = 128;
                        uint fourCC = BitConverter.ToUInt32(all, 84);  // "DXT5" / "DX10" / etc
                        // 'DX10' = 0x30315844
                        if (fourCC == 0x30315844)
                        {
                            dataOffset = 148;
                            Debug.Log("[PliersPlus] 检测到 DX10 扩展头，数据从 148 开始");
                        }

                        int payloadLength = all.Length - dataOffset;
                        byte[] payload = new byte[payloadLength];
                        Array.Copy(all, dataOffset, payload, 0, payloadLength);

                        dragTexture = new Texture2D((int)width, (int)height, TextureFormat.DXT5, false);
                        dragTexture.LoadRawTextureData(payload);
                        dragTexture.Apply(false, true);
                        Debug.Log($"[PliersPlus] 贴图加载成功: {dragTexture.width}x{dragTexture.height} fmt={dragTexture.format}");
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[PliersPlus] 贴图加载失败: {e}");
                    }
                    finally
                    {
                        stream.Dispose();
                    }
                }
                if (dragTexture == null)
                    Debug.LogWarning("[PliersPlus] image_connect_drag.dds 未找到或加载失败");
            }

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
            GameObject visPrefab = null;
            var disconnectTool = DisconnectTool.Instance;
            if (disconnectTool == null)
            {
                var all = Resources.FindObjectsOfTypeAll<DisconnectTool>();
                if (all != null && all.Length > 0)
                    disconnectTool = all[0];
            }
            if (disconnectTool != null)
            {
                var singleField = typeof(DisconnectTool).GetField(
                    "disconnectVisSingleModePrefab", BindingFlags.NonPublic | BindingFlags.Instance);
                var multiField = typeof(DisconnectTool).GetField(
                    "disconnectVisMultiModePrefab", BindingFlags.NonPublic | BindingFlags.Instance);
                visPrefab = singleField?.GetValue(disconnectTool) as GameObject;
                if (visPrefab == null)
                    visPrefab = multiField?.GetValue(disconnectTool) as GameObject;
            }
            if (visPrefab == null)
                Debug.LogWarning("[PliersPlus] DisconnectVis prefab 未找到");

            connectVisPool = new GameObjectPool(
                () =>
                {
                    GameObject go;
                    if (visPrefab != null)
                    {
                        go = GameUtil.KInstantiate(visPrefab, Grid.SceneLayer.FXFront, null, 0);

                        if (dragTexture != null)
                        {
                            var mrs = go.GetComponentsInChildren<MeshRenderer>(true);
                            foreach (var mr in mrs)
                            {
                                var mat = mr.material;
                                if (mat == null) continue;
                                mat.SetTexture("_MainTex", dragTexture);
                                mat.SetColor("_Color", new Color32(0, 119, 145, 255));
                            }
                        }
                    }
                    else
                    {
                        go = new GameObject("ConnectVis");
                        go.layer = LayerMask.NameToLayer("Overlay");
                        var srFallback = go.AddComponent<SpriteRenderer>();
                        var tex = new Texture2D(1, 1);
                        tex.SetPixel(0, 0, Color.white);
                        tex.Apply();
                        srFallback.sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
                        go.transform.localScale = new Vector3(Grid.CellSizeInMeters, Grid.CellSizeInMeters, 1f);
                    }
                    go.SetActive(false);
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

                    var baseAreaField = typeof(DragTool).GetField("areaVisualizer", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (baseAreaField != null)
                        baseAreaField.SetValue(this, areaVisualizer);
                    var baseSrField = typeof(DragTool).GetField("areaVisualizerSpriteRenderer", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (baseSrField != null)
                        baseSrField.SetValue(this, sr2);
                }
            }

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
            RefreshRegionVisuals(downPos, upPos);
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
            Grid.CellToXY(nc, out int nx, out int ny);
            if (nx < min.x || nx >= max.x || ny < min.y || ny >= max.y) return false;
            if (!Grid.IsVisible(nc)) return false;
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
                if (ReferenceEquals(nm, srcMgr)) return true;
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
                    vis.UpdateConnections(newConnections);
                    vis.Refresh();
                    dirtyMgrs.Add(mgr);
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