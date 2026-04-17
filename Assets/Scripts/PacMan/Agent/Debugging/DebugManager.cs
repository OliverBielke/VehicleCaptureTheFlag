using UnityEngine;
using Scripts.Map;
using UnityEngine.Serialization;


namespace PacMan.Agent.Debugging
{
    public class DebugManager : MonoBehaviour
    {
        // The Singleton instance
        public static DebugManager Instance { get; private set; }

        [Header("PacMan Debugging")]
        [Tooltip("Show the generated Obstacle Grid Map")]
        public bool obstacleMap;

        [Tooltip("Show the Division of the Middle")]
        public bool middle;
        
        [Tooltip("Show the Voronoi Partitioning")]
        public bool voronoi;
        
        [Tooltip("Show Pathfinding Planner steps")]
        public bool aStar;

        [Tooltip("Show the Current Path")]
        public bool path;

        [Tooltip("Show the Velocity Obstacle Debugger")]
        public bool vO;

        [Tooltip("Show the Enemy Localization Debugger")]
        public bool enemyLocalization;

        [Tooltip("Show role/mode/reason labels above agents")]
        public bool agentHud = true;

        private void Awake()
        {
            // Standard Singleton setup
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
            }
            else
            {
                Instance = this;
                // Optional: Keep this alive across scene loads
                DontDestroyOnLoad(gameObject); 
            }
        }
        
        private void OnDrawGizmos()
        {
            // If the toggle is on, and the map has finished generating somewhere in the game
            if (obstacleMap && ObstacleMapV2.Instance != null)
            {
                // Tell the map to draw itself!
                ObstacleMapV2.Instance.DrawMapGizmos();
            }
        }
    }
}
