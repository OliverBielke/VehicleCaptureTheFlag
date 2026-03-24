using JetBrains.Annotations;
using PacMan.Local;
using UnityEngine;

namespace PacMan.Game
{
    public enum ManagerMode
    {
        Local,
        Server,
        Client,
        Replay
    }

    public class PacManManagerModeSelector : MonoBehaviour
    {
        public static ManagerMode? mode_override = null;
        [CanBeNull] public static string map_override = null;

        public ManagerMode mode = ManagerMode.Local;

        public GameStatePlaybackManager playback_manager;
        public PacManGameManager game_manager;

        public void Awake()
        {
            if (mode_override.HasValue) mode = mode_override.Value;

            ConfigureManagerObjects();
        }

        private void ConfigureManagerObjects()
        {
            var usePlaybackManager = mode == ManagerMode.Client || mode == ManagerMode.Replay;
            var shareGameObject = playback_manager != null &&
                                  game_manager != null &&
                                  playback_manager.gameObject == game_manager.gameObject;

            SetManagerState(playback_manager, usePlaybackManager, shareGameObject);
            SetManagerState(game_manager, !usePlaybackManager, shareGameObject);
        }

        private static void SetManagerState(MonoBehaviour manager, bool isActive, bool keepGameObjectActive)
        {
            if (manager == null)
            {
                return;
            }

            if (!keepGameObjectActive && manager.gameObject.activeSelf != isActive)
            {
                manager.gameObject.SetActive(isActive);
            }

            manager.gameObject.transform.position = Vector3.zero;
            manager.gameObject.transform.rotation = Quaternion.Euler(Vector3.zero);
            manager.enabled = isActive;
        }
    }
}