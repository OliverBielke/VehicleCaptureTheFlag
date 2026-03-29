using System.Collections.Generic;
using System.Linq;
using PacMan.Interface.PacMan;
using PacMan.Local;
using Scripts.Map;
using UnityEngine;
using PacMan.Agent.PathFinding;
using PacMan.Agent.PathFollowing;

namespace PacMan.Agent
{
    public class PacManAI : MonoBehaviour
    {
        protected PacManAgentManager _agent;
        protected ObstacleMap _obstacleMap;
        protected MapManager _mapManager;
        private bool _hasGoal;
        private Vector3 _goalPosition;
        private List<Node> _waypoints;
        private DroneControlling _droneControlling;
        private Transform _initialDroneState;

        public virtual void Initialize(MapManager mapManager) // Ticked when all agents spawned by the network and seen properly by the client. Not the same as Start or Awake in this assignment.
        {
            _agent = GetComponent<PacManAgentManager>();
            _mapManager = mapManager;
            _obstacleMap = ObstacleMap.Initialize(_mapManager, new List<GameObject>(), Vector3.one);
            // All of the calls below should also work in here. Report it as a bug if you find that some part of the observations is inaccessible during init.
            _hasGoal = false;
        }

        public virtual PacManAction Tick() //The Tick from the network controller
        {
            _agent.GetTimeRemaining();
            _agent.GetScore();
            bool isGhost = _agent.IsGhost();
            bool isScared = _agent.IsScared();
            float scaredDuration = _agent.GetScaredRemainingDuration();

            float carriedFoodCount = _agent.GetCarriedFoodCount();

            List<GameObject> foodPositions = _agent.GetFoodObjects(); // Positions of food or last know position of food
            var activeFoodPositions = foodPositions.FindAll(food => food.activeSelf); // Food that is currently on the ground
            var inactiveFoodLatestPositions = foodPositions.FindAll(food => !food.activeSelf); // Food that is currently carried. The gameObject position will report where it was picked up from. Might be useful in some scenarios.
            List<GameObject> capsulePositions = _agent.GetCapsuleObjects();
            
            var isLocalPointTraversable = _obstacleMap?.GetLocalPointTraversibility(transform.localPosition);
            
            var teamAgentManagers = _agent.GetTeamAgents(); //Agents in team, including this agent
            var friendlyAgentManagers = _agent.GetFriendlyAgents(); //Agents in team, except this agent

            var visibleEnemyAgents = _agent.GetVisibleEnemyAgents(); // Enemy agents in LoS. Know percise information
            PacManObservations fetchEnemyObservations = _agent.GetEnemyObservations(); // Enemies out of LoS. Know partial information. 
            if (fetchEnemyObservations.Observations.Length > 0)
            {
                // Debug.Log(fetchEnemyObservations.ObservationFixedTime);
            }

            // Since the RigidBody is updated server side and the client only syncs position, rigidbody.Velocity does not report a velocity
            var velocity = _agent.GetVelocity(); // Use the manager method to get the true velocity from the server
            // friendlyAgentManager.GetVelocity(); // Given the damping, max velocity magnitude is around 2.34

            // If no goal is assigned
            if (!_hasGoal)
            {
                _initialDroneState = _agent.transform;
                var agentPos = _initialDroneState.position;
                var closestPos =  Vector3.zero;
                var closestDistance = float.MaxValue;
                foreach (var foodPosition in activeFoodPositions)
                {
                    //Check if food is on oppenents side
                    var foodPos = foodPosition.transform.position;
                    var startPos = _agent.globalStartPosition;
                    var isEatableFood = (startPos.x * foodPos.x < 0); //Food is eatable if on opposite side
                    
                    if (!isEatableFood) //If not eatable
                    {
                        continue;
                    }
                    
                    //Check distance to food
                    var curDistance = Vector3.Distance(foodPos, agentPos);

                    if (closestDistance <= curDistance) //If not the closest food
                    {
                        continue;
                    }
                    //Assign current food as closest
                    closestDistance = curDistance;
                    closestPos = foodPos;
                }
                
                var size = 0.5f;
                Debug.DrawLine(closestPos - Vector3.up * size, closestPos + Vector3.up * size, Color.red, 100f);
                Debug.DrawLine(closestPos - Vector3.left * size, closestPos + Vector3.left * size, Color.red, 100f);
                Debug.DrawLine(closestPos - Vector3.forward * size, closestPos + Vector3.forward * size, Color.red, 100f);

                _goalPosition = closestPos;
                _hasGoal = true;
                
                Astar astar = new();
                List<Vector3> astarPath = astar.PlanPathAStar(agentPos, _goalPosition);
        
                if (astarPath.Count < 2)
                {
                    Debug.LogError("A* failed - no path found for drone");
                }
        
                List<Node> nodes = new();
                foreach (Vector3 pos in astarPath)
                {
                    nodes.Add(new Node(pos.x, pos.z));
                }
                
                var groundPlane = GameObject.Find("GroundPlane");
                var groundCollider = groundPlane.GetComponent<Collider>();
                var smoother = new CGSmoother(agentPos.y, groundCollider);
                nodes = smoother.GetSmoothedPath(nodes);
                
                
                _waypoints = nodes;
        
                // Creates the new PD Controller for the new path
                _droneControlling = new DroneControlling(nodes, _goalPosition, _initialDroneState);
            }
            
            // Calculates the move
            _droneControlling.PDCalculateMove(droneTransform:_initialDroneState);
        
            var x = _droneControlling.h;
            var z = _droneControlling.v;
            
            /*
            var vo = new VO(vehicleTransform:_initialDroneState, 15f);
        
            Collider[] obstacles = Physics.OverlapSphere(transform.position, 20f, LayerMask.GetMask("Obstacle"));
            GameObject[] pedestrians = GameObject.FindGameObjectsWithTag("Searcher");
        
            (finalH, finalV) = vo.GetSafeAcceleration(myTransform:_initialDroneState, currentVelocity:velocity, 
                intendedH:finalH, intendedV:finalV, otherDrones:, pedestrians:pedestrians, staticObstacles:obstacles);
            */
            
            var droneAction = new PacManAction
            {
                Acceleration = new Vector2(x, z), // Controller converts to normalized if magnitude > 1. Magnitude 0.3 guarantees not observed
            };

            Debug.Log($"Drone acceleration: {droneAction.Acceleration.magnitude}, Intended move: {droneAction.Acceleration}, Velocity: {velocity}");
            
            return droneAction;
        }
        
        
        private void OnDrawGizmos()
        {
            if (_waypoints != null && _waypoints.Count > 0)
            {
                Gizmos.color = Color.cyan;
                for (int i = 0; i < _waypoints.Count - 1; i++)
                {
                    Vector3 start = new Vector3(_waypoints[i].position.x, transform.position.y, _waypoints[i].position.y);
                    Vector3 end = new Vector3(_waypoints[i + 1].position.x, transform.position.y, _waypoints[i + 1].position.y);
                    Gizmos.DrawLine(start, end);
                    Gizmos.DrawSphere(start, 0.1f);
                }
                // Draw last waypoint
                Vector3 lastPos = new Vector3(_waypoints[_waypoints.Count - 1].position.x, transform.position.y, _waypoints[_waypoints.Count - 1].position.y);
                Gizmos.DrawSphere(lastPos, 0.15f);
            }

            if (_droneControlling != null && _initialDroneState != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawSphere(
                    new Vector3(_droneControlling.closestPoint.x, _initialDroneState.position.y,
                        _droneControlling.closestPoint.y), 0.2f);
                Gizmos.color = Color.blue;
                Gizmos.DrawSphere(
                    new Vector3(_droneControlling.targetPoint.x, _initialDroneState.position.y,
                        _droneControlling.targetPoint.y), 0.2f);
            }
        }
    }
}