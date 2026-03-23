using PacMan.Interface.PacMan;
using PacMan.Local;
using UnityEngine;

namespace PacMan.Game
{
    public class GameStatePlaybackAgentManager : PacManAgentManager
    {
        protected override void ConfigureForCurrentMode()
        {
            var movementController = GetComponent<PacManMovementController>();
            if (movementController != null)
            {
                movementController.enabled = false;
            }
        }

        public override void InitializeAI()
        {
            var selector = FindFirstObjectByType<PacManManagerModeSelector>();
            var playbackManager = PacManGameManager as GameStatePlaybackManager;
            if (selector != null &&
                selector.mode == ManagerMode.Client &&
                playbackManager != null &&
                playbackManager.IsControlledAgent(this))
            {
                base.InitializeAI();
            }
        }

        public override void OnTriggerEnter(Collider other)
        {
        }

        public override void OnCollisionStay(Collision other)
        {
        }

        public override void UpdateFoodDelivered()
        {
        }

        public override void UpdateAction(PacManAction? overrideAction = null)
        {
            SampleAction(overrideAction);
        }
    }
}
