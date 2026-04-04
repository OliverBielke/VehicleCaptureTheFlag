using System.Collections.Generic;
using System.Linq;
using PacMan.Interface.PacMan;
using PacMan.Local;
using Scripts.Map;
using UnityEngine;
using PacMan.Agent.PathFinding;
using PacMan.Agent.PathFollowing;
using PacMan.Agent.BehaviorTreeFolder;
using TMPro;
using UnityEngine;
using Scripts.Map;

namespace PacMan.Agent
{
    public class PacManAIDebugBT : PacManAI
    {
        private bool _hasGoal;
        private Vector3 _goalPosition;
        private List<Node> _waypoints;
        private CGSmoother _pathSmoother;
        private DroneControlling _droneControlling;
        private Transform _initialDroneState;
        private GameObject _currentFoodTarget;
        [Header("Debug")]
        [SerializeField] private bool useManualBlackboard = false;
        [SerializeField] private PacManBlackboard debugBlackboard = new();
        [SerializeField] private TextMeshPro debugText;
        [SerializeField] private bool allowManualOverride = true;
        private BehaviorTree _behaviorTree;
        private AgentMode _currentMode;

        public override void Initialize(MapManager mapManager)
        {
            _agent = GetComponent<PacManAgentManager>();
            _mapManager = mapManager;
            _obstacleMap = ObstacleMap.Initialize(_mapManager, new List<GameObject>(), new Vector3(0.1f, 1f, 0.1f), new Vector3(1f, 1f, 1f));
            // All of the calls below should also work in here. Report it as a bug if you find that some part of the observations is inaccessible during init.
            _hasGoal = false;
            _behaviorTree = new BehaviorTree();
            
            var groundPlane = GameObject.Find("GroundPlane");
            var groundCollider = groundPlane.GetComponent<Collider>();
            _pathSmoother = new CGSmoother(_agent.transform.position.y, groundCollider);
        }

        public override PacManAction Tick()
        {
            _agent.GetTimeRemaining();
            var stepsSinceMatchStart = _agent.GetStepsSinceMatchStart();
            var stepsRemaining = _agent.GetStepsRemaining();
            var score = _agent.GetScore();
            var lastRespawnStep = _agent.GetLastRespawnStep();
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

            
            PacManBlackboard bb;
            if (useManualBlackboard)
            {
                bb = debugBlackboard;
            }
            else
            {
                var visibleEnemies = _agent.GetVisibleEnemyAgents();

                bb = new PacManBlackboard
                {
                    isGhost = _agent.IsGhost(),
                    isScared = _agent.IsScared(),
                    carriedFood = _agent.GetCarriedFoodCount(),
                    visibleEnemyCount = visibleEnemies.Count,
                    enemyVisible = visibleEnemies.Count > 0,
                    shouldReturnHome = _agent.GetCarriedFoodCount() >= 3
                };

                debugBlackboard = bb;
            }


            // Since the RigidBody is updated server side and the client only syncs position, rigidbody.Velocity does not report a velocity
            var velocity = _agent.GetVelocity(); // Use the manager method to get the true velocity from the server
            // friendlyAgentManager.GetVelocity(); // Given the damping, max velocity magnitude is around 2.34
            if (lastRespawnStep != 0 && lastRespawnStep == stepsSinceMatchStart)
            {
                print("Detected respawn step");
            }
            // // replace the human input below with some AI stuff
            var x = 0;
            var z = 0;

            _currentMode = _behaviorTree.Evaluate(bb);

            if (debugText != null)
            {
                debugText.text =
                    $"Mode: {_currentMode}\n" +
                    $"Ghost: {bb.isGhost}\n" +
                    $"Scared: {bb.isScared}\n" +
                    $"Food: {bb.carriedFood}\n" +
                    $"VisibleEnemies: {bb.visibleEnemyCount}\n" +
                    $"Return: {bb.shouldReturnHome}";
            }
            Vector2 accel = Vector2.zero;


            switch (_currentMode)
            {
                case AgentMode.Attack:
                    accel = GetAttackAcceleration(activeFoodPositions);
                    break;

                case AgentMode.ReturnHome:
                    accel = GetReturnHomeAcceleration();
                    break;

                case AgentMode.Defend:
                    accel = GetAttackAcceleration(activeFoodPositions);
                    break;

                case AgentMode.Evade:
                    accel = GetAttackAcceleration(activeFoodPositions);
                    break;

                case AgentMode.Patrol:
                default:
                    accel = GetAttackAcceleration(activeFoodPositions);
                    break;
            }

            // Manual override with keyboard
            Vector2 manualAccel = GetManualAcceleration();
            if (manualAccel != Vector2.zero)
            {
                accel = manualAccel;
            }

            return new PacManAction
            {
                Acceleration = accel
            };
            }
        
        private Vector2 GetAttackAcceleration(List<GameObject> activeFoodPositions)
        {
            if (!_hasGoal)
            {
                var gf = new GoalFinding(agent: _agent);
                var closestFood = gf.GetClosestEatableFood(activeFoodPositions, debug: true);
                _goalPosition = closestFood;

                bool pathOk = MakePath();

                if (!pathOk)
                {
                    _hasGoal = false;
                    return Vector2.zero;
                }

                _hasGoal = true;
            }

            if (_droneControlling == null || _initialDroneState == null)
            {
                _hasGoal = false;
                return Vector2.zero;
            }

            _droneControlling.PDCalculateMove(droneTransform: _initialDroneState);

            var x = _droneControlling.h;
            var z = _droneControlling.v;

            return new Vector2(x, z);
        }

        private Vector2 GetReturnHomeAcceleration()
        {
            // Blue home is left, Red home is right
            float x = CompareTag("Blue") ? -1f : 1f;
            return new Vector2(x, 0f);
        }

        private Vector2 GetPatrolAcceleration()
        {
            // Stay still for now
            return Vector2.zero;
        }

        private Vector2 GetDefendAcceleration()
        {
            var visibleEnemies = _agent.GetVisibleEnemyAgents();
            if (visibleEnemies != null && visibleEnemies.Count > 0)
            {
                Vector3 myPos = transform.localPosition;
                Vector3 enemyPos = visibleEnemies[0].transform.localPosition;
                Vector3 dir = (enemyPos - myPos).normalized;
                return new Vector2(dir.x, dir.z);
            }

            return Vector2.zero;
        }

        private Vector2 GetEvadeAcceleration()
        {
            var visibleEnemies = _agent.GetVisibleEnemyAgents();
            if (visibleEnemies != null && visibleEnemies.Count > 0)
            {
                Vector3 myPos = transform.localPosition;
                Vector3 enemyPos = visibleEnemies[0].transform.localPosition;
                Vector3 dir = (myPos - enemyPos).normalized;
                return new Vector2(dir.x, dir.z);
            }

            return GetReturnHomeAcceleration();
        }
        private Vector2 GetManualAcceleration()
        {
            int x = 0;
            int z = 0;

            bool isBlue = TeamAssignmentUtil.CheckTeam(gameObject) == Team.Blue;
            bool isRed = TeamAssignmentUtil.CheckTeam(gameObject) == Team.Red;

            if ((isBlue && Input.GetKey("w")) || (isRed && Input.GetKey(KeyCode.UpArrow)))
            {
                z = 1;
            }

            if ((isBlue && Input.GetKey("s")) || (isRed && Input.GetKey(KeyCode.DownArrow)))
            {
                z = -1;
            }

            if ((isBlue && Input.GetKey("a")) || (isRed && Input.GetKey(KeyCode.LeftArrow)))
            {
                x = -1;
            }

            if ((isBlue && Input.GetKey("d")) || (isRed && Input.GetKey(KeyCode.RightArrow)))
            {
                x = 1;
            }

            return new Vector2(x, z);
        }


        /// <summary>
        /// Calculates the new path based on the _goalPosition and stores it in _waypoints.
        /// Also initializes _droneControlling. 
        /// </summary>
        private bool MakePath()
        {
            _initialDroneState = _agent.transform;
            var curPos = _initialDroneState.localPosition;

            Astar aStar = new Astar(_obstacleMap);
            List<Vector3> aStarPath = aStar.PlanPathAStar(curPos, _goalPosition);

            if (aStarPath == null || aStarPath.Count < 2)
            {
                Debug.LogWarning("MakePath failed: no valid A* path.");
                _waypoints = null;
                _droneControlling = null;
                return false;
            }

            List<Node> nodes = new();
            foreach (Vector3 pos in aStarPath)
            {
                nodes.Add(new Node(pos.x, pos.z));
            }

            if (nodes.Count < 2)
            {
                Debug.LogWarning("MakePath failed: not enough nodes.");
                _waypoints = null;
                _droneControlling = null;
                return false;
            }

            // _waypoints = _pathSmoother.GetSmoothedPath(nodes);
            _waypoints = nodes;
            if (_waypoints == null || _waypoints.Count < 2)
            {
                Debug.LogWarning("MakePath failed: smoother returned invalid waypoints.");
                _droneControlling = null;
                return false;
            }

            _droneControlling = new DroneControlling(_waypoints, _goalPosition, _initialDroneState);
            return true;
        }
        
        
        private void OnGUI()
        {
            if (!Application.isPlaying)
                return;

            Vector3 worldPos = transform.position + Vector3.up * 2f;
            Vector3 screenPos = Camera.main.WorldToScreenPoint(worldPos);

            if (screenPos.z > 0f)
            {
                float x = screenPos.x;
                float y = Screen.height - screenPos.y;

                GUI.Label(
                    new Rect(x, y, 220f, 120f),
                    $"Mode: {_currentMode}\n" +
                    $"Ghost: {debugBlackboard.isGhost}\n" +
                    $"Food: {debugBlackboard.carriedFood}\n" +
                    $"Enemies: {debugBlackboard.visibleEnemyCount}\n" +
                    $"Return: {debugBlackboard.shouldReturnHome}"
                );
            }
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
