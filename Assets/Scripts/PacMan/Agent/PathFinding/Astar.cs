using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Vector3 = UnityEngine.Vector3;
using Scripts.Map;
using PacMan.Agent.Debugging;

namespace PacMan.Agent.PathFinding
{
    public class Astar
    {
        private readonly ObstacleMapV2 _obstacleMap;
        private readonly List<Vector3> _astarExploredNodes = new();
        private readonly HashSet<Vector2Int> _dynamicBlockedCells;
        private readonly System.Func<Vector3, bool> _additionalTraversability;
        private readonly int _voronoiCellScale;
        private readonly float _dangerPenaltyMultiplier;
        private Dictionary<Vector2Int, VoronoiCellData> _voronoiMap;
        
        /// <summary>
        /// Creates an A* planner with optional dynamic obstacles, territory constraints, and coarse Voronoi lookup scaling.
        /// </summary>
        /// <param name="obstacleMap">Grid used for A* traversal and world/cell conversion.</param>
        /// <param name="dynamicBlockedPositions">Optional runtime positions to treat as blocked cells.</param>
        /// <param name="additionalTraversability">Optional extra traversability rule evaluated in world space.</param>
        /// <param name="voronoiCellScale">How many A* cells map to one Voronoi cell per axis.</param>
        public Astar(
            ObstacleMapV2 obstacleMap,
            IEnumerable<Vector3> dynamicBlockedPositions = null,
            System.Func<Vector3, bool> additionalTraversability = null,
            int voronoiCellScale = 1,
            float dangerPenaltyMultiplier = 12f)
        {
            _obstacleMap = obstacleMap;
            _dynamicBlockedCells = new HashSet<Vector2Int>();
            _additionalTraversability = additionalTraversability;
            _voronoiCellScale = Mathf.Max(1, voronoiCellScale);
            _dangerPenaltyMultiplier = Mathf.Max(1f, dangerPenaltyMultiplier);

            if (dynamicBlockedPositions == null || _obstacleMap == null)
                return;

            foreach (var position in dynamicBlockedPositions)
            {
                var cell = ToCellKey(position);
                _dynamicBlockedCells.Add(cell);
            }
        }

        /// <summary>
        /// Run the A* algorithm and optionally apply Voronoi danger costs.
        /// </summary>
        /// <param name="start">World-space start position.</param>
        /// <param name="goal">World-space goal position.</param>
        /// <param name="voronoiMap">Optional coarse Voronoi danger map used to bias path cost.</param>
        /// <returns>The planned path in world-space, or null if no path could be found.</returns>
        public List<Vector3> PlanPathAStar(Vector3 start, Vector3 goal, 
            Dictionary<Vector2Int, VoronoiCellData> voronoiMap = null)
        {
            _voronoiMap = voronoiMap;
            
            // Convert world positions to map grid cells
            var startCell3D = _obstacleMap.WorldToCell(start);
            var goalCell3D = _obstacleMap.WorldToCell(goal);
            
            var startCell = new Vector2Int(startCell3D.x, startCell3D.z);
            var goalCell = new Vector2Int(goalCell3D.x, goalCell3D.z);
            var originalStartCell = startCell;
            var originalGoalCell = goalCell;

            startCell = FindNearestFreeCell(startCell);
            goalCell = FindNearestFreeCell(goalCell);
            // Get true world positions for precise debug drawing
            var startWorld = _obstacleMap.CellToWorld(new Vector3Int(startCell.x, 0, startCell.y));
            var goalWorld = _obstacleMap.CellToWorld(new Vector3Int(goalCell.x, 0, goalCell.y));

            // Mark the start and goal positions with an X
            if (DebugManager.Instance != null && DebugManager.Instance.aStar)
            {
                var markerSize = 0.3f;
                Debug.DrawLine(startWorld + new Vector3(-markerSize, 0, -markerSize), startWorld + new Vector3(markerSize, 0, markerSize), Color.yellow, 3f);
                Debug.DrawLine(startWorld + new Vector3(-markerSize, 0, markerSize), startWorld + new Vector3(markerSize, 0, -markerSize), Color.yellow, 3f);

                Debug.DrawLine(goalWorld + new Vector3(-markerSize, 0, -markerSize), goalWorld + new Vector3(markerSize, 0, markerSize), Color.red, 3f);
                Debug.DrawLine(goalWorld + new Vector3(-markerSize, 0, markerSize), goalWorld + new Vector3(markerSize, 0, -markerSize), Color.red, 3f);
            }
            
            if (!IsTraversableAStar(goalCell))
            {
                Debug.LogError(
                    $"A* goal {goalCell} is not traversable. Not even surrounding nodes. Can't plan path. " +
                    $"startCell={originalStartCell}->{startCell} goalCell={originalGoalCell}->{goalCell} " +
                    $"dynamicBlockedCells={_dynamicBlockedCells.Count} hasExtraConstraint={_additionalTraversability != null}");
                return null;
            }
            
            List<AStarNode> openSet = new();
            HashSet<Vector2Int> closedSet = new();

            // Pass the map instance so the node can check precomputed distances
            var startNode = new AStarNode(pos: startCell, goal: goalCell, obstacleMap: _obstacleMap, 
                voronoiMap: _voronoiMap, voronoiCellScale: _voronoiCellScale,
                dangerPenaltyMultiplier: _dangerPenaltyMultiplier, parent: null);
            openSet.Add(startNode);
            
            const int maxIterations = 50000;
            var iter = 0;

            while (openSet.Count > 0 && iter < maxIterations)
            {
                iter++;
                
                var currentNode = openSet.OrderBy(n => n.FCost).First();
                openSet.Remove(currentNode);
                closedSet.Add(currentNode.Position);
                
                _astarExploredNodes.Add(_obstacleMap.CellToWorld(new Vector3Int(currentNode.Position.x, 0, currentNode.Position.y)));
                
                // Check if we hit the exact goal cell
                if (currentNode.Position == goalCell) 
                {
                    var path = ReconstructPath(currentNode);
                    
                    if (DebugManager.Instance != null && DebugManager.Instance.aStar)
                    {
                        for (var i = 0; i < path.Count - 1; i++)
                        {
                            // Drawing this for slightly longer (e.g., 3 seconds) so it stays 
                            // visible just a bit longer than the search tree
                            Debug.DrawLine(path[i], path[i + 1], Color.green, 3f);
                        }
                    }
                    
                    return path;
                }
                
                foreach (Vector2Int neighborPos in GetNeighbors(currentNode.Position))
                {
                    if (closedSet.Contains(neighborPos)) continue;
                    if (!IsTraversableAStar(neighborPos)) continue;
                    
                    AStarNode neighborNode = openSet.FirstOrDefault(n => n.Position == neighborPos);
                    
                    if (neighborNode == null)
                    {
                        neighborNode = new AStarNode(pos: neighborPos, goal: goalCell, obstacleMap: _obstacleMap, 
                            voronoiMap:_voronoiMap, voronoiCellScale: _voronoiCellScale,
                            dangerPenaltyMultiplier: _dangerPenaltyMultiplier, parent: currentNode);
                        openSet.Add(neighborNode);
                        
                        // Draw cyan lines for newly explored paths. They will vanish after 2 seconds.
                        if (DebugManager.Instance != null && DebugManager.Instance.aStar)
                        {
                            Vector3 currWorld = _obstacleMap.CellToWorld(new Vector3Int(currentNode.Position.x, 0, currentNode.Position.y));
                            Vector3 neighWorld = _obstacleMap.CellToWorld(new Vector3Int(neighborPos.x, 0, neighborPos.y));
                            Debug.DrawLine(currWorld, neighWorld, Color.cyan, 2f);
                        }
                    }
                    else if (neighborNode.CostToCome(parent: currentNode) < neighborNode.GCost)
                    {
                        neighborNode.SwitchParent(currentNode);
                        
                        // Draw magenta lines if A* found a faster shortcut to an already explored node
                        if (DebugManager.Instance != null && DebugManager.Instance.aStar)
                        {
                            Vector3 currWorld = _obstacleMap.CellToWorld(new Vector3Int(currentNode.Position.x, 0, currentNode.Position.y));
                            Vector3 neighWorld = _obstacleMap.CellToWorld(new Vector3Int(neighborPos.x, 0, neighborPos.y));
                            Debug.DrawLine(currWorld, neighWorld, Color.magenta, 2f);
                        }
                    }
                }
            }
            
            Debug.LogError(
                $"A* failed after {iter} iterations. Explored {_astarExploredNodes.Count} nodes, OpenSet empty: {openSet.Count == 0}. " +
                $"startCell={originalStartCell}->{startCell} goalCell={originalGoalCell}->{goalCell} " +
                $"dynamicBlockedCells={_dynamicBlockedCells.Count} hasExtraConstraint={_additionalTraversability != null}");
            return null;
        }
        
        
        /// <summary>
        /// The nodes of the A* algorithm. Each node stores its position, gCost (cost from start),
        /// hCost (heuristic to goal), and a reference to its parent node for path reconstruction.
        /// </summary>
        private class AStarNode
        {
            public float GCost;
            private readonly float _hCost;
            public AStarNode Parent;
            public readonly Vector2Int Position;
            private readonly ObstacleMapV2 _obstacleMap;
            private readonly Dictionary<Vector2Int, VoronoiCellData> _voronoiMap;
            private readonly int _voronoiCellScale;
            private readonly float _dangerPenaltyMultiplier;

            public AStarNode(Vector2Int pos, Vector2Int goal, ObstacleMapV2 obstacleMap, 
                Dictionary<Vector2Int, VoronoiCellData> voronoiMap, int voronoiCellScale,
                float dangerPenaltyMultiplier, AStarNode parent=null)
            {
                Position = pos;
                Parent = parent;
                _obstacleMap = obstacleMap;
                _voronoiMap = voronoiMap;
                _voronoiCellScale = Mathf.Max(1, voronoiCellScale);
                _dangerPenaltyMultiplier = Mathf.Max(1f, dangerPenaltyMultiplier);

                GCost = CostToCome(parent: parent);
                _hCost = Heuristic(goal: goal);
            }
    
            /// <summary>
            /// Estimated distance to the goal. 
            /// </summary>
            /// <param name="goal">The goal position. </param>
            /// <returns>Euclidian distance to the goal. </returns>
            private float Heuristic(Vector2Int goal)
            {
                var goalWorld = _obstacleMap.CellToWorld(new Vector3Int(goal.x, 0, goal.y));
                var currentWorld = _obstacleMap.CellToWorld(new Vector3Int(Position.x, 0, Position.y));
                
                return Vector3.Distance(goalWorld, currentWorld);
            }

            public float CostToCome(AStarNode parent)
            {
                if (parent == null) return 0f;
                
                // Calculate true step cost mimicking the precomputation step
                Vector3 parentWorld = _obstacleMap.CellToWorld(new Vector3Int(parent.Position.x, 0, parent.Position.y));
                Vector3 currentWorld = _obstacleMap.CellToWorld(new Vector3Int(Position.x, 0, Position.y));

                var multiplier = 1f;
                if (_voronoiMap != null)
                {
                    var voronoiKey = new Vector2Int(
                        FloorDiv(Position.x, _voronoiCellScale),
                        FloorDiv(Position.y, _voronoiCellScale));

                    if (_voronoiMap.TryGetValue(voronoiKey, out var cellData))
                    {
                        // Smoothly scale cost so near-enemy cells are discouraged without hard blocking.
                        float danger = Mathf.Clamp01(cellData.Danger);
                        multiplier = Mathf.Lerp(1f, _dangerPenaltyMultiplier, danger);
                    }
                }
                
                return parent.GCost + multiplier * Vector3.Distance(parentWorld, currentWorld);
            }

            /// <summary>
            /// Switches the parent of this node to a new parent and updates the gCost accordingly. This is used when we find a better path to an existing node in the open set.
            /// </summary>
            /// <param name="newParent">The new parent node. </param>
            public void SwitchParent(AStarNode newParent)
            {
                Parent = newParent;
                GCost = CostToCome(parent:newParent);
            }
    
            public float FCost => GCost + _hCost;

            /// <summary>
            /// Integer floor-division that stays correct for negative coordinates.
            /// This is required when mapping fine-grid cells into coarser Voronoi cells.
            /// </summary>
            private static int FloorDiv(int value, int divisor)
            {
                if (divisor <= 0)
                    return value;

                if (value >= 0)
                    return value / divisor;

                return -(((-value) + divisor - 1) / divisor);
            }
        }
        
        
        /// <summary>
        /// Returns the path from the start node to the given end node. 
        /// </summary>
        /// <param name="endNode">The end node. </param>
        /// <returns>The path to the end node. </returns>
        private List<Vector3> ReconstructPath(AStarNode endNode)
        {
            List<Vector3> path = new();
            var current = endNode;

            while (current != null)
            {
                // Convert Vector2Int coordinates back to 3D Vector3 world coordinates
                Vector3 worldPos = _obstacleMap.CellToWorld(new Vector3Int(current.Position.x, 0, current.Position.y));
                path.Add(worldPos);
                current = current.Parent;
            }

            path.Reverse();
            return path;
        }
        
        
        /// <summary>
        /// Get 8-connected neighbors (including diagonals) for a grid cell.
        /// </summary>
        /// <param name="pos">The node position we base the neighbors on.</param>
        /// <returns>List of the neighbor positions. </returns>
        private static List<Vector2Int> GetNeighbors(Vector2Int pos)
        {
            List<Vector2Int> neighbors = new();

            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
    
                    neighbors.Add(new Vector2Int(pos.x + dx, pos.y + dy));
                }
            }

            return neighbors;
        }
        
        
        /// <summary>
        /// Checks if a cell is traversable under static, dynamic, and optional extra constraints.
        /// </summary>
        /// <param name="cellPos">The cell coordinate to test.</param>
        /// <returns>True if the cell can be traversed; otherwise false.</returns>
        private bool IsTraversableAStar(Vector2Int cellPos)
        {
            if (_obstacleMap == null)
                return false;

            if (_dynamicBlockedCells.Contains(cellPos))
                return false;

            Vector3 worldPosition = _obstacleMap.CellToWorld(new Vector3Int(cellPos.x, 0, cellPos.y));
            if (_additionalTraversability != null && !_additionalTraversability(worldPosition))
                return false;

            if (_obstacleMap.traversabilityPerCell.TryGetValue(cellPos, out var traversability))
            {
                return traversability == ObstacleMapV2.Traversability.Free;
            }

            return false; // Cell out of bounds
        }

        private Vector2Int ToCellKey(Vector3 localPosition)
        {
            var cellPos = _obstacleMap.WorldToCell(localPosition);
            return new Vector2Int(cellPos.x, cellPos.z);
        }

        /// <summary>
        /// Finds the nearest traversable cell.
        /// </summary>
        /// <param name="origin">Cell coordinate to start from.</param>
        /// <param name="maxRadius">Maximum search radius in cells.</param>
        /// <returns>The nearest traversable cell, or the original cell if none is found.</returns>
        private Vector2Int FindNearestFreeCell(Vector2Int origin, int maxRadius = 12)
        {
            if (IsTraversableAStar(origin)) return origin;

            for (var r = 1; r <= maxRadius; r++)
            {
                for (var dx = -r; dx <= r; dx++)
                {
                    for (var dy = -r; dy <= r; dy++)
                    {
                        var candidate = new Vector2Int(origin.x + dx, origin.y + dy);

                        if (IsTraversableAStar(candidate)) return candidate;
                    }
                }
            }

            return origin;
        }

    }
}
