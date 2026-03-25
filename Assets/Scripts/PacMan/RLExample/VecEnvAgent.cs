using System;
using PacMan.Local;
using Scripts.VecEnv.Core;
using Scripts.VecEnv.Message;

namespace PacMan.RLExample
{
    public class VecEnvAgent : GymAgent
    {
        private PacManAgentManager agent;

        private void Awake()
        {
            agent = GetComponent<PacManAgentManager>();
        }

        protected override void GymReset()
        {
            throw new System.NotImplementedException();
        }

        protected override void SetAction(AgentAction agentAction)
        {
            throw new System.NotImplementedException();
        }

        protected override void CollectObservation(ref AgentObservation observation)
        {
            throw new System.NotImplementedException();
        }

        protected override float CollectReward()
        {
            throw new System.NotImplementedException();
        }

        protected override EnvironmentState GymStep()
        {
            throw new System.NotImplementedException();
        }
    }
}