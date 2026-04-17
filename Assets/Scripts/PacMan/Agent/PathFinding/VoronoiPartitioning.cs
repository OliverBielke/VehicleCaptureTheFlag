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

        /// <summary>
        /// Distance from this cell to the current agent source.
        /// </summary>
        public float AgentDistance;

        /// <summary>
        /// Distance from this cell to the closest enemy source.
        /// </summary>
        public float EnemyDistance;

        /// <summary>
        /// Normalized [0..1] danger score (1 = very dangerous, 0 = very safe).
        /// </summary>
        public float Danger;
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

            var agentDistances = new Dictionary<Vector2Int, float>();
            var enemyDistances = new Dictionary<Vector2Int, float>();
            Queue<Vector2Int> agentQueue = new Queue<Vector2Int>();
            Queue<Vector2Int> enemyQueue = new Queue<Vector2Int>();

            // 1. Seed the Agent (Safe Zone Source)
            var agentCell3D = _obstacleMap.WorldToCell(agentPosition);
            var agentCell2D = new Vector2Int(agentCell3D.x, agentCell3D.z);
            
            if (IsFreeCell(agentCell2D))
            {
                agentDistances[agentCell2D] = 0f;
                agentQueue.Enqueue(agentCell2D);
            }

            // 2. Seed all Enemies (Danger Zone Sources)
            foreach (var pos in enemyPositions)
            {
                var enemyCell3D = _obstacleMap.WorldToCell(pos);
                var enemyCell2D = new Vector2Int(enemyCell3D.x, enemyCell3D.z);
                
                // If an enemy is in the exact same cell as the agent (or another enemy), 
                // the first one processed wins. In this setup, Agent wins ties.
                if (!IsFreeCell(enemyCell2D) || enemyDistances.ContainsKey(enemyCell2D))
                {
                    continue;
                }

                enemyDistances[enemyCell2D] = 0f;
                enemyQueue.Enqueue(enemyCell2D);
            }

            Vector2Int[] dirs = {
                Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right, 
                new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1) 
            };

            // 3. Compute separate distance fields, then derive a graded risk map.
            RelaxDistanceField(agentDistances, agentQueue, dirs);
            RelaxDistanceField(enemyDistances, enemyQueue, dirs);

            foreach (var cellTrav in _obstacleMap.traversabilityPerCell)
            {
                if (cellTrav.Value != ObstacleMapV2.Traversability.Free)
                    continue;

                var cell = cellTrav.Key;
                bool hasAgentDistance = agentDistances.TryGetValue(cell, out float agentDistance);
                bool hasEnemyDistance = enemyDistances.TryGetValue(cell, out float enemyDistance);

                if (!hasAgentDistance && !hasEnemyDistance)
                    continue;

                if (!hasAgentDistance)
                    agentDistance = float.MaxValue;

                if (!hasEnemyDistance)
                    enemyDistance = float.MaxValue;

                bool isSafe = agentDistance <= enemyDistance;
                float sourceDistance = isSafe ? agentDistance : enemyDistance;

                voronoiMap[cell] = new VoronoiCellData
                {
                    IsSafe = isSafe,
                    Distance = sourceDistance,
                    AgentDistance = agentDistance,
                    EnemyDistance = enemyDistance,
                    Danger = ComputeDanger(agentDistance, enemyDistance)
                };
            }

            return voronoiMap;
        }

        /// <summary>
        /// Converts the relative agent/enemy travel distances for a cell into a normalized danger score.
        /// </summary>
        /// <param name="agentDistance">Distance from the agent source to the cell in grid-step units.</param>
        /// <param name="enemyDistance">Distance from the nearest enemy source to the cell in grid-step units.</param>
        /// <returns>
        /// A value in the range [0..1], where 0 is safest and 1 is most dangerous.
        /// </returns>
        private static float ComputeDanger(float agentDistance, float enemyDistance)
        {
            if (float.IsPositiveInfinity(enemyDistance) || enemyDistance >= float.MaxValue * 0.5f)
                return 0f;

            if (float.IsPositiveInfinity(agentDistance) || agentDistance >= float.MaxValue * 0.5f)
                return 1f;

            // Risk is higher when close to enemies and when enemy reaches the cell before us.
            float margin = enemyDistance - agentDistance;
            float enemyProximityRisk = Mathf.Exp(-enemyDistance / 4f);
            float ownershipRisk = 1f / (1f + Mathf.Exp(margin / 1.2f));

            return Mathf.Clamp01(enemyProximityRisk * 0.65f + ownershipRisk * 0.35f);
        }

        /// <summary>
        /// Expands a multi-source distance field across traversable cells using 8-connected movement.
        /// </summary>
        /// <param name="distances">Dictionary that stores the shortest discovered distance per cell.</param>
        /// <param name="queue">Frontier queue seeded with one or more source cells.</param>
        /// <param name="directions">Neighbor offsets used for propagation.</param>
        private void RelaxDistanceField(
            Dictionary<Vector2Int, float> distances,
            Queue<Vector2Int> queue,
            IReadOnlyList<Vector2Int> directions)
        {
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                float currentDistance = distances[current];

                foreach (var dir in directions)
                {
                    var neighbor = current + dir;
                    if (!IsFreeCell(neighbor))
                        continue;

                    float stepDistance = (dir.x == 0 || dir.y == 0) ? 1f : 1.414f;
                    float newDistance = currentDistance + stepDistance;

                    if (!distances.TryGetValue(neighbor, out float oldDistance) || newDistance < oldDistance)
                    {
                        distances[neighbor] = newDistance;
                        queue.Enqueue(neighbor);
                    }
                }
            }
        }

        /// <summary>
        /// Checks whether a cell exists in the map and is currently traversable.
        /// </summary>
        /// <param name="cell">Grid cell coordinate in XZ space.</param>
        /// <returns>True if the cell is marked as free; otherwise false.</returns>
        private bool IsFreeCell(Vector2Int cell)
        {
            return _obstacleMap != null &&
                   _obstacleMap.traversabilityPerCell != null &&
                   _obstacleMap.traversabilityPerCell.TryGetValue(cell, out var traversability) &&
                   traversability == ObstacleMapV2.Traversability.Free;
        }

        public void DrawVoronoiDebug(Dictionary<Vector2Int, VoronoiCellData> voronoiMap)
        {
            if (DebugManager.Instance == null || !DebugManager.Instance.voronoi || voronoiMap == null) return;

            foreach (var kvp in voronoiMap)
            {
                Vector3Int cellLocation = new Vector3Int(kvp.Key.x, 0, kvp.Key.y);
                Vector3 center = _obstacleMap.CellToWorld(cellLocation) + _obstacleMap.trueScale / 2f;
                
                // Green->Red gradient based on graded danger.
                Color regionColor = Color.Lerp(Color.green, Color.red, kvp.Value.Danger);
                
                // Make the edges more transparent so we can still see the map
                float maxDist = 30f; // Adjust this based on your map size
                regionColor.a = Mathf.Lerp(0.5f, 0.1f, kvp.Value.Distance / maxDist);

                Gizmos.color = regionColor;
                Gizmos.DrawCube(center, _obstacleMap.trueScale * 0.95f);
            }
        }
    }
}