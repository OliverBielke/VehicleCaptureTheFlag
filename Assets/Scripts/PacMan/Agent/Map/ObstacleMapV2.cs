using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Scripts.Map
{
    public class ObstacleMapV2
    {
        // So other scripts can easily find the active map
        public static ObstacleMapV2 Instance { get; private set; }
        
        private List<GameObject> obstacles;

        public Dictionary<Vector2Int, Traversability> traversabilityPerCell;
        public Dictionary<Vector2Int, List<GameObject>> gameGameObjectsPerCell;
        public List<GameObject> obstacleObjects;
        public BoundsInt localBounds;
        public BoundsInt cellBounds;

        private readonly Vector3 cellScale;
        public Vector3 trueScale;
        public float obstacleRadius = 0.45f;

        public List<string> layerNames = new() { "Obstacle" };
        private Transform map;
        private int layermask;
        public int inflationRadiusCells = 0;

        public static ObstacleMapV2 Initialize(MapManager map, List<GameObject> additionalObjects, Vector3 cellScale)
        {
            return Initialize(map, additionalObjects, cellScale, 0);
        }

        public static ObstacleMapV2 Initialize(MapManager map, List<GameObject> additionalObjects, Vector3 cellScale, int inflationRadiusCells = 0)
        {
            var obstacleObjects = map.GetObstacleObjects();
            obstacleObjects.AddRange(additionalObjects);

            var obstacleMap = new ObstacleMapV2(map, obstacleObjects, cellScale);
            obstacleMap.inflationRadiusCells = inflationRadiusCells;
            obstacleMap.GenerateMap();
            
            // Store this map as the global instance so our DebugManager can find it
            Instance = obstacleMap;
            
            return obstacleMap;
        }

        public ObstacleMapV2(MapManager map, List<GameObject> obstacleObjects, Vector3 cellScale)
        {
            this.map = map.transform;
            this.cellScale = cellScale;
            this.obstacleObjects = obstacleObjects;
        }

        public void GenerateMap()
        {
            trueScale = Vector3.Scale(cellScale, map.localScale);

            var mapBoundsHelper = EncapsulateGameObjects(obstacleObjects);
            
            mapBoundsHelper = InverseTransformBounds(map, mapBoundsHelper);
            
            var minToInt = Vector3Int.CeilToInt(new Vector3(mapBoundsHelper.min.x / trueScale.x, 0, mapBoundsHelper.min.z / trueScale.z));
            var maxToInt = Vector3Int.FloorToInt(new Vector3(mapBoundsHelper.max.x / trueScale.x, 1, mapBoundsHelper.max.z / trueScale.z));
            cellBounds = new BoundsInt(minToInt, maxToInt - minToInt);

            minToInt = Vector3Int.CeilToInt(new Vector3(mapBoundsHelper.min.x, 0, mapBoundsHelper.min.z));
            maxToInt = Vector3Int.FloorToInt(new Vector3(mapBoundsHelper.max.x, 1, mapBoundsHelper.max.z));
            localBounds = new BoundsInt(minToInt, maxToInt - minToInt);

            layermask = layerNames.Select(name => LayerMask.GetMask(name)).Sum();
            (gameGameObjectsPerCell, traversabilityPerCell) = GenerateMapData(obstacleObjects);
        }

        public Vector3Int WorldToCell(Vector3 worldPosition)
        {
            return Vector3Int.FloorToInt(Vector3.Scale(map.transform.InverseTransformPoint(worldPosition), new Vector3(1 / trueScale.x, 1 / trueScale.y, 1 / trueScale.z)));
        }

        public Vector3 CellToWorld(Vector3Int vector3Int)
        {
            return map.transform.TransformPoint(Vector3.Scale(vector3Int, trueScale));
        }

        public List<GameObject> GetObstaclesPerCell(Vector3Int cellPos)
        {
            var vector2Int = new Vector2Int(cellPos.x, cellPos.z);
            if (gameGameObjectsPerCell.ContainsKey(vector2Int)) return gameGameObjectsPerCell[vector2Int];
            return new List<GameObject>();
        }

        public Traversability GetGlobalPointTravesibility(Vector3 worldPosition)
        {
            return GetLocalPointTraversibility(Vector3.Scale(WorldToCell(worldPosition), trueScale)); // Need to undo scaling from WorldToCell, as it is also applied in LocalPointTraversable
        }

        public Traversability GetLocalPointTraversibility(Vector3 localPosition)
        {
            var cellPos = Vector3Int.FloorToInt(Vector3.Scale(localPosition, new Vector3(1 / trueScale.x, 1 / trueScale.y, 1 / trueScale.z)));
            var vector2Int = new Vector2Int(cellPos.x, cellPos.z);
            if (traversabilityPerCell.ContainsKey(vector2Int)) return traversabilityPerCell[vector2Int];
            return Traversability.Unmapped;
        }

        private (Dictionary<Vector2Int, List<GameObject>>, Dictionary<Vector2Int, Traversability>) GenerateMapData(List<GameObject> gameObjects)
        {
            var gameObjectsPerCell = new Dictionary<Vector2Int, List<GameObject>>();
            var traversabilityData = new Dictionary<Vector2Int, Traversability>();

            foreach (var pos in cellBounds.allPositionsWithin)
            {
                gameObjectsPerCell[new Vector2Int(pos.x, pos.z)] = new List<GameObject>();
                traversabilityData[new Vector2Int(pos.x, pos.z)] = Traversability.Free;
            }


            foreach (var gameObject in gameObjects)
            {
                var objectBounds = ConvertToMapBoundsIntWithCellMargin(InverseTransformBounds(map, gameObject.GetComponent<Renderer>().bounds), trueScale, obstacleRadius);

                foreach (var cellPosition in objectBounds.allPositionsWithin)
                {
                    var collider = gameObject.GetComponent<Collider>();

                    if (collider != null)
                    {
                        var obstructed = CheckObstruction(cellPosition);
                        if (obstructed)
                        {
                            var dictVector = new Vector2Int(cellPosition.x, cellPosition.z);
                            if (traversabilityData.ContainsKey(dictVector))
                            {
                                traversabilityData[dictVector] = Traversability.Blocked;
                                gameObjectsPerCell[dictVector].Add(gameObject);
                            }
                        }
                    }
                }
            }

            if (inflationRadiusCells > 0)
            {
                InflateBlockedCells(traversabilityData, inflationRadiusCells);
            }

            return (gameObjectsPerCell, traversabilityData);
        }

        public bool CheckObstruction(Vector3Int cellLocation)
        {
            Vector3 center = CellToWorld(cellLocation) + trueScale / 2;
            return Physics.CheckSphere(center, obstacleRadius, layermask);
        }


        private Bounds EncapsulateGameObjects(List<GameObject> gameObjects)
        {
            var mapBoundsHelper = gameObjects[0].GetComponent<Renderer>().bounds;
            foreach (GameObject renderer in gameObjects)
            {
                var rendererBounds = renderer.transform.GetComponent<Renderer>();
                if (rendererBounds != null)
                {
                    mapBoundsHelper.Encapsulate(rendererBounds.bounds);
                }
                else
                {
                    foreach (var componentsInChild in renderer.GetComponentsInChildren<Renderer>())
                    {
                        mapBoundsHelper.Encapsulate(componentsInChild.bounds);
                    }
                }
            }

            return mapBoundsHelper;
        }

        private Bounds InverseTransformBounds(Transform _transform, Bounds _localBounds)
        {
            var center = _transform.InverseTransformPoint(_localBounds.center);

            var extents = _localBounds.extents;
            var axisX = _transform.InverseTransformVector(extents.x, 0, 0);
            var axisY = _transform.InverseTransformVector(0, extents.y, 0);
            var axisZ = _transform.InverseTransformVector(0, 0, extents.z);

            extents.x = Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x);
            extents.y = Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y);
            extents.z = Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z);

            return new Bounds { center = center, extents = extents };
        }

        private BoundsInt ConvertToMapBoundsIntWithCellMargin(Bounds bounds, Vector3 cellSize, float radius)
        {
            int padX = Mathf.CeilToInt(radius / trueScale.x);
            int padZ = Mathf.CeilToInt(radius / trueScale.z);

            var minToInt = Vector3Int.FloorToInt(new Vector3(bounds.min.x / trueScale.x, 0, bounds.min.z / trueScale.z));
            var maxToInt = Vector3Int.CeilToInt(new Vector3(bounds.max.x / trueScale.x, 1, bounds.max.z / trueScale.z));

            minToInt.x -= padX;
            minToInt.z -= padZ;
            maxToInt.x += padX;
            maxToInt.z += padZ;

            return new BoundsInt(minToInt, maxToInt - minToInt);
        }

        private void InflateBlockedCells(Dictionary<Vector2Int, Traversability> traversabilityData, int radius)
        {
            var originalBlocked = traversabilityData
                .Where(kvp => kvp.Value == Traversability.Blocked)
                .Select(kvp => kvp.Key)
                .ToList();

            var toBlock = new HashSet<Vector2Int>();

            foreach (var cell in originalBlocked)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    for (int dz = -radius; dz <= radius; dz++)
                    {
                        var neighbor = new Vector2Int(cell.x + dx, cell.y + dz);

                        if (!traversabilityData.ContainsKey(neighbor))
                            continue;

                        // circular inflation; remove this if you want square inflation
                        if (dx * dx + dz * dz > radius * radius)
                            continue;

                        toBlock.Add(neighbor);
                    }
                }
            }

            foreach (var cell in toBlock)
            {
                traversabilityData[cell] = Traversability.Blocked;
            }
        }
        
        
        public enum Traversability
        {
            Free = 0,
            Blocked = 1,
            Unmapped = 2,
        }
        
        /// <summary>
        /// Draws the grid map in the Scene view using Unity Gizmos.
        /// </summary>
        public void DrawMapGizmos()
        {
            // Safety check to ensure the map has been generated
            if (traversabilityPerCell == null) return;

            foreach (var kvp in traversabilityPerCell)
            {
                // Set colors based on traversability state
                if (kvp.Value == Traversability.Blocked)
                {
                    Gizmos.color = new Color(1f, 0f, 0f, 0.4f); // Semi-transparent Red
                }
                else if (kvp.Value == Traversability.Free)
                {
                    Gizmos.color = new Color(0f, 1f, 0f, 0.1f); // Very faint Green
                }
                else
                {
                    continue; // Skip unmapped areas
                }

                // Reconstruct the 3D cell location from the 2D dictionary key
                // Note: The dictionary uses x and y, but your 3D grid maps 'y' to the 'z' axis
                Vector3Int cellLocation = new Vector3Int(kvp.Key.x, 0, kvp.Key.y);
        
                // Find the physical center of the cell
                Vector3 center = CellToWorld(cellLocation) + trueScale / 2f;

                // Draw the cube. We multiply the scale by 0.95f to leave a tiny gap 
                // between cells, making the grid pattern visible.
                Gizmos.DrawCube(center, trueScale * 0.95f);
            }
        }
    }
}