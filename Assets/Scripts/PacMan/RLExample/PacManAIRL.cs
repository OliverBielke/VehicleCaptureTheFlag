using PacMan.Agent;
using PacMan.Interface.PacMan;
using PacMan.Local;
using Scripts.Map;
using Scripts.VecEnv.Inference;
using Scripts.VecEnv.Message;
using Unity.InferenceEngine;
using UnityEngine;

namespace PacMan.RLExample
{
    public class PacManAIRL : PacManAI
    {
        private InferenceHelper helper;
        public ModelAsset modelAsset;

        public override void Initialize(MapManager mapManager)
        {
            _agent = GetComponent<PacManAgentManager>();
            helper = new InferenceHelper(modelAsset);
        }

        public override PacManAction Tick()
        {
            var visibleEnemies = _agent.GetVisibleEnemyAgents();
            if (visibleEnemies.Count > 0)
            {
                var firstVisible = visibleEnemies[0];

                if (!firstVisible.isGhost && _agent.isGhost)
                {
                    var action = helper.DoInference(PacManGymAgent.CollectObservation(new AgentObservation(4, 0), firstVisible, _agent));
                    return new PacManAction
                    {
                        Acceleration = new Vector2(action.Continuous[0], action.Continuous[1]),
                    };
                }
            }


            var x = 0;
            var z = 0;

            if (TeamAssignmentUtil.CheckTeam(gameObject) == Team.Blue && Input.GetKey("w") || TeamAssignmentUtil.CheckTeam(gameObject) == Team.Red && Input.GetKey("up"))
            {
                z = 1;
            }

            if (TeamAssignmentUtil.CheckTeam(gameObject) == Team.Blue && Input.GetKey("a") || TeamAssignmentUtil.CheckTeam(gameObject) == Team.Red && Input.GetKey("left"))
            {
                x = -1;
            }

            if (TeamAssignmentUtil.CheckTeam(gameObject) == Team.Blue && Input.GetKey("s") || TeamAssignmentUtil.CheckTeam(gameObject) == Team.Red && Input.GetKey("down"))
            {
                z = -1;
            }

            if (TeamAssignmentUtil.CheckTeam(gameObject) == Team.Blue && Input.GetKey("d") || TeamAssignmentUtil.CheckTeam(gameObject) == Team.Red && Input.GetKey("right"))
            {
                x = 1;
            }

            var droneAction = new PacManAction
            {
                Acceleration = new Vector2(x, z),
            };

            return droneAction;
        }
    }
}