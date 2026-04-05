using Scripts.Map;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Reflection;

namespace PacMan.Agent.Map
{
    public static class MapEditing
    {
        /// <summary>
        /// Inflate the obstacles. 
        /// </summary>
        /// <param name="map">The obstacle map. </param>
        /// <param name="radius">Radius of inflation in #cells. </param>
        public static void InflateObstacleMap(ObstacleMapV2 map, int radius)
        {
            if (map == null || radius <= 0) return;

            // Find all currently blocked cells
            var originalBlocked = map.traversabilityPerCell
                .Where(kvp => kvp.Value == ObstacleMapV2.Traversability.Blocked)
                .Select(kvp => kvp.Key)
                .ToList();

            var toBlock = new HashSet<Vector2Int>();

            foreach (var cell in originalBlocked)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    for (var dz = -radius; dz <= radius; dz++)
                    {
                        var neighbor = new Vector2Int(cell.x + dx, cell.y + dz);

                        if (!map.traversabilityPerCell.ContainsKey(neighbor))
                            continue;

                        // Circular inflation
                        if (dx * dx + dz * dz > radius * radius)
                            continue;

                        toBlock.Add(neighbor);
                    }
                }
            }

            // Apply the inflated blocked cells back to the map
            foreach (var cell in toBlock)
            {
                map.traversabilityPerCell[cell] = ObstacleMapV2.Traversability.Blocked;
            }
        }
        
        
        public static void DrawObstacleMap(
            Transform transform,
            ObstacleMapV2 obstacleMap,
            bool draw)
        {
            if (!draw || obstacleMap == null || obstacleMap.traversabilityPerCell == null)
                return;

            foreach (var posEntity in obstacleMap.traversabilityPerCell)
            {
                var position = new Vector3Int(posEntity.Key.x, 0, posEntity.Key.y);

                var cellToWorld = obstacleMap.CellToWorld(position) + obstacleMap.trueScale / 2f;
                cellToWorld.y = transform.position.y + 0.25f;

                var gizmoSize = new Vector3(
                    obstacleMap.trueScale.x * 0.95f,
                    0.005f,
                    obstacleMap.trueScale.z * 0.95f
                );

                if (posEntity.Value == ObstacleMapV2.Traversability.Blocked)
                    Gizmos.color = Color.red;
                else
                    Gizmos.color = Color.green;

                Gizmos.DrawCube(cellToWorld, gizmoSize);
            }
        }
    }
}