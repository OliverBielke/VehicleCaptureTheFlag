using System.Collections.Generic;
using System.Linq;
using PacMan.Interface.PacMan;
using PacMan.Local;
using PacMan.Agent.PathFinding;
using PacMan.Agent.PathFollowing;
using PacMan.Agent.BehaviorTreeFolder;
using PacMan.Agent.Debugging;
using PacMan.Agent.Map;
using TMPro;
using UnityEngine;
using Scripts.Map;
using PacMan.Agent.EnemyLocalization;
using System.Reflection;
using PacMan.Agent.RoleAssignment;

namespace PacMan.Agent
{        
    public enum StaticRole
    {
        None,
        Attack,
        Defend
    }

    public class PacManAIDebugBT : PacManAI
    {
        public class TrackedEnemyInfo
        {
            public int ServerIndex;
            public Vector3 Position;
            public bool IsGhost;
            public bool IsVisible;
            public bool HasFood;
            public bool HasPosition;
        }

        private bool _hasGoal;
        private Vector3 _goalPosition;
        private List<Node> _waypoints;
        private CGSmoother _pathSmoother;
        private DroneControlling _droneControlling;
        private Transform _initialDroneState;
        private GameObject _currentFoodTarget;
        private BehaviorTree<DefenderBlackboard> _defenderTree;
        private BehaviorTree<AttackerBlackboard> _attackerTree;
        private BTDecision _lastDecision;
        private string _btReason = "-";
        [SerializeField] private bool drawObstacleMap = false;
        [Header("Debug")]
        [SerializeField] private TextMeshPro debugText;
        [SerializeField] private bool allowManualOverride = true;
        [SerializeField] private StaticRole _assignedRole = StaticRole.None;
        [SerializeField] private Vector3 _defenseAnchor;
        [SerializeField] private bool _hasDefenseAnchor = false;
        [SerializeField] private Vector3 _attackAnchor;
        [SerializeField] private bool _hasAttackAnchor = false;
        private MapMiddleAnalyzer _middleAnalyzer;
        private MapMiddleAnalyzer.MiddleInfo _middleInfo;
        [SerializeField] private bool drawMiddle = true;
        [SerializeField] private bool drawAstar = true;
        [Header("Attack Patrol")]
        [SerializeField] private int attackPatrolSwitchSteps = 30;
        [SerializeField] private float attackPatrolOffset = 1.0f;
        [SerializeField] private float attackPatrolArriveDistance = 0.15f;
        private AgentMode _currentMode;
        private AgentMode _previousMode;
        private bool _visualizerLinked = false;
        
        // To track respawns
        private int _previousRespawnStep = -1;

        private const float AnchorReachedDistance = 0.35f;

        public StaticRole AssignedRole => _assignedRole;
        public bool HasAssignedRole => _assignedRole != StaticRole.None;
        public PacManAgentManager AgentManager => _agent;
        public Vector3 DefenseAnchor => _defenseAnchor;
        public bool HasDefenseAnchor => _hasDefenseAnchor;
        public Vector3 AttackAnchor => _attackAnchor;
        public bool HasAttackAnchor => _hasAttackAnchor;

        public void SetAssignedRole(StaticRole role)
        {
            _assignedRole = role;
            Debug.Log($"{name} assigned role: {_assignedRole}");
        }

        public void SetDefenseAnchor(Vector3 anchor)
        {
            _defenseAnchor = anchor;
            _hasDefenseAnchor = true;
        }

        public void ClearDefenseAnchor()
        {
            _hasDefenseAnchor = false;
        }

        public void SetAttackAnchor(Vector3 anchor)
        {
            _attackAnchor = anchor;
            _hasAttackAnchor = true;
        }

        public void ClearAttackAnchor()
        {
            _hasAttackAnchor = false;
        }
        public override void Initialize(MapManager mapManager)
        {
            _agent = GetComponent<PacManAgentManager>();
            _mapManager = mapManager;
            var gridSize = 0.2f;
            _obstacleMap = ObstacleMapV2.Initialize(_mapManager, new List<GameObject>(), new Vector3(gridSize, 1f, gridSize), new Vector3(1f, 1f, 1f));
            // All of the calls below should also work in here. Report it as a bug if you find that some part of the observations is inaccessible during init.
            _hasGoal = false;
            _defenderTree = DefenderTreeFactory.Create();
            _attackerTree = AttackerTreeFactory.Create();
            _middleAnalyzer = new MapMiddleAnalyzer(_obstacleMap);
            _middleInfo = _middleAnalyzer.Analyze();

            // Debug.Log($"Detected lanes: {_middleInfo.LaneCount}");
            // foreach (var lane in MapMiddleAnalyzer.GetLanesOrdered(_middleInfo))
            // {
            //     Debug.Log($"{lane.Label} | z [{lane.MinZ}, {lane.MaxZ}] | width={lane.WidthCells} | major={lane.IsMajor}");
            // }
            var groundPlane = GameObject.Find("GroundPlane");
            var groundCollider = groundPlane.GetComponent<Collider>();
            RoleAssigner.Instance?.RegisterAgent(this);
            
            // Set the initial respawn step
            if (_agent != null) _previousRespawnStep = _agent.GetLastRespawnStep();
            
        }

        private void OnDisable()
        {
            RoleAssigner.Instance?.UnregisterAgent(this);
        }

        public override PacManAction Tick()
        {
            // Respawn Detection
            var currentRespawnStep = _agent.GetLastRespawnStep();
            if (currentRespawnStep != _previousRespawnStep)
            {
                // The agent just died and respawned. Reset the path!
                ClearCurrentPath();
                _previousRespawnStep = currentRespawnStep;
            }
            
            _agent.GetTimeRemaining();
            _agent.GetScore();

            Vector3 velocity = _agent.GetVelocity();

            _lastDecision = EvaluateCurrentRoleTree();
            _currentMode = _lastDecision.Mode;

            if (_currentMode != _previousMode)
            {
                ClearCurrentPath();
            }

            Vector2 accel = ExecuteDecision(_lastDecision, velocity);

            Vector2 manualAccel = GetManualAcceleration();
            if (manualAccel != Vector2.zero)
            {
                accel = manualAccel;
            }

            _previousMode = _currentMode;

            return new PacManAction
            {
                Acceleration = accel
            };
        }
        private BTDecision EvaluateCurrentRoleTree()
        {
            switch (_assignedRole)
            {
                case StaticRole.Defend:
                {
                    DefenderBlackboard bb = BuildDefenderBlackboard();
                    _btReason = bb.debugReason;
                    return _defenderTree.Evaluate(bb);
                }

                case StaticRole.Attack:
                {
                    AttackerBlackboard bb = BuildAttackerBlackboard();
                    _btReason = bb.debugReason;
                    return _attackerTree.Evaluate(bb);
                }

                default:
                    _btReason = "No assigned role";
                    return BTDecision.Running(AgentMode.Patrol, "NoRole");
            }
        }
        private Vector2 GetAttackAcceleration(List<GameObject> activeFoodPositions)
        {
            // 1. VALIDATE EXISTING GOAL
            if (_hasGoal)
            {
                // Condition A: Did we reach the goal?
                if (Vector3.Distance(transform.localPosition, _goalPosition) < 0.2f)
                {
                    _hasGoal = false;
                }
                // Condition B: Was our targeted food eaten by someone else?
                else if (_currentFoodTarget != null && !_currentFoodTarget.activeSelf)
                {
                    _hasGoal = false;
                }
            }
            
            // FIND NEW GOAL IF NEEDED
            if (!_hasGoal)
            {
                var gf = new GoalFinding(agent: _agent);
                var closestFood = gf.GetClosestEatableFood(activeFoodPositions, debug: true);
                _goalPosition = closestFood;

                // Map the returned Vector3 back to the actual GameObject so we can track if it gets deactivated
                _currentFoodTarget = activeFoodPositions.FirstOrDefault(f => f.transform.position == closestFood);
                
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
            // 1. Identify team to determine the correct middle line points
            bool isBlue = TeamAssignmentUtil.CheckTeam(gameObject) == Team.Blue;
            List<Vector3> homePoints = isBlue ? _middleInfo.MiddleLeftLocalPositions : _middleInfo.MiddleRightLocalPositions;

            // Fallback if the MapMiddle analyzer failed or hasn't run
            if (homePoints == null || homePoints.Count == 0)
            {
                return new Vector2(isBlue ? -1f : 1f, 0f);
            }

            // 2. Find the closest home point on the middle line
            Vector3 currentPos = transform.localPosition;
            Vector3 closestHomePoint = homePoints[0];
            float minDistance = float.MaxValue;

            foreach (var point in homePoints)
            {
                float dist = Vector3.Distance(currentPos, point);
                if (dist < minDistance)
                {
                    minDistance = dist;
                    closestHomePoint = point;
                }
            }

            // 3. Set the goal and generate the path
            // We check the distance to ensure we recalculate if the goal shifts (e.g., agent was previously tracking a food item)
            if (!_hasGoal || Vector3.Distance(_goalPosition, closestHomePoint) > 1.0f)
            {
                _goalPosition = closestHomePoint;
                bool pathOk = MakePath();

                // If pathfinding fails (e.g., A* returns < 2 nodes because we are already touching the point)
                // Fall back to moving horizontally so the agent crosses the line to score
                if (!pathOk)
                {
                    _hasGoal = false;
                    return new Vector2(isBlue ? -1f : 1f, 0f);
                }

                _hasGoal = true;
            }

            // 4. Follow the calculated path
            if (_droneControlling == null || _initialDroneState == null)
            {
                _hasGoal = false;
                return Vector2.zero;
            }

            _droneControlling.PDCalculateMove(droneTransform: _initialDroneState);
            return new Vector2(_droneControlling.h, _droneControlling.v);
        }

        private Vector2 GetPatrolAcceleration()
        {
            // Stay still for now
            return Vector2.zero;
        }

        private Vector2 GetDefendAcceleration()
        {
            // Remove any previous goal
            _hasGoal = false;
            
            var visibleEnemies = _agent.GetVisibleEnemyAgents();

            // 1) If an enemy is visible, chase it
            if (visibleEnemies != null && visibleEnemies.Count > 0)
            {
                Vector3 myPos = transform.localPosition;
                Vector3 enemyPos = visibleEnemies[0].transform.localPosition;
                Vector3 dir = (enemyPos - myPos).normalized;

                ClearCurrentPath(); // stop following anchor path while actively chasing
                return new Vector2(dir.x, dir.z);
            }

            // 2) Otherwise go to the assigned defense anchor
            if (!_hasDefenseAnchor)
                return Vector2.zero;

            Vector3 myLocalPos = transform.localPosition;

            // If already close enough to the anchor, stay there
            if (Vector3.Distance(myLocalPos, _defenseAnchor) <= AnchorReachedDistance)
            {
                ClearCurrentPath();
                return Vector2.zero;
            }

            // Rebuild path if needed or if the goal changed
            bool needNewPath = !_hasGoal || Vector3.Distance(_goalPosition, _defenseAnchor) > 0.05f;

            if (needNewPath)
            {
                _goalPosition = _defenseAnchor;

                bool pathOk = MakePath();
                if (!pathOk)
                {
                    ClearCurrentPath();
                    return Vector2.zero;
                }

                _hasGoal = true;
            }

            if (_droneControlling == null || _initialDroneState == null)
            {
                ClearCurrentPath();
                return Vector2.zero;
            }

            _droneControlling.PDCalculateMove(droneTransform: _initialDroneState);

            return new Vector2(_droneControlling.h, _droneControlling.v);
        }

        private Vector2 GetEvadeAcceleration(Vector3 velocity)
        {
            
            
            var visibleEnemies = _agent.GetVisibleEnemyAgents();
            if (visibleEnemies != null && visibleEnemies.Count > 0)
            {
                
                Vector3 myPos = transform.localPosition;
                Vector3 enemyPos = visibleEnemies[0].transform.localPosition;
                Vector3 dir = (myPos - enemyPos).normalized;
                var intendedH = dir.x;
                var intendedV = dir.z;
                
                var vo = new VO(transform, maxAcceleration:15f);

                float x;
                float z;

                // Friends + power pill
                var lowRiskObstacles = _agent.GetFriendlyAgents().Where(a => a != _agent) // Exclude self
                    .Select(agent => agent.gameObject)
                    .ToList();
                var powerPills = _agent.GetCapsuleObjects();
                lowRiskObstacles.AddRange(powerPills);
                var obstacles = Physics.OverlapSphere(transform.position, 20f, LayerMask.GetMask("Obstacle"));
            
                var enemyGhosts = _agent.GetVisibleEnemyAgents().Where(a => a.IsGhost())
                    .Select(e => e.gameObject).ToList();
                
                (x, z) = vo.GetSafeAcceleration(myTransform: transform, currentVelocity: velocity, 
                    intendedH:intendedH, intendedV:intendedV, highRiskDrones:enemyGhosts, lowRiskDrones:lowRiskObstacles,
                    staticObstacles:obstacles);
                
                return new Vector2(x, z);
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
        /// Get closest visible enemy PacMan and Ghost distances. 
        /// </summary>
        /// <param name="visibleEnemyAgents">List of the visible enemies. </param>
        /// <param name="enemyGhostDistance">Distance to closest visible enemy ghost. </param>
        /// <param name="enemyPacManDistance">Distance to closest visible enemy Pac Man. </param>
        private void GetClosestEnemies(List<PacManAgentManager> visibleEnemyAgents, 
            out float enemyGhostDistance, out float enemyPacManDistance)
        {
            enemyGhostDistance = float.MaxValue;
            enemyPacManDistance = float.MaxValue;
            foreach (var enemy in visibleEnemyAgents)
            {
                if (enemy.isGhost)
                {
                    //Closest distance
                    enemyGhostDistance = Mathf.Min(enemyGhostDistance, Vector3.Distance(enemy.transform.position, transform.position));
                }
                else
                {
                    //Closest distance
                    enemyPacManDistance = Mathf.Min(enemyPacManDistance, Vector3.Distance(enemy.transform.position, transform.position));
                }
            }
        }
        
        
        /// <summary>
        /// Calculates the new path based on the _goalPosition and stores it in _waypoints.
        /// Also initializes _droneControlling. 
        /// </summary>
        private bool MakePath()
        {
            _initialDroneState = _agent.transform;
            var curPos = _initialDroneState.localPosition;
            var dynamicEnemyObstacles = GetTrackedEnemies()
                .Where(enemy => enemy != null && enemy.HasPosition)
                .Select(enemy => enemy.Position)
                .ToList();

            var startTrav = _obstacleMap.GetLocalPointTraversibility(curPos);
            var goalTrav = _obstacleMap.GetLocalPointTraversibility(_goalPosition);

            // Debug.Log(
            //     $"MakePath() | agent={name} | mode={_currentMode} | " +
            //     $"start={curPos} | goal={_goalPosition} | " +
            //     $"startTrav={startTrav} | goalTrav={goalTrav} | " +
            //     $"hasDefenseAnchor={_hasDefenseAnchor} | defenseAnchor={_defenseAnchor}"
            // );

            Astar aStar = new Astar(_obstacleMap, dynamicEnemyObstacles);
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

            _waypoints = nodes;
            _droneControlling = new DroneControlling(_waypoints, _goalPosition, _initialDroneState);
            return true;
        }

        private DefenderBlackboard BuildDefenderBlackboard()
        {
            DefenderBlackboard bb = new DefenderBlackboard();

            Vector3 myPos = transform.localPosition;
            var defendAssignment = RoleAssigner.Instance?.DefendManager?.GetAssignment(this);

            if (defendAssignment != null)
            {
                bb.enemyPacmanIntruderSuspected = true;
                bb.suspectedIntruderPosition = defendAssignment.TargetPosition;
                bb.debugReason = defendAssignment.Reason;
            }

            bb.enemyLikelyCrossingMyLane = false;
            bb.predictedCrossingPoint = Vector3.zero;

            bb.safeMiddlePillsAvailable = false;
            bb.safeMiddlePillPosition = Vector3.zero;

            bb.formationPoint = _hasDefenseAnchor ? _defenseAnchor : myPos;
            bb.dropZonePoint = _hasDefenseAnchor ? _defenseAnchor : myPos;

            bb.outsideDefensiveZone =
                _hasDefenseAnchor &&
                Vector3.Distance(myPos, _defenseAnchor) > 1.25f;

            if (string.IsNullOrEmpty(bb.debugReason))
                bb.debugReason = "Default defend state";

            return bb;
        }
        private AttackerBlackboard BuildAttackerBlackboard()
        {
            AttackerBlackboard bb = new AttackerBlackboard();

            Vector3 myPos = transform.localPosition;
            var trackedEnemies = GetTrackedEnemies();
            var activeFood = _agent.GetFoodObjects().FindAll(f => f.activeSelf &&
                                                TeamAssignmentUtil.CheckTeam(f) != TeamAssignmentUtil.CheckTeam(gameObject));

            float closestGhostDist = float.MaxValue;
            TrackedEnemyInfo closestGhost = null;

            if (trackedEnemies != null)
            {
                foreach (var enemy in trackedEnemies)
                {
                    if (enemy == null || !enemy.HasPosition || !enemy.IsGhost)
                        continue;

                    float dist = Vector3.Distance(myPos, enemy.Position);
                    if (dist < closestGhostDist)
                    {
                        closestGhostDist = dist;
                        closestGhost = enemy;
                    }
                }
            }

            bool ghostNearby = closestGhost != null && closestGhostDist < 4f;
            bool carryingFood = _agent.GetCarriedFoodCount() >= 1;

            bb.shouldReturnHome = ghostNearby && carryingFood;

            Vector3 homeTarget = GetClosestHomePoint();
            bb.homeTargetPosition = homeTarget;

            var attackAssignment = RoleAssigner.Instance?.AttackManager?.GetAssignment(this, activeFood);

            if (attackAssignment?.FoodTarget != null && !ghostNearby)
            {
                bb.safeEnemyPillsAvailable = true;
                bb.enemyPillTargetPosition = attackAssignment.FoodTarget.transform.localPosition;
            }

            bb.safeMiddlePillsAvailable = false;
            bb.middlePillTargetPosition = Vector3.zero;

            Vector3 attackAnchor = _hasAttackAnchor ? _attackAnchor : myPos;
            bb.attackPositionTarget = attackAnchor;
            bb.patrolTargetPosition = GetAttackPatrolPoint(attackAnchor);

            bb.outsideAttackZone =
                _hasAttackAnchor &&
                Vector3.Distance(myPos, _attackAnchor) > 1.5f;

            if (bb.shouldReturnHome)
                bb.debugReason = "Threat nearby while carrying food";
            else if (bb.safeEnemyPillsAvailable)
                bb.debugReason = attackAssignment?.Reason ?? "Safe enemy pill available";
            else if (bb.outsideAttackZone)
                bb.debugReason = "Outside attack zone";
            else
                bb.debugReason = "Patrol attack zone";

            return bb;
        }

        public List<TrackedEnemyInfo> GetTrackedEnemies()
        {
            var tracked = new Dictionary<int, TrackedEnemyInfo>();
            var tracker = EnemyTrackerManager.Instance;
            var observations = _agent.GetEnemyObservations();

            if (observations.Observations != null)
            {
                foreach (var observation in observations.Observations)
                {
                    if (observation.ServerIndex < 0)
                        continue;

                    Vector3 estimatedPosition = Vector3.zero;
                    bool hasEstimate = tracker != null &&
                                       tracker.TryGetEstimate(observation.ServerIndex, out estimatedPosition);
                    bool hasObservationPosition = observation.Visible || observation.Position != Vector3.zero;

                    if (!hasEstimate && !hasObservationPosition)
                        continue;

                    tracked[observation.ServerIndex] = new TrackedEnemyInfo
                    {
                        ServerIndex = observation.ServerIndex,
                        Position = hasEstimate ? estimatedPosition : observation.Position,
                        IsGhost = observation.IsGhost,
                        IsVisible = observation.Visible,
                        HasFood = observation.HasFood,
                        HasPosition = true
                    };
                }
            }

            var visibleEnemies = _agent.GetVisibleEnemyAgents();
            if (visibleEnemies != null)
            {
                foreach (var enemy in visibleEnemies)
                {
                    if (enemy == null || enemy.serverIndex < 0)
                        continue;

                    Vector3 position = enemy.transform.localPosition;
                    if (tracker != null && tracker.TryGetEstimate(enemy.serverIndex, out var estimatedPosition))
                        position = estimatedPosition;

                    tracked[enemy.serverIndex] = new TrackedEnemyInfo
                    {
                        ServerIndex = enemy.serverIndex,
                        Position = position,
                        IsGhost = enemy.IsGhost(),
                        IsVisible = true,
                        HasFood = enemy.GetCarriedFoodCount() > 0,
                        HasPosition = true
                    };
                }
            }

            return tracked.Values.ToList();
        }

        private Vector3 GetAttackPatrolPoint(Vector3 anchor)
        {
            if (!_hasAttackAnchor)
                return anchor;

            int switchSteps = Mathf.Max(1, attackPatrolSwitchSteps);
            int stepBucket = Mathf.FloorToInt(_agent.GetStepsSinceMatchStart() / (float)switchSteps);
            bool usePositiveOffset = ((stepBucket + Mathf.Abs(GetInstanceID())) % 2) == 0;
            float zOffsetMagnitude = Mathf.Max(0.1f, attackPatrolOffset);
            float zOffset = usePositiveOffset ? zOffsetMagnitude : -zOffsetMagnitude;

            Vector3 patrolPoint = anchor + new Vector3(0f, 0f, zOffset);

            if (_obstacleMap.GetLocalPointTraversibility(patrolPoint) == ObstacleMapV2.Traversability.Free)
                return patrolPoint;

            patrolPoint = anchor + new Vector3(0f, 0f, -zOffset);
            if (_obstacleMap.GetLocalPointTraversibility(patrolPoint) == ObstacleMapV2.Traversability.Free)
                return patrolPoint;

            Vector3 deeperOwnSide = anchor + new Vector3(
                TeamAssignmentUtil.CheckTeam(gameObject) == Team.Blue ? -0.75f : 0.75f,
                0f,
                0f);

            if (_obstacleMap.GetLocalPointTraversibility(deeperOwnSide) == ObstacleMapV2.Traversability.Free)
                return deeperOwnSide;

            return anchor;
        }

        private Vector3 GetClosestHomePoint()
        {
            bool isBlue = TeamAssignmentUtil.CheckTeam(gameObject) == Team.Blue;
            List<Vector3> homePoints = isBlue
                ? _middleInfo.MiddleLeftLocalPositions
                : _middleInfo.MiddleRightLocalPositions;

            if (homePoints == null || homePoints.Count == 0)
            {
                return transform.localPosition;
            }

            Vector3 currentPos = transform.localPosition;
            Vector3 closestHomePoint = homePoints[0];
            float minDistance = float.MaxValue;

            foreach (var point in homePoints)
            {
                float dist = Vector3.Distance(currentPos, point);
                if (dist < minDistance)
                {
                    minDistance = dist;
                    closestHomePoint = point;
                }
            }

            return closestHomePoint;
        }
        private Vector2 ExecuteDecision(BTDecision decision, Vector3 velocity)
        {
            if (decision == null)
                return Vector2.zero;

            switch (decision.DebugLabel)
            {
                // Defender
                case "InterceptIntruder":
                    return ExecuteInterceptIntruder(decision);

                case "BlockCrossing":
                    return ExecuteBlockCrossing(decision);

                case "CollectSafeMiddlePills":
                    return ExecuteDefenderCollectSafeMiddlePills(decision);

                case "MoveToFormation":
                    return ExecuteMoveToFormation(decision);

                case "HoldDropZone":
                    return ExecuteHoldDropZone(decision);

                // Attacker
                case "ReturnHome":
                    return ExecuteReturnHome(decision);

                case "CollectEnemyPills":
                    return ExecuteCollectEnemyPills(decision);

                case "CollectSafeMiddlePills_Attack":
                    return ExecuteAttackerCollectSafeMiddlePills(decision);

                case "MoveToAttackPosition":
                    return ExecuteMoveToAttackPosition(decision);

                case "PatrolAttackZone":
                    return ExecutePatrolAttackZone(decision);

                case "Evade":
                    return ExecuteEvade(decision, velocity);

                default:
                    ClearCurrentPath();
                    return Vector2.zero;
            }
        }
        private Vector2 MoveToTarget(Vector3 target, float arriveDistance = 0.35f)
        {
            Vector3 myLocalPos = transform.localPosition;

            if (Vector3.Distance(myLocalPos, target) <= arriveDistance)
            {
                ClearCurrentPath();
                return Vector2.zero;
            }

            bool needNewPath = !_hasGoal || Vector3.Distance(_goalPosition, target) > 0.05f;

            if (needNewPath)
            {
                _goalPosition = target;

                bool pathOk = MakePath();
                if (!pathOk)
                {
                    ClearCurrentPath();
                    return Vector2.zero;
                }

                _hasGoal = true;
            }

            if (_droneControlling == null || _initialDroneState == null)
            {
                ClearCurrentPath();
                return Vector2.zero;
            }

            _droneControlling.PDCalculateMove(droneTransform: _initialDroneState);

            var moveVector = GetSafeAcceleration();
                
            return moveVector;
        }


        /// <summary>
        /// Returns a safe acceleration based on the _droneControlling output, surrounding obstacles and
        /// other things based on if the agent is ghost or pacman. 
        /// </summary>
        /// <returns></returns>
        private Vector2 GetSafeAcceleration()
        {
            var vo = new VO(transform, maxAcceleration:15f);
            
            // Friends + power pill
            var lowRiskObstacles = _agent.GetFriendlyAgents().Where(a => a != _agent) // Exclude self
                .Select(agent => agent.gameObject)
                .ToList();
            var powerPills = _agent.GetCapsuleObjects();
            lowRiskObstacles.AddRange(powerPills);
            var obstacles = Physics.OverlapSphere(transform.position, 20f, LayerMask.GetMask("Obstacle"));
            
            var enemyGhosts = _agent.GetVisibleEnemyAgents().Where(a => a.IsGhost())
                .Select(e => e.gameObject).ToList();
            
            float safeH;
            float safeV;
            (safeH,safeV) = vo.GetSafeAcceleration(myTransform:transform, currentVelocity:_agent.GetVelocity(), 
                intendedH:_droneControlling.h, intendedV:_droneControlling.v,highRiskDrones:enemyGhosts, 
                lowRiskDrones:lowRiskObstacles, staticObstacles:obstacles);
            
            return new Vector2(safeH, safeV);
        }
        
        
        private Vector2 ExecuteInterceptIntruder(BTDecision decision)
        {
            if (decision == null || !decision.HasTarget)
            {
                ClearCurrentPath();
                return Vector2.zero;
            }

            return MoveToTarget(decision.TargetPosition, arriveDistance: 0.25f);
        }
        private Vector2 ExecuteBlockCrossing(BTDecision decision)
        {
            if (decision == null || !decision.HasTarget)
            {
                ClearCurrentPath();
                return Vector2.zero;
            }

            return MoveToTarget(decision.TargetPosition, arriveDistance: 0.35f);
        }
        private Vector2 ExecuteDefenderCollectSafeMiddlePills(BTDecision decision)
        {
            if (decision == null || !decision.HasTarget)
            {
                ClearCurrentPath();
                return Vector2.zero;
            }

            return MoveToTarget(decision.TargetPosition, arriveDistance: 0.25f);
        }

        private Vector2 ExecuteMoveToFormation(BTDecision decision)
        {
            if (decision == null || !decision.HasTarget)
            {
                ClearCurrentPath();
                return Vector2.zero;
            }

            return MoveToTarget(decision.TargetPosition, arriveDistance: 0.35f);
        }
        private Vector2 ExecuteHoldDropZone(BTDecision decision)
        {
            if (decision == null || !decision.HasTarget)
            {
                ClearCurrentPath();
                return Vector2.zero;
            }

            return MoveToTarget(decision.TargetPosition, arriveDistance: 0.30f);
        }
        private Vector2 ExecuteReturnHome(BTDecision decision)
        {
            if (decision == null || !decision.HasTarget)
            {
                ClearCurrentPath();
                return Vector2.zero;
            }

            return MoveToTarget(decision.TargetPosition, arriveDistance: 0.30f);
        }
        private Vector2 ExecuteCollectEnemyPills(BTDecision decision)
        {
            if (decision == null || !decision.HasTarget)
            {
                ClearCurrentPath();
                return Vector2.zero;
            }

            return MoveToTarget(decision.TargetPosition, arriveDistance: 0.20f);
        }
        private Vector2 ExecuteAttackerCollectSafeMiddlePills(BTDecision decision)
        {
            if (decision == null || !decision.HasTarget)
            {
                ClearCurrentPath();
                return Vector2.zero;
            }

            return MoveToTarget(decision.TargetPosition, arriveDistance: 0.25f);
        }
        private Vector2 ExecuteMoveToAttackPosition(BTDecision decision)
        {
            if (decision == null || !decision.HasTarget)
            {
                ClearCurrentPath();
                return Vector2.zero;
            }

            return MoveToTarget(decision.TargetPosition, arriveDistance: 0.35f);
        }
        private Vector2 ExecutePatrolAttackZone(BTDecision decision)
        {
            if (decision == null || !decision.HasTarget)
            {
                ClearCurrentPath();
                return Vector2.zero;
            }

            return MoveToTarget(decision.TargetPosition, arriveDistance: attackPatrolArriveDistance);
        }
        private Vector2 ExecuteEvade(BTDecision decision, Vector3 velocity)
        {
            return GetEvadeAcceleration(velocity);
        }
        private void OnGUI()
        {
            if (!Application.isPlaying)
                return;

            Vector3 worldPos = transform.position + Vector3.up * 2f;
            Vector3 screenPos = Camera.main.WorldToScreenPoint(worldPos);

            if (screenPos.z <= 0f)
                return;

            float x = screenPos.x;
            float y = Screen.height - screenPos.y;

            string btLabel = _lastDecision != null ? _lastDecision.DebugLabel : "-";
            string targetText = (_lastDecision != null && _lastDecision.HasTarget)
                ? _lastDecision.TargetPosition.ToString("F2")
                : "-";

            GUI.Label(
                new Rect(x, y, 280f, 180f),
                $"Role: {_assignedRole}\n" +
                $"Mode: {_currentMode}\n" +
                $"BT: {btLabel}\n" +
                $"Reason: {_btReason}\n" +
                $"Has goal: {_hasGoal}\n" +
                $"Has target: {(_lastDecision != null && _lastDecision.HasTarget)}\n" +
                $"Target: {targetText}"
            );
        }
        
        private void ClearCurrentPath()
        {
            _hasGoal = false;
            _waypoints = null;
            _droneControlling = null;
        }
        private void OnDrawGizmos()
        {
            MapEditing.DrawObstacleMap(transform, _obstacleMap, drawObstacleMap);
            if (drawAstar)
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

            if (drawMiddle)
            {
                DrawMiddleGizmos();
            }
        }
        private void DrawMiddleGizmos()
        {
            if (_middleInfo.MiddleLeftLocalPositions == null || _middleInfo.MiddleRightLocalPositions == null)
                return;

            Gizmos.color = Color.cyan;
            foreach (var p in _middleInfo.MiddleLeftLocalPositions)
            {
                Vector3 wp = transform.parent != null ? transform.parent.TransformPoint(p) : p;
                Gizmos.DrawSphere(wp + Vector3.up * 0.15f, 0.07f);
            }

            Gizmos.color = Color.magenta;
            foreach (var p in _middleInfo.MiddleRightLocalPositions)
            {
                Vector3 wp = transform.parent != null ? transform.parent.TransformPoint(p) : p;
                Gizmos.DrawSphere(wp + Vector3.up * 0.15f, 0.07f);
            }
                        if (_middleInfo.Lanes == null || _middleInfo.Lanes.Count == 0)
                return;

            foreach (var lane in _middleInfo.Lanes)
            {
                Gizmos.color = lane.IsMajor ? Color.white : Color.gray;

                foreach (var p in lane.LeftLocalPositions)
                {
                    Vector3 wp = transform.parent != null ? transform.parent.TransformPoint(p) : p;
                    Gizmos.DrawSphere(wp + Vector3.up * 0.15f, 0.05f);
                }

                foreach (var p in lane.RightLocalPositions)
                {
                    Vector3 wp = transform.parent != null ? transform.parent.TransformPoint(p) : p;
                    Gizmos.DrawSphere(wp + Vector3.up * 0.15f, 0.05f);
                }

                Vector3 centerWorld = transform.parent != null
                    ? transform.parent.TransformPoint(lane.MidCenterLocal)
                    : lane.MidCenterLocal;

                Gizmos.color = lane.IsMajor ? Color.yellow : Color.red;
                Gizmos.DrawSphere(centerWorld + Vector3.up * 0.3f, 0.12f);
                Gizmos.DrawLine(centerWorld + Vector3.up * 0.05f, centerWorld + Vector3.up * 0.45f);

            #if UNITY_EDITOR
                    UnityEditor.Handles.color = lane.IsMajor ? Color.yellow : Color.red;
                    UnityEditor.Handles.Label(
                        centerWorld + Vector3.up * 0.5f,
                        $"{lane.Label}\nwidth={lane.WidthCells}\nmajor={lane.IsMajor}"
                    );
            #endif
            }
        }
    }
}
