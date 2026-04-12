using System.Collections.Generic;
using UnityEngine;
using Scripts.Map;
using PacMan.Agent.Debugging;

namespace PacMan.Agent.PathFinding
{
    /// <summary>
    /// Holds the safety status and distance for a single cell in the Voronoi partition.
    /// </summary>
    public struct VoronoiCellData
    {
        /// <summary>
        /// True if the agent can reach this cell before any enemy. 
        /// False if an enemy can reach it first.
        /// </summary>
        public bool IsSafe;

        /// <summary>
        /// The distance to the entity (agent or enemy) that claimed this cell.
        /// </summary>
        public float Distance;
    }

    public class VoronoiPartitioning
    {
        private ObstacleMapV2 _obstacleMap;

        public VoronoiPartitioning(ObstacleMapV2 map)
        {
            _obstacleMap = map;
        }

        /// <summary>
        /// Calculates a 2-Team Voronoi partition: Agent vs. All Enemies.
        /// </summary>
        /// <param name="agentPosition">The world position of the current agent.</param>
        /// <param name="enemyPositions">A list of world-space positions of visible opponents.</param>
        public Dictionary<Vector2Int, VoronoiCellData> ComputeVoronoi(Vector3 agentPosition, List<Vector3> enemyPositions)
        {
            var voronoiMap = new Dictionary<Vector2Int, VoronoiCellData>();
            
            if (_obstacleMap == null || _obstacleMap.traversabilityPerCell == null)
                return voronoiMap;

            Queue<Vector2Int> queue = new Queue<Vector2Int>();

            // 1. Seed the Agent (Safe Zone Source)
            var agentCell3D = _obstacleMap.WorldToCell(agentPosition);
            var agentCell2D = new Vector2Int(agentCell3D.x, agentCell3D.z);
            
            voronoiMap[agentCell2D] = new VoronoiCellData { IsSafe = true, Distance = 0f };
            queue.Enqueue(agentCell2D);

            // 2. Seed all Enemies (Danger Zone Sources)
            foreach (var pos in enemyPositions)
            {
                var enemyCell3D = _obstacleMap.WorldToCell(pos);
                var enemyCell2D = new Vector2Int(enemyCell3D.x, enemyCell3D.z);
                
                // If an enemy is in the exact same cell as the agent (or another enemy), 
                // the first one processed wins. In this setup, Agent wins ties.
                if (!voronoiMap.ContainsKey(enemyCell2D))
                {
                    voronoiMap[enemyCell2D] = new VoronoiCellData { IsSafe = false, Distance = 0f };
                    queue.Enqueue(enemyCell2D);
                }
            }

            Vector2Int[] dirs = {
                Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right, 
                new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1) 
            };

            // 3. Multi-Source BFS
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                var currentData = voronoiMap[current];

                foreach (var dir in dirs)
                {
                    var neighbor = current + dir;

                    if (_obstacleMap.traversabilityPerCell.TryGetValue(neighbor, out var trav) && trav == ObstacleMapV2.Traversability.Free)
                    {
                        float stepDist = (dir.x == 0 || dir.y == 0) ? 1f : 1.414f;
                        float newDist = currentData.Distance + stepDist;

                        if (!voronoiMap.ContainsKey(neighbor) || newDist < voronoiMap[neighbor].Distance)
                        {
                            voronoiMap[neighbor] = new VoronoiCellData { IsSafe = currentData.IsSafe, Distance = newDist };
                            queue.Enqueue(neighbor);
                        }
                    }
                }
            }

            return voronoiMap;
        }

        public void DrawVoronoiDebug(Dictionary<Vector2Int, VoronoiCellData> voronoiMap)
        {
            if (DebugManager.Instance == null || !DebugManager.Instance.voronoi || voronoiMap == null) return;

            foreach (var kvp in voronoiMap)
            {
                Vector3Int cellLocation = new Vector3Int(kvp.Key.x, 0, kvp.Key.y);
                Vector3 center = _obstacleMap.CellToWorld(cellLocation) + _obstacleMap.trueScale / 2f;
                
                // Green for safe (agent reaches first), Red for dangerous (enemy reaches first)
                Color regionColor = kvp.Value.IsSafe ? Color.green : Color.red;
                
                // Make the edges more transparent so we can still see the map
                float maxDist = 30f; // Adjust this based on your map size
                regionColor.a = Mathf.Lerp(0.5f, 0.1f, kvp.Value.Distance / maxDist);

                Gizmos.color = regionColor;
                Gizmos.DrawCube(center, _obstacleMap.trueScale * 0.95f);
            }
        }
    }
}