using System;
using System.Collections.Generic;
using PacMan.Game;
using PacMan.Interface.PacMan;
using PacMan.Local;
using Scripts.VecEnv.Core;
using Scripts.VecEnv.Message;
using UnityEngine;
using Random = UnityEngine.Random;

namespace PacMan.RLExample
{
    public class PacManGymAgent : GymAgent
    {
        private PacManAgentManager _agent;
        private PacManMovementController _controller;
        private bool _caughtTarget;
        private Vector2 _currentAction = Vector2.zero;
        private PacManAgentManager _target;
        private float _distancePrevious;

        private void Awake()
        {
            GymVecEnvManager.Instance.physicsStepsPerGymStep = 5;
            _agent = GetComponent<PacManAgentManager>();
        }

        protected override void Initialize()
        {
        }

        protected override void CollectInfo(Dictionary<string, float> metadata)
        {
            metadata.Add("Red", _agent.CompareTag("Red") ? 1 : 0); // Just some proverbial food for thought
            metadata.Add("Blue", _agent.CompareTag("Blue") ? 1 : 0);
        }

        protected override void GymReset()
        {
            var localRandomPosition = new Vector3(Random.Range(-13f, -2.5f), 0.4f, Random.Range(-5f, 5f));
            _agent.transform.position = _agent.PacManGameManager.transform.TransformPoint(localRandomPosition);
            _caughtTarget = false;
            _target = _agent.GetVisibleEnemyAgents()[0];

            localRandomPosition = new Vector3(Random.Range(-13f, -2.5f), 0.4f, Random.Range(-5f, 5f));
            _target.transform.position = _agent.PacManGameManager.transform.TransformPoint(localRandomPosition);
            _distancePrevious = DistanceToTarget().magnitude;
        }

        public void FixedUpdate()
        {
            _agent.Movement.ApplyDesiredControl(_currentAction);
        }

        protected override void SetAction(AgentAction agentAction)
        {
            _currentAction = new Vector2(agentAction.Continuous[0], agentAction.Continuous[1]);
        }

        protected override void CollectObservation(ref AgentObservation observation)
        {
            var enemyRelative = DistanceToTarget(); //Normalized target vector. Not in agent space, because actions are not in agent space :)
            observation.AppendContinuous(enemyRelative.x);
            observation.AppendContinuous(enemyRelative.z);
        }

        private Vector3 DistanceToTarget()
        {
            return (_agent.GetVisibleEnemyAgents()[0].transform.position - _agent.transform.position) / 15f;
        }

        protected override float CollectReward()
        {
            if (_caughtTarget) return 0.75f;
            var distanceCurrent = DistanceToTarget().magnitude;
            var potentialReward = _distancePrevious - distanceCurrent;
            return potentialReward * 0.25f;
        }

        protected override EnvironmentState GymStep()
        {
            if (_caughtTarget) return EnvironmentState.Done;
            return EnvironmentState.Running;
        }

        public virtual void OnCollisionStay(Collision other)
        {
            if (other.gameObject.name == "PacManBrownian" && other.gameObject.tag != tag)
            {
                _caughtTarget = true;
            }
        }

        protected override AgentAction ProduceDummyAction(AgentAction dummyAgentAction)
        {
            var x = 0;
            var z = 0;
            if (Input.GetKey("w"))
            {
                z = 1;
            }

            if (Input.GetKey("a"))
            {
                x = -1;
            }

            if (Input.GetKey("s"))
            {
                z = -1;
            }

            if (Input.GetKey("d"))
            {
                x = 1;
            }

            dummyAgentAction.Continuous[0] = x;
            dummyAgentAction.Continuous[1] = z;
            return dummyAgentAction;
        }
    }
}