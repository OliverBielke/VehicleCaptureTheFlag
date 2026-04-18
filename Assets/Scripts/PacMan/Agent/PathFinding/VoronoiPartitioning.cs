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
        private static int _computeLogCounter;
        private static int _drawLogCounter;
        private const int LogEveryNCalls = 20;

        public VoronoiPartitioning(ObstacleMapV2 map)
        {
            _obstacleMap = map;
        }

        /// <summary>
        /// Calculates a 2-Team Voronoi partition: Agent vs. All Enemies.
        /// </summary>
        /// <param name="agentPosition">The world position of the current agent.</param>
        /// <param name="enemyPositions">A list of world-space positions of visible opponents.</param>
        /// <param name="includeCellPredicate">
        /// Optional cell filter. If provided, only cells returning true are considered during expansion
        /// and in the final Voronoi output.
        /// </param>
        /// <returns>Voronoi data keyed by cell coordinate.</returns>
        public Dictionary<Vector2Int, VoronoiCellData> ComputeVoronoi(
            Vector3 agentPosition,
            List<Vector3> enemyPositions,
            System.Func<Vector2Int, bool> includeCellPredicate = null)
        {
            var voronoiMap = new Dictionary<Vector2Int, VoronoiCellData>();
            bool shouldLog = DebugManager.Instance != null &&
                             DebugManager.Instance.voronoi &&
                             (++_computeLogCounter % LogEveryNCalls == 0);
            
            if (_obstacleMap == null || _obstacleMap.traversabilityPerCell == null)
                return voronoiMap;

            var agentDistances = new Dictionary<Vector2Int, float>();
            var enemyDistances = new Dictionary<Vector2Int, float>();
            Queue<Vector2Int> agentQueue = new Queue<Vector2Int>();
            Queue<Vector2Int> enemyQueue = new Queue<Vector2Int>();

            // 1. Seed the Agent (Safe Zone Source)
            var agentCell3D = _obstacleMap.WorldToCell(agentPosition);
            var agentCell2D = new Vector2Int(agentCell3D.x, agentCell3D.z);
            bool agentSeedEligible = IsCellEligible(agentCell2D, includeCellPredicate);
            
            if (agentSeedEligible)
            {
                agentDistances[agentCell2D] = 0f;
                agentQueue.Enqueue(agentCell2D);
            }

            // 2. Seed all Enemies (Danger Zone Sources)
            int enemySeedEligibleCount = 0;
            int enemySeedDuplicateCount = 0;
            int enemySeedRejectedCount = 0;
            foreach (var pos in enemyPositions)
            {
                var enemyCell3D = _obstacleMap.WorldToCell(pos);
                var enemyCell2D = new Vector2Int(enemyCell3D.x, enemyCell3D.z);
                
                // If an enemy is in the exact same cell as the agent (or another enemy), 
                // the first one processed wins. In this setup, Agent wins ties.
                if (!IsCellEligible(enemyCell2D, includeCellPredicate))
                {
                    enemySeedRejectedCount++;
                    continue;
                }

                if (enemyDistances.ContainsKey(enemyCell2D))
                {
                    enemySeedDuplicateCount++;
                    continue;
                }

                enemyDistances[enemyCell2D] = 0f;
                enemyQueue.Enqueue(enemyCell2D);
                enemySeedEligibleCount++;
            }

            Vector2Int[] dirs = {
                Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right, 
                new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1) 
            };

            // 3. Compute separate distance fields, then derive a graded risk map.
            RelaxDistanceField(agentDistances, agentQueue, dirs, includeCellPredicate);
            RelaxDistanceField(enemyDistances, enemyQueue, dirs, includeCellPredicate);

            int freeCells = 0;
            int includeFilteredOut = 0;
            int unmappedCells = 0;
            int safeCells = 0;
            int unsafeCells = 0;
            foreach (var cellTrav in _obstacleMap.traversabilityPerCell)
            {
                if (cellTrav.Value != ObstacleMapV2.Traversability.Free)
                    continue;

                freeCells++;

                if (includeCellPredicate != null && !includeCellPredicate(cellTrav.Key))
                {
                    includeFilteredOut++;
                    continue;
                }

                var cell = cellTrav.Key;
                bool hasAgentDistance = agentDistances.TryGetValue(cell, out float agentDistance);
                bool hasEnemyDistance = enemyDistances.TryGetValue(cell, out float enemyDistance);

                if (!hasAgentDistance && !hasEnemyDistance)
                {
                    unmappedCells++;
                    continue;
                }

                if (!hasAgentDistance)
                    agentDistance = float.MaxValue;

                if (!hasEnemyDistance)
                    enemyDistance = float.MaxValue;

                bool isSafe = agentDistance <= enemyDistance;
                float sourceDistance = isSafe ? agentDistance : enemyDistance;
                if (isSafe) safeCells++; else unsafeCells++;

                voronoiMap[cell] = new VoronoiCellData
                {
                    IsSafe = isSafe,
                    Distance = sourceDistance,
                    AgentDistance = agentDistance,
                    EnemyDistance = enemyDistance,
                    Danger = ComputeDanger(agentDistance, enemyDistance)
                };
            }

            if (!agentSeedEligible)
            {
                Debug.LogWarning($"[Voronoi] Agent seed rejected at {agentCell2D}. includeCellPredicate active={includeCellPredicate != null}");
            }

            if (enemyPositions.Count > 0 && enemySeedEligibleCount == 0)
            {
                Debug.LogWarning($"[Voronoi] All enemy seeds rejected. enemies={enemyPositions.Count}, rejected={enemySeedRejectedCount}, duplicates={enemySeedDuplicateCount}");
            }

            if (voronoiMap.Count == 0)
            {
                Debug.LogWarning("[Voronoi] Computed map is empty after filtering/expansion.");
            }

            if (shouldLog)
            {
                Debug.Log(
                    $"[Voronoi] Compute summary | agentCell={agentCell2D} agentSeed={agentSeedEligible} " +
                    $"enemyInput={enemyPositions.Count} enemySeeds={enemySeedEligibleCount} enemyRejected={enemySeedRejectedCount} enemyDup={enemySeedDuplicateCount} " +
                    $"free={freeCells} includeFiltered={includeFilteredOut} unmapped={unmappedCells} safe={safeCells} unsafe={unsafeCells} out={voronoiMap.Count}");
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

            // Strongly damp danger with enemy distance so far-away enemies do not saturate risk.
            float enemyProximityRisk = 1f / (1f + enemyDistance * enemyDistance * 0.75f);

            // Ownership influence is secondary: enemy-ahead cells are riskier, but still bounded by proximity.
            float margin = enemyDistance - agentDistance;
            float ownershipRisk = 1f / (1f + Mathf.Exp(margin / 1.6f));
            
            float combined = enemyProximityRisk * 0.35f + ownershipRisk * 0.65f;
            return Mathf.Clamp01(combined);
        }

        /// <summary>
        /// Expands a multi-source distance field across traversable cells using 8-connected movement.
        /// </summary>
        /// <param name="distances">Dictionary that stores the shortest discovered distance per cell.</param>
        /// <param name="queue">Frontier queue seeded with one or more source cells.</param>
        /// <param name="directions">Neighbor offsets used for propagation.</param>
        /// <param name="includeCellPredicate">Optional cell filter used to limit traversal.</param>
        private void RelaxDistanceField(
            Dictionary<Vector2Int, float> distances,
            Queue<Vector2Int> queue,
            IReadOnlyList<Vector2Int> directions,
            System.Func<Vector2Int, bool> includeCellPredicate)
        {
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                float currentDistance = distances[current];

                foreach (var dir in directions)
                {
                    var neighbor = current + dir;
                    if (!IsCellEligible(neighbor, includeCellPredicate))
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

        /// <summary>
        /// Checks if a cell is traversable and, if supplied, accepted by the caller's filter.
        /// </summary>
        /// <param name="cell">Grid cell coordinate in XZ space.</param>
        /// <param name="includeCellPredicate">Optional filter for restricting Voronoi coverage.</param>
        /// <returns>True if the cell is valid for Voronoi processing.</returns>
        private bool IsCellEligible(Vector2Int cell, System.Func<Vector2Int, bool> includeCellPredicate)
        {
            return IsFreeCell(cell) && (includeCellPredicate == null || includeCellPredicate(cell));
        }

        public void DrawVoronoiDebug(
            Dictionary<Vector2Int, VoronoiCellData> voronoiMap, 
            float safetyThreshold = 0.5f,
            System.Func<Vector2Int, bool> cellFilter = null)
        {
            if (DebugManager.Instance == null || !DebugManager.Instance.voronoi || voronoiMap == null) return;

            bool shouldLog = (++_drawLogCounter % LogEveryNCalls == 0);
            int thresholdFilteredOut = 0;
            int cellFilteredOut = 0;
            int drawnCount = 0;

            foreach (var kvp in voronoiMap)
            {
                // Only display cells with danger below threshold
                if (kvp.Value.Danger > safetyThreshold)
                {
                    thresholdFilteredOut++;
                    continue;
                }
                
                // Apply optional cell filter (e.g., opponent side only)
                if (cellFilter != null && !cellFilter(kvp.Key))
                {
                    cellFilteredOut++;
                    continue;
                }

                Vector3Int cellLocation = new Vector3Int(kvp.Key.x, 0, kvp.Key.y);
                Vector3 center = _obstacleMap.CellToWorld(cellLocation) + _obstacleMap.trueScale / 2f;
                
                // Green->Red gradient based on graded danger.
                Color regionColor = Color.Lerp(Color.green, Color.red, kvp.Value.Danger);
                
                // Make the edges more transparent so we can still see the map
                float maxDist = 30f; // Adjust this based on your map size
                regionColor.a = Mathf.Lerp(0.5f, 0.1f, kvp.Value.Distance / maxDist);

                Gizmos.color = regionColor;
                Gizmos.DrawCube(center, _obstacleMap.trueScale * 0.95f);
                drawnCount++;
            }

            if (drawnCount == 0)
            {
                Debug.LogWarning(
                    $"[Voronoi] Draw produced no visible cells | input={voronoiMap.Count} threshold={safetyThreshold:0.00} " +
                    $"dangerFiltered={thresholdFilteredOut} cellFiltered={cellFilteredOut} hasCellFilter={cellFilter != null}");
            }

            if (shouldLog)
            {
                Debug.Log(
                    $"[Voronoi] Draw summary | input={voronoiMap.Count} drawn={drawnCount} threshold={safetyThreshold:0.00} " +
                    $"dangerFiltered={thresholdFilteredOut} cellFiltered={cellFilteredOut} hasCellFilter={cellFilter != null}");
            }
        }
    }
}