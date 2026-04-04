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
        public static void InflateObstacleMap(ObstacleMap map, int radius)
        {
            if (map == null || radius <= 0) return;

            // Find all currently blocked cells
            var originalBlocked = map.traversabilityPerCell
                .Where(kvp => kvp.Value == ObstacleMap.Traversability.Blocked)
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
                map.traversabilityPerCell[cell] = ObstacleMap.Traversability.Blocked;
            }
        }
        
        
        public static void VisualizeObstacleMap(Transform transform, ObstacleMap obstacleMap, ref bool visualizerLinked)
        {
            // 1. On the very first Tick, forcefully inject our inflated map into the visualizer
            if (!visualizerLinked)
            {
                Transform gameManager = transform.parent;
                if (gameManager != null)
                {
                    ObstacleMapVisualizer visualizer = gameManager.GetComponentInChildren<ObstacleMapVisualizer>();
                    if (visualizer != null)
                    {
                        // Use C# Reflection to find the private "m_ObstacleMap" field...
                        FieldInfo privateMapField = typeof(ObstacleMapVisualizer).GetField("m_ObstacleMap", BindingFlags.NonPublic | BindingFlags.Instance);
                
                        if (privateMapField != null)
                        {
                            // ...and forcefully set it to the agent's already-inflated map!
                            privateMapField.SetValue(visualizer, obstacleMap);
                        }
                    }
                }
                visualizerLinked = true; // Make sure we only do this once
            }
            
        }
    }
}