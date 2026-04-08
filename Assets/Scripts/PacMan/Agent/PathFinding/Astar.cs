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
            const float gridSize = 0.2f;
            start.y = 0f;
            goal.y = 0f;
            start = FindNearestFreeCell(RoundToGrid(start, gridSize), gridSize);
            goal = RoundToGrid(goal, gridSize);

            // Mark the start and goal positions with an X so they stand out
            if (DebugManager.Instance != null && DebugManager.Instance.aStar)
            {
                var markerSize = 0.3f; // Adjust this if the cross is too big or small
        
                // Draw a Yellow cross for the Start position
                Debug.DrawLine(start + new Vector3(-markerSize, 0, -markerSize), start + new Vector3(markerSize, 0, markerSize), Color.yellow, 3f);
                Debug.DrawLine(start + new Vector3(-markerSize, 0, markerSize), start + new Vector3(markerSize, 0, -markerSize), Color.yellow, 3f);

                // Draw a Red cross for the Goal position
                Debug.DrawLine(goal + new Vector3(-markerSize, 0, -markerSize), goal + new Vector3(markerSize, 0, markerSize), Color.red, 3f);
                Debug.DrawLine(goal + new Vector3(-markerSize, 0, markerSize), goal + new Vector3(markerSize, 0, -markerSize), Color.red, 3f);
            }
            
            if (!IsTraversableAStar(goal))
            {
                Debug.LogError($"A* goal {goal} is not traversable. Trying closest position.");
                goal = FindNearestFreeCell(goal, gridSize);

                if (!IsTraversableAStar(goal))
                {
                    Debug.LogError($"A* goal {goal} is not traversable. Not even surrounding nodes. Can't plan path.");
                    return null;
                }
            }
            
            List<AStarNode> openSet = new();
            HashSet<Vector3> closedSet = new();

            var startNode = new AStarNode(pos:start, goal:goal);
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

                var distToGoal = Vector2.Distance(
                    new Vector2(currentNode.Position.x, currentNode.Position.z), 
                    new Vector2(goal.x, goal.z)
                );
                
                if (distToGoal < gridSize / 2f) //If at the goal grid
                {
                    Debug.Log($"A* found path in {iter} iterations");
                    var path = ReconstructPath(currentNode);
                    
                    // Draw the final winning path in Green.
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
                
                foreach (Vector3 neighborPos in GetNeighbors(currentNode.Position, gridSize))
                {
                    if (closedSet.Contains(neighborPos))
                        continue;
                    
                    if (!IsTraversableAStar(neighborPos))
                        continue;
                    
                    AStarNode neighborNode = openSet.FirstOrDefault(n => n.Position == neighborPos);
                    
                    if (neighborNode == null)
                    {
                        neighborNode = new AStarNode(pos: neighborPos, parent: currentNode, goal: goal);
                        openSet.Add(neighborNode);
                        
                        // Draw cyan lines for newly explored paths. They will vanish after 2 seconds.
                        if (DebugManager.Instance != null && DebugManager.Instance.aStar)
                        {
                            Debug.DrawLine(currentNode.Position, neighborPos, Color.cyan, 2f);
                        }
                    }
                    else if (neighborNode.CostToCome(parent: currentNode) < neighborNode.GCost)
                    {
                        neighborNode.SwitchParent(currentNode);
                        
                        // Draw magenta lines if A* found a faster shortcut to an already explored node
                        if (DebugManager.Instance != null && DebugManager.Instance.aStar)
                        {
                            Debug.DrawLine(currentNode.Position, neighborPos, Color.magenta, 2f);
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
            public readonly Vector3 Position;

            /// <summary>
            /// Initializes a new AStarNode with the given position. gCost and hCost are set to infinity by default, and parent is null.
            /// </summary>
            /// <param name="pos">The position of the node. </param>
            public AStarNode(Vector3 pos, Vector3 goal, AStarNode parent=null)
            {
                Position = pos;
                Parent = parent;

                GCost = CostToCome(parent: parent);

                _hCost = Heuristic(goal:goal);
            }
            
            /// <summary>
            /// Heuristic cost to go for the vehicle. 
            /// </summary>
            /// <param name="goal">The goal posiiton. </param>
            /// <returns>The estimated cost to go. </returns>
            private float Heuristic(Vector3 goal)
            {
                return Vector2.Distance(
                    new Vector2(Position.x, Position.z), 
                    new Vector2(goal.x, goal.z)
                );
            }

            /// <summary>
            /// Cost to come (gCost) is calculated as the parent's gCost plus the distance from the parent to this node. If there is no parent, gCost is 0.
            /// </summary>
            /// <returns>The cost to come to this node from start. </returns>
            public float CostToCome(AStarNode parent)
            {
                var cost = 0f;
                
                if (parent != null)
                {
                    cost += parent.GCost + Vector3.Distance(parent.Position, Position);
                }
                
                return cost;
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
        private static List<Vector3> ReconstructPath(AStarNode endNode)
        {
            List<Vector3> path = new();
            var current = endNode;
        
            while (current != null)
            {
                path.Add(current.Position);
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
        private static List<Vector3> GetNeighbors(Vector3 pos, float gridSize)
        {
            List<Vector3> neighbors = new();
    
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dz == 0) continue;
            
                    var neighbor = new Vector3(
                        pos.x + dx * gridSize,
                        pos.y,
                        pos.z + dz * gridSize
                    );
                    
                    // Snap the neighbor to the grid immediately
                    neighbors.Add(RoundToGrid(neighbor, gridSize));
                }
            }
    
            return neighbors;
        }
        
        
        /// <summary>
        /// Checks if a position is traversable. 
        /// </summary>
        /// <param name="position">The position we want to check. </param>
        /// <returns>True if the position is traversable and false otherwise. </returns>
        private bool IsTraversableAStar(Vector3 position)
        {
            if (_obstacleMap == null)
                return false;

            return _obstacleMap.GetLocalPointTraversibility(position) == ObstacleMapV2.Traversability.Free;
        }

        /// <summary>
        /// Finds nearest traversable cell. 
        /// </summary>
        /// <param name="origin"> Coordinate to check </param>
        /// <param name="gridSize"> Size of the grids </param>
        /// <param name="maxRadius"> Radius to check snap </param>
        /// <returns>True if the position is traversable and false otherwise. </returns>
        private Vector3 FindNearestFreeCell(Vector3 origin, float gridSize, int maxRadius = 3)
        {
            if (IsTraversableAStar(origin))
                return origin;

            for (var r = 1; r <= maxRadius; r++)
            {
                for (var dx = -r; dx <= r; dx++)
                {
                    for (var dz = -r; dz <= r; dz++)
                    {
                        var candidate = new Vector3(
                            origin.x + dx * gridSize,
                            origin.y,
                            origin.z + dz * gridSize
                        );

                        if (IsTraversableAStar(candidate))
                            return candidate;
                    }
                }
            }

            return origin;
        }

    }
}