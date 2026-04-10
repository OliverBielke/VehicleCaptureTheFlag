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
        private readonly List<Vector2Int> _astarExploredNodes = new();
        
        public Astar(ObstacleMapV2 obstacleMap)
        {
            _obstacleMap = obstacleMap;
        }

        /// <summary>
        /// Run the A* algorithm. 
        /// </summary>
        /// <param name="start">Start position. </param>
        /// <param name="goal">Goal position. </param>
        /// <returns>The planned path. </returns>
        public List<Vector3> PlanPathAStar(Vector3 start, Vector3 goal)
        {
            // Convert world positions to map grid cells
            var startCell3D = _obstacleMap.WorldToCell(start);
            var goalCell3D = _obstacleMap.WorldToCell(goal);
            
            var startCell = new Vector2Int(startCell3D.x, startCell3D.z);
            var goalCell = new Vector2Int(goalCell3D.x, goalCell3D.z);

            startCell = FindNearestFreeCell(startCell);

            // Get true world positions for precise debug drawing
            Vector3 startWorld = _obstacleMap.CellToWorld(new Vector3Int(startCell.x, 0, startCell.y));
            Vector3 goalWorld = _obstacleMap.CellToWorld(new Vector3Int(goalCell.x, 0, goalCell.y));

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
                goalCell = FindNearestFreeCell(goalCell);
                if (!IsTraversableAStar(goalCell))
                {
                    Debug.LogError($"A* goal {goalCell} is not traversable. Not even surrounding nodes. Can't plan path.");
                    return null;
                }
            }
            
            List<AStarNode> openSet = new();
            HashSet<Vector2Int> closedSet = new();

            // Pass the map instance so the node can check precomputed distances
            var startNode = new AStarNode(pos: startCell, goal: goalCell, obstacleMap: _obstacleMap);
            openSet.Add(startNode);
            
            const int maxIterations = 50000;
            var iter = 0;

            while (openSet.Count > 0 && iter < maxIterations)
            {
                iter++;
                
                var currentNode = openSet.OrderBy(n => n.FCost).First();
                openSet.Remove(currentNode);
                closedSet.Add(currentNode.Position);
                
                _astarExploredNodes.Add(currentNode.Position);
                
                // Check if we hit the exact goal cell
                if (currentNode.Position == goalCell) 
                {
                    var path = ReconstructPath(currentNode);
                    
                    if (DebugManager.Instance != null && DebugManager.Instance.aStar)
                    {
                        for (var i = 0; i < path.Count - 1; i++)
                        {
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
                        neighborNode = new AStarNode(pos: neighborPos, goal: goalCell, obstacleMap: _obstacleMap, parent: currentNode);
                        openSet.Add(neighborNode);
                        
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
                        
                        if (DebugManager.Instance != null && DebugManager.Instance.aStar)
                        {
                            Vector3 currWorld = _obstacleMap.CellToWorld(new Vector3Int(currentNode.Position.x, 0, currentNode.Position.y));
                            Vector3 neighWorld = _obstacleMap.CellToWorld(new Vector3Int(neighborPos.x, 0, neighborPos.y));
                            Debug.DrawLine(currWorld, neighWorld, Color.magenta, 2f);
                        }
                    }
                }
            }
            
            Debug.LogError($"A* failed after {iter} iterations. Explored {_astarExploredNodes.Count} nodes, OpenSet empty: {openSet.Count == 0}");
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

            public AStarNode(Vector2Int pos, Vector2Int goal, ObstacleMapV2 obstacleMap, AStarNode parent=null)
            {
                Position = pos;
                Parent = parent;
                _obstacleMap = obstacleMap;

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
        
                return parent.GCost + Vector3.Distance(parentWorld, currentWorld);
            }

            public void SwitchParent(AStarNode newParent)
            {
                Parent = newParent;
                GCost = CostToCome(parent:newParent);
            }
    
            public float FCost => GCost + _hCost;
        }
        
        
        /// <summary>
        /// Rounds a position to the nearest grid point based on the specified grid size. This helps to discretize the search space for A*.
        /// </summary>
        /// <param name="pos">The position we want to round. </param>
        /// <param name="gridSize">The grid size. </param>
        /// <returns>The rounded position. </returns>
        private static Vector3 RoundToGrid(Vector3 pos, float gridSize)
        {
            return new Vector3(
                Mathf.Round(pos.x / gridSize) * gridSize,
                pos.y,
                Mathf.Round(pos.z / gridSize) * gridSize
            );
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
        /// Get the neighboring positions around the given position based on the specified grid size. This generates 8-connected neighbors (including diagonals).
        /// </summary>
        /// <param name="pos">The node position we base the neighbors on. </param>
        /// <param name="gridSize">The grid size. </param>
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
        /// Checks if a position is traversable. 
        /// </summary>
        /// <param name="position">The position we want to check. </param>
        /// <returns>True if the position is traversable and false otherwise. </returns>
        private bool IsTraversableAStar(Vector2Int cellPos)
        {
            if (_obstacleMap == null) return false;

            if (_obstacleMap.traversabilityPerCell.TryGetValue(cellPos, out var traversability))
            {
                return traversability == ObstacleMapV2.Traversability.Free;
            }

            return false; // Cell out of bounds
        }

        /// <summary>
        /// Finds nearest traversable cell. 
        /// </summary>
        /// <param name="origin"> Coordinate to check </param>
        /// <param name="gridSize"> Size of the grids </param>
        /// <param name="maxRadius"> Radius to check snap </param>
        /// <returns>True if the position is traversable and false otherwise. </returns>
        private Vector2Int FindNearestFreeCell(Vector2Int origin, int maxRadius = 3)
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