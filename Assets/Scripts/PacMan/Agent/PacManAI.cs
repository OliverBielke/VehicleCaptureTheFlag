using System.Collections.Generic;
using System.Linq;
using PacMan.Interface.PacMan;
using PacMan.Local;
using PacMan.Agent.PathFinding;
using PacMan.Agent.PathFollowing;
using PacMan.Agent.BehaviorTreeFolder;
using PacMan.Agent.Map;
using UnityEngine;
using Scripts.Map;
using PacMan.Agent.EnemyLocalization;
using PacMan.Agent.RoleAssignment;
using PacMan.Agent.Debugging;
using PacMan.Game;

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
        [SerializeField] private StaticRole _assignedRole = StaticRole.None;
        [SerializeField] private Vector3 _defenseAnchor;
        [SerializeField] private bool _hasDefenseAnchor = false;
        [SerializeField] private Vector3 _attackAnchor;
        [SerializeField] private bool _hasAttackAnchor = false;
        private MapMiddleAnalyzer _middleAnalyzer;
        private MapMiddleAnalyzer.MiddleInfo _middleInfo;
        [Header("Attack Patrol")]
        [SerializeField] private int attackPatrolSwitchSteps = 30;
        [SerializeField] private float attackPatrolOffset = 1.0f;
        [SerializeField] private float attackPatrolArriveDistance = 0.15f;
         [Header("Power Play")]
         [SerializeField] private float powerPillBaseTimeSeconds = 25f;
         [SerializeField] private float powerPillExtraTimeSeconds = 20f;
         [SerializeField] private int poweredReturnFoodThreshold = 6;
        [SerializeField] private float nextCapsuleGrabLeadTime = 0.15f;
        [SerializeField] private float consumedCapsuleContactDistance = 0.25f;
        private float _cachedCapsuleRushTimeThreshold = 25f;
        [Header("Retreat")]
        [SerializeField] private float returnHomeOwnSideOffset = 1.2f;
        [SerializeField] private float returnHomeReleaseOwnSideDistance = 1.2f;
        [SerializeField] private int attackerGhostDangerStartFoodThreshold = 5;
        [SerializeField] private int attackerForcedReturnFoodThreshold = 9;
        [SerializeField] private float lateGameReturnHomeBaseSeconds = 20f;
        [SerializeField] private float lateGameReturnHomeBufferSeconds = 6f;
        [SerializeField] private float lateGameReturnHomeHorizontalSpeed = 2.34f;
        [SerializeField] private float friendlyCapsuleObstacleInflation = 0.4f;
        [SerializeField] private float capsuleGoalIgnoreRadius = 0.6f;
        [SerializeField] private float baseGhostDangerDistance = 0f;
        [SerializeField] private float maxGhostDangerDistance = 4.5f;
        [SerializeField] private float ghostDangerHysteresisDistance = 1.0f;
        [SerializeField] private int returnHomeLaneDangerSampleCount = 8;
        [SerializeField] private int returnHomeRepathIntervalSteps = 8;
        [SerializeField] private float voronoiPathDangerPenaltyMultiplier = 30f;
        [SerializeField] private float defenderPurePursuitSwitchDistance = 2f;
        [Header("Voronoi Safety")]
        private float _voronoiSafetyThreshold = 0.5f;
        private int _voronoiUpdateIntervalSteps = 3;
        [Header("Defense Mirror")]
        [SerializeField] private float defenderMirrorEnemySideDepth = 2.0f;
        [SerializeField] private float defenderMirrorLanePadding = 0.5f;
        [SerializeField] private int defenderMirrorRepathIntervalSteps = 1;
        [Header("Defense Middle Pills")]
        [SerializeField] private float defenderSafeMiddleDepth = 2.0f;
        [SerializeField] private float defenderSafeMiddleLanePadding = 0.4f;
        [SerializeField] private float defenderSafeMiddleEnemyClearance = 4f;
        [Header("Defense Lane Guard")]
        [SerializeField] private int defenderLaneFoodPileHoldThreshold = 15;
        [SerializeField] private float defenderLaneFoodPileRadius = 7f;
        [SerializeField] private float defenderLaneFoodPileChainRadius = 1.5f;
        [SerializeField] private float defenderLaneFoodPileIntruderRadius = 3f;
        [SerializeField] private bool drawDefenderLaneFoodPileRadius = true;
        [Header("Path Stability")]
        [SerializeField] private int minStepsBetweenRepaths = 12;
        [SerializeField] private float retargetDistanceThreshold = 0.75f;
        [SerializeField] private float targetLockDistance = 0.35f;
        [SerializeField] private int pillRepathIntervalSteps = 35;
        [SerializeField] private int pillUnsafeCellRetryThreshold = 6;
        [SerializeField] private int pillCandidateAttempts = 1;
        [SerializeField] private int failedPathRetryCooldownSteps = 8;
        [SerializeField] private int pillUnsafeRetargetCooldownSteps = 18;
        [SerializeField] private float pillDangerSwitchThreshold = 0.15f;
        [SerializeField] private float pillDistanceSwitchThreshold = 1.0f;
        [SerializeField] private int capsuleRepathIntervalSteps = 35;
        [Header("Teammate Yield")]
        [SerializeField] private float teammateYieldDetectDistance = 0.75f;
        [SerializeField] private int teammateYieldBackoffSteps = 8;
        [SerializeField] private int teammateYieldObstacleSteps = 20;
        [SerializeField] private int teammateYieldRetriggerCooldownSteps = 12;
        [SerializeField] private float teammateYieldGoalIgnoreRadius = 0.5f;
        [SerializeField] private float teammateYieldObstacleInflation = 1f;
        [SerializeField] private float teammateYieldSettledTargetDistance = 0.45f;
        [SerializeField] private float teammateYieldReleaseDistance = 1.1f;
        private AgentMode _currentMode;
        private AgentMode _previousMode;
        private bool _visualizerLinked = false;
        
        private VoronoiPartitioning _voronoiPartitioning;
        private Dictionary<Vector2Int, VoronoiCellData> _currentVoronoi;
        private static readonly Dictionary<Team, List<Vector3>> ConsumedEnemyCapsulesByTeam = new();
        private static readonly Dictionary<int, FoodSpawnInfo> FoodSpawnInfoById = new();

        private struct FoodSpawnInfo
        {
            public Vector3 SpawnLocalPosition;
        }

        // To track respawns
        private int _previousRespawnStep = -1;
        private int _lastPathPlanStep = -99999;
        private int _lastFailedPathPlanStep = -99999;
        private int _lastKnownRespawnStep = -1;
        private bool _attackerThreatRetreatActive = false;
        private int _previousCarriedFoodCount = 0;
        private bool _attackerRegroupAfterReturnHome = false;
        private int _lastPlannedUnsafeCellCount = 0;
        private int _lastVoronoiUpdateStep = -99999;
        private Vector3 _lastPlannedGoalPosition = Vector3.zero;
        private int _foodTargetUnsafeRetargetBlockedUntilStep = -99999;
        private bool _hasLatchedHomeTarget = false;
        private Vector3 _latchedHomeTarget = Vector3.zero;
        private int _teammateYieldBackoffUntilStep = -1;
        private int _teammateYieldObstacleUntilStep = -1;
        private int _teammateYieldRetriggerBlockedUntilStep = -1;
        private Vector3 _teammateYieldObstaclePosition = Vector3.zero;
        private Vector2 _teammateYieldBackoffAcceleration = Vector2.zero;
        private bool _teammateYieldWaitingForSeparation = false;
        private static GUIStyle _agentHudStyle;

        // Fine grid is 0.2 and Voronoi grid is 1.0, so each coarse cell spans 5x5 fine cells.
        private const int VoronoiCellScaleFactor = 5; // fine 0.2 grid to coarse 1.0 grid

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
            _obstacleMap = ObstacleMapV2.Initialize(_mapManager, new List<GameObject>(), new Vector3(gridSize, 1f, gridSize));
            
            //Make all the classes have the same obstacle map
            if (EnemyTrackerManager.Instance != null) EnemyTrackerManager.Instance.SetObstacleMap(_obstacleMap);
            if (RoleAssigner.Instance != null) RoleAssigner.Instance.SetObstacleMap(_obstacleMap);
            CacheFoodSpawnInfo();
            _cachedCapsuleRushTimeThreshold = CalculateCapsuleRushTimeThreshold(GetActiveEnemyCapsules());
            
            
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
            ConsumedEnemyCapsulesByTeam[TeamAssignmentUtil.CheckTeam(gameObject)] = new List<Vector3>();
            
            // Set the initial respawn step
            if (_agent != null) _previousRespawnStep = _agent.GetLastRespawnStep();
            
            var coarseObstacleMap = ObstacleMapV2.Initialize(_mapManager, new List<GameObject>(), new Vector3(1f, 1f, 1f));
            _voronoiPartitioning = new VoronoiPartitioning(coarseObstacleMap);
        }

        private void OnDisable()
        {
            RoleAssigner.Instance?.UnregisterAgent(this);
        }

        public override PacManAction Tick()
        {
            _agent.GetTimeRemaining();
            _agent.GetScore();

                Vector3 velocity = _agent.GetVelocity();
                int carriedFoodCount = _agent.GetCarriedFoodCount();

                int currentRespawnStep = _agent.GetLastRespawnStep();
                if (_lastKnownRespawnStep != currentRespawnStep)
                {
                    ClearCurrentPath();
                    _lastKnownRespawnStep = currentRespawnStep;
                    _attackerThreatRetreatActive = false;
                    _previousCarriedFoodCount = carriedFoodCount;
                    _attackerRegroupAfterReturnHome = false;
                    _lastPlannedUnsafeCellCount = 0;
                    _hasLatchedHomeTarget = false;
                    _teammateYieldBackoffUntilStep = -1;
                    _teammateYieldObstacleUntilStep = -1;
                    _teammateYieldRetriggerBlockedUntilStep = -1;
                    _teammateYieldWaitingForSeparation = false;
                }

                RegisterConsumedEnemyCapsuleFromTeamPositions();

                TryTriggerTeammateYield();

                if (IsTeammateYieldBackoffActive())
                {
                    ClearCurrentPath();
                    _previousMode = _currentMode;
                    _btReason = "Yielding to teammate";
                    _previousCarriedFoodCount = carriedFoodCount;
                    return new PacManAction
                    {
                        Acceleration = _teammateYieldBackoffAcceleration
                    };
                }

                bool justDepositedFood =
                    _assignedRole == StaticRole.Attack &&
                    _previousCarriedFoodCount > 0 &&
                    carriedFoodCount == 0 &&
                    IsInOwnTerritory(transform.localPosition);

                if (justDepositedFood)
                {
                    _attackerRegroupAfterReturnHome = true;
                    ClearCurrentPath();
                }

                _lastDecision = EvaluateCurrentRoleTree();
                _currentMode = _lastDecision.Mode;

                if (_currentMode != _previousMode)
                {
                    ClearCurrentPath();
                }

                Vector2 accel = ExecuteDecision(_lastDecision, velocity);

                _previousMode = _currentMode;
                _previousCarriedFoodCount = carriedFoodCount;

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
                return new Vector2(dir.x, dir.z);
            }

            return GetReturnHomeAcceleration();
        }

        /// <summary>
        /// Checks whether an attacker should begin a late-game retreat based on carried food,
        /// remaining time, and an estimated travel time to a home target.
        /// </summary>
        /// <param name="myPos">Current local position of the agent.</param>
        /// <param name="carriedFoodCount">How much food the agent is currently carrying.</param>
        /// <param name="timeRemaining">Seconds left in the match.</param>
        /// <param name="canReachHomeInTime">True when the current estimate says home is still reachable before timeout.</param>
        /// <returns>True when the agent should immediately head home; otherwise false.</returns>
        private bool ShouldReturnHomeLateGame(Vector3 myPos, int carriedFoodCount, float timeRemaining, out bool canReachHomeInTime)
        {
            canReachHomeInTime = false;

            if (carriedFoodCount <= 0)
                return false;

            if (IsInOwnTerritory(myPos))
                return false;

            if (timeRemaining <= 0f)
                return false;

            Vector3 homeTarget = GetSafestHomePoint();
            float estimatedTravelDistance = Vector3.Distance(myPos, homeTarget);
            float estimatedTimeToHome = estimatedTravelDistance / Mathf.Max(0.01f, lateGameReturnHomeHorizontalSpeed);
            canReachHomeInTime = estimatedTimeToHome <= timeRemaining;
            if (!canReachHomeInTime)
                return false;

            float deadline = Mathf.Max(lateGameReturnHomeBaseSeconds, estimatedTimeToHome + lateGameReturnHomeBufferSeconds);

             return timeRemaining <= deadline;
         }

          /// <summary>
          /// Calculates the time threshold for rushing power capsules in late game.
          /// Formula: baseTime + (numExtraPowerPills * extraTime)
          /// For example: 25 + (2 * 20) = 64 seconds for 2 available power pills
          /// </summary>
          /// <param name="activeEnemyCapsules">List of available enemy capsules to count.</param>
          /// <returns>The calculated capsule rush time threshold in seconds.</returns>
          private float CalculateCapsuleRushTimeThreshold(List<GameObject> activeEnemyCapsules)
         {
             int numCapsules = activeEnemyCapsules != null ? activeEnemyCapsules.Count : 0;
             // Base time + (extra capsules beyond first) * extra time per capsule
             // With 1 capsule: base only
             // With 2+ capsules: base + (count-1) * extra
             float extraCapsuleCount = Mathf.Max(0, numCapsules - 1);
             return powerPillBaseTimeSeconds + (extraCapsuleCount * powerPillExtraTimeSeconds);
         }

         /// <summary>
         /// Checks whether the agent has enough time to reach a power capsule with a buffer for other agents.
         /// Uses Euclidean distance to estimate travel time at a typical movement speed.
         /// The agent needs enough time to: travel to capsule + buffer time for other agents to use the power-up.
         /// </summary>
         /// <param name="agentPos">Current local position of the agent.</param>
         /// <param name="capsulePos">Local position of the target power capsule.</param>
         /// <param name="timeRemaining">Seconds left in the match.</param>
         /// <returns>True if the agent can reach the capsule with buffer time for other agents to use it; otherwise false.</returns>
         private bool CanReachCapsuleWithBufferTime(Vector3 agentPos, Vector3 capsulePos, float timeRemaining)
         {
             // Use a conservative movement speed estimate (slower than max speed to account for pathfinding)
             float estimatedSpeed = lateGameReturnHomeHorizontalSpeed;
             
             // Calculate Euclidean distance to capsule
             float distanceToCapsule = Vector3.Distance(agentPos, capsulePos);
             
             // Time to reach and grab the capsule
             float timeToReachCapsule = distanceToCapsule / Mathf.Max(0.01f, estimatedSpeed);
             
             // Buffer time for other agents to utilize the power capsule benefit
             float bufferForOtherAgents = 3.0f;
             
             // Total time needed: reach capsule + buffer for team to use it
             float totalTimeNeeded = timeToReachCapsule + bufferForOtherAgents;
             
             return (timeRemaining - totalTimeNeeded) >= 0f;
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
        private bool MakePath(bool ownTerritoryOnly = false)
        {
            _initialDroneState = _agent.transform;
            var movementController = _initialDroneState.GetComponent<PacManMovementController>();
            if (movementController == null)
            {
                Debug.LogWarning($"MakePath failed: missing {nameof(PacManMovementController)} on agent {name}.");
                _waypoints = null;
                _droneControlling = null;
                return false;
            }
            var curPos = _initialDroneState.localPosition;
            var dynamicPathObstacles = BuildDynamicPathObstacles(
                _goalPosition,
                out int originalCapsuleObstacleCount,
                out int teammateYieldObstacleCount);

            var startTrav = _obstacleMap.GetLocalPointTraversibility(curPos);
            var goalTrav = _obstacleMap.GetLocalPointTraversibility(_goalPosition);

            // Debug.Log(
            //     $"MakePath() | agent={name} | mode={_currentMode} | " +
            //     $"start={curPos} | goal={_goalPosition} | " +
            //     $"startTrav={startTrav} | goalTrav={goalTrav} | " +
            //     $"hasDefenseAnchor={_hasDefenseAnchor} | defenseAnchor={_defenseAnchor}"
            // );

            UpdateVoronoiData();
            bool enforceOwnTerritoryPath =
                ownTerritoryOnly &&
                IsInOwnTerritory(curPos) &&
                IsInOwnTerritory(_goalPosition);

            Astar aStar = new Astar(
                _obstacleMap,
                dynamicPathObstacles,
                enforceOwnTerritoryPath ? IsInOwnTerritory : null,
                VoronoiCellScaleFactor,
                voronoiPathDangerPenaltyMultiplier);
            List<Vector3> aStarPath = aStar.PlanPathAStar(curPos, _goalPosition, _currentVoronoi);

            _lastPlannedGoalPosition = _goalPosition;
            _lastPlannedUnsafeCellCount = CountUnsafeCellsOnPath(aStarPath);

            if (aStarPath == null || aStarPath.Count < 2)
            {
                Debug.LogWarning(
                    $"MakePath failed: no valid A* path. " +
                    $"agent={name} role={_assignedRole} mode={_currentMode} decision={_lastDecision?.DebugLabel ?? "-"} reason={_btReason} " +
                    $"ownTerritoryOnly={ownTerritoryOnly} enforcedOwnTerritory={enforceOwnTerritoryPath} " +
                    $"start={curPos} goal={_goalPosition} startTrav={startTrav} goalTrav={goalTrav} " +
                    $"dynamicObstacles={dynamicPathObstacles.Count} capsuleObstacles={originalCapsuleObstacleCount} " +
                    $"yieldObstacles={teammateYieldObstacleCount} activeCapsules={GetActiveCapsules().Count}");
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
            _droneControlling = new DroneControlling(_waypoints, _goalPosition, _initialDroneState, movementController);
            _lastPathPlanStep = _agent.GetStepsSinceMatchStart();
            return true;
        }
        
        
        /// <summary>
        /// Refreshes cached Voronoi data on a staggered cadence.
        /// Voronoi is kept available for enemy food/capsule evaluation even while standing on the home side.
        /// </summary>
        private void UpdateVoronoiData()
        {
            // If agent has at least two seconds of powers
            // or it is powered and there is another power pill on the map
            if (_agent.GetPowerRemainingDuration() > 2f || 
                (_agent.IsPoweredUp() && _agent.GetCapsuleObjects().Any(c => c != null && c.activeSelf && !IsInOwnTerritory(c.transform.localPosition))))
            {
                _currentVoronoi = null;
                return;
            }

            bool isBlue = TeamAssignmentUtil.CheckTeam(gameObject) == Team.Blue;
            bool isOnOpponentSide = isBlue ? transform.localPosition.x > 0 : transform.localPosition.x < 0;
            bool goalOnOpponentSide = _hasGoal && !IsInOwnTerritory(_goalPosition);
            Team myTeam = TeamAssignmentUtil.CheckTeam(gameObject);
            bool hasEnemyFoodTargets = _agent.GetFoodObjects().Any(food =>
                food != null && food.activeSelf && TeamAssignmentUtil.CheckTeam(food) != myTeam);
            bool hasEnemyCapsuleTargets = _agent.GetCapsuleObjects().Any(capsule =>
                capsule != null && capsule.activeSelf && TeamAssignmentUtil.CheckTeam(capsule) != myTeam);
            bool shouldEvaluateEnemyObjectives = _assignedRole == StaticRole.Attack && (hasEnemyFoodTargets || hasEnemyCapsuleTargets);
            bool shouldUseVoronoi = isOnOpponentSide || goalOnOpponentSide || shouldEvaluateEnemyObjectives;

            if (!shouldUseVoronoi)
            {
                _currentVoronoi = null;
                return;
            }

            int interval = Mathf.Max(1, _voronoiUpdateIntervalSteps);
            int currentStep = _agent.GetStepsSinceMatchStart();
            int phase = GetVoronoiUpdatePhase(interval);
            bool mustBootstrap = _currentVoronoi == null;
            bool dueByInterval = (currentStep - _lastVoronoiUpdateStep) >= interval;
            bool onStaggerSlot = ((currentStep + phase) % interval) == 0;

            if (!mustBootstrap && !(dueByInterval && onStaggerSlot))
                return;

            var enemyPositions = GetTrackedEnemies()
                .Where(enemy => enemy != null && enemy.HasPosition)
                .Select(enemy => enemy.Position)
                .ToList();

            // Compute full Voronoi and keep visualization filtering in OnDrawGizmos.
            _currentVoronoi = _voronoiPartitioning.ComputeVoronoi(transform.localPosition, enemyPositions);
            _lastVoronoiUpdateStep = currentStep;
        }

        /// <summary>
        /// Computes a stable update offset so Voronoi refreshes are staggered across agents.
        /// </summary>
        /// <param name="interval">The configured update interval in steps.</param>
        /// <returns>
        /// A deterministic phase offset derived from the agent server index when available,
        /// otherwise from the local instance ID.
        /// </returns>
        private int GetVoronoiUpdatePhase(int interval)
        {
            if (interval <= 1)
                return 0;

            int stableId = (_agent != null && _agent.serverIndex >= 0)
                ? _agent.serverIndex
                : Mathf.Abs(GetInstanceID());

            return Mathf.Abs(stableId) % interval;
        }
        

        private DefenderBlackboard BuildDefenderBlackboard()
        {
            DefenderBlackboard bb = new DefenderBlackboard();

            Vector3 myPos = transform.localPosition;
            UpdateVoronoiData();
            var defendAssignment = RoleAssigner.Instance?.DefendManager?.GetAssignment(this);
            var activeFood = _agent.GetFoodObjects().FindAll(f => f.activeSelf &&
                                                TeamAssignmentUtil.CheckTeam(f) != TeamAssignmentUtil.CheckTeam(gameObject));
            bool isPowered = _agent.IsPoweredUp();
            bool isScared = _agent.IsScared();
            float scaredRemaining = Mathf.Max(0f, _agent.GetScaredRemainingDuration());
            int carriedFood = _agent.GetCarriedFoodCount();
            Vector3 homeTarget = GetSafestHomePoint();
            bool holdLaneDueToFoodPile = ShouldHoldDefenderLaneDueToFoodPile(
                out int protectedLaneFoodCount,
                out Vector3 protectedFoodCenter,
                out List<Vector3> protectedFoodPositions);

            bb.homeTargetPosition = homeTarget;

            if (isScared)
            {
                if (carriedFood >= poweredReturnFoodThreshold || scaredRemaining < 2f)
                {
                    bb.shouldReturnHome = true;
                    bb.debugReason = carriedFood >= poweredReturnFoodThreshold
                        ? "Scared loot threshold reached"
                        : "Scared ending soon, returning home";
                }
                else
                {
                    var scaredAssignment = RoleAssigner.Instance?.AttackManager?.GetAssignment(this, activeFood, includePoweredDefenders: true);
                    GameObject selectedFoodTarget = GetSafestFoodTarget(activeFood, GetPreferredFoodTarget(activeFood, scaredAssignment?.FoodTarget));
                    string selectedFoodReason = selectedFoodTarget != null
                        ? "Scared counter-raid (safest pill)"
                        : (scaredAssignment?.Reason ?? "Scared counter-raid");

                    if (selectedFoodTarget != null && ShouldRetryFoodTarget(selectedFoodTarget.transform.localPosition))
                    {
                        GameObject alternateFoodTarget = GetAlternativeFoodTarget(activeFood, selectedFoodTarget);
                        if (alternateFoodTarget != null)
                        {
                            selectedFoodTarget = alternateFoodTarget;
                            RegisterUnsafeFoodRetarget();
                            selectedFoodReason = $"Assigned pill path too unsafe ({_lastPlannedUnsafeCellCount} unsafe cells), trying alternate";
                        }
                    }

                    if (selectedFoodTarget != null)
                    {
                        SetCurrentFoodTarget(selectedFoodTarget);
                        bb.shouldLootWhilePowered = true;
                        bb.enemyPillTargetPosition = selectedFoodTarget.transform.localPosition;
                        bb.debugReason = selectedFoodReason;
                    }
                    else if (TryGetSafestFoodPosition(myPos, activeFood, out var fallbackScaredFoodTarget))
                    {
                        SetCurrentFoodTarget(null);
                        bb.shouldLootWhilePowered = true;
                        bb.enemyPillTargetPosition = fallbackScaredFoodTarget;
                        bb.debugReason = "Scared counter-raid fallback";
                    }
                    else
                    {
                        SetCurrentFoodTarget(null);
                        bb.shouldReturnHome = true;
                        bb.debugReason = "Scared with no enemy pill target";
                    }
                }
            }
            else if (isPowered && !holdLaneDueToFoodPile)
            {
                bb.shouldReturnHome = carriedFood >= poweredReturnFoodThreshold;

                var poweredAssignment = RoleAssigner.Instance?.AttackManager?.GetAssignment(this, activeFood, includePoweredDefenders: true);
                GameObject poweredFoodTarget = GetSafestFoodTarget(activeFood, GetPreferredFoodTarget(activeFood, poweredAssignment?.FoodTarget));
                if (!bb.shouldReturnHome && poweredFoodTarget != null)
                {
                    SetCurrentFoodTarget(poweredFoodTarget);
                    bb.shouldLootWhilePowered = true;
                    bb.enemyPillTargetPosition = poweredFoodTarget.transform.localPosition;
                    bb.debugReason = "Powered up loot mode";
                }
                else if (bb.shouldReturnHome)
                {
                    SetCurrentFoodTarget(null);
                    bb.debugReason = "Powered loot threshold reached";
                }
            }
            else if (isPowered && holdLaneDueToFoodPile)
            {
                SetCurrentFoodTarget(null);
                bb.debugReason = $"Powered guarding lane pill pile ({protectedLaneFoodCount} pills)";
            }

            bool defendAssignmentAllowed =
                defendAssignment != null &&
                (!holdLaneDueToFoodPile ||
                 IsIntruderNearProtectedFoodPile(defendAssignment.TargetPosition, protectedFoodCenter, protectedFoodPositions));

            if (!bb.shouldLootWhilePowered && !bb.shouldReturnHome && defendAssignmentAllowed)
            {
                bb.enemyPacmanIntruderSuspected = true;
                bb.suspectedIntruderPosition = defendAssignment.TargetPosition;
                bb.debugReason = defendAssignment.Reason;
            }
            else if (!bb.shouldLootWhilePowered && !bb.shouldReturnHome && holdLaneDueToFoodPile)
            {
                if (string.IsNullOrEmpty(bb.debugReason))
                    bb.debugReason = $"Guarding lane pill pile ({protectedLaneFoodCount} pills)";
            }

            bb.enemyLikelyCrossingMyLane = false;
            bb.predictedCrossingPoint = Vector3.zero;
            if (!bb.shouldLootWhilePowered &&
                !bb.shouldReturnHome &&
                !isPowered &&
                !isScared &&
                !bb.enemyPacmanIntruderSuspected &&
                TryGetMirrorLaneTarget(out var mirrorTarget, out var mirrorReason))
            {
                bb.enemyLikelyCrossingMyLane = true;
                bb.predictedCrossingPoint = mirrorTarget;
                bb.debugReason = mirrorReason;
            }

            bb.safeMiddlePillsAvailable = false;
            bb.safeMiddlePillPosition = Vector3.zero;
            if (!bb.shouldLootWhilePowered &&
                !bb.shouldReturnHome &&
                !bb.enemyPacmanIntruderSuspected &&
                !bb.enemyLikelyCrossingMyLane &&
                (!isPowered || !holdLaneDueToFoodPile) &&
                TryGetSafeMiddlePillTarget(activeFood, out var safeMiddleTarget, out var safeMiddleReason))
            {
                bb.safeMiddlePillsAvailable = true;
                bb.safeMiddlePillPosition = safeMiddleTarget;
                bb.debugReason = safeMiddleReason;
            }

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
             UpdateVoronoiData();
             var trackedEnemies = GetTrackedEnemies();
             var activeFood = _agent.GetFoodObjects().FindAll(f => f.activeSelf &&
                                                 TeamAssignmentUtil.CheckTeam(f) != TeamAssignmentUtil.CheckTeam(gameObject));
             var activeEnemyCapsules = GetActiveEnemyCapsules();
             float timeRemaining = _agent.GetTimeRemaining();
             bool isPowered = _agent.IsPoweredUp();
             float powerRemaining = Mathf.Max(0f, _agent.GetPowerRemainingDuration());
             int carriedFoodCount = _agent.GetCarriedFoodCount();
             
             // Calculate power capsule rush decision early so it can override late-game return home
             Vector3 capsuleTarget = Vector3.zero;
             bool shouldRushPowerCapsule =
                 !isPowered &&
                   timeRemaining <= _cachedCapsuleRushTimeThreshold &&
                 TryGetClosestObjectPosition(myPos, activeEnemyCapsules, out capsuleTarget) &&
                 CanReachCapsuleWithBufferTime(myPos, capsuleTarget, timeRemaining);
             
             // Don't return home due to time if we should rush the power capsule instead
             bool shouldReturnHomeLateGame;
             bool canReachHomeInTime = true;  // Default to true if we're rushing capsule
             if (shouldRushPowerCapsule)
             {
                 shouldReturnHomeLateGame = false;
             }
             else
             {
                 shouldReturnHomeLateGame = ShouldReturnHomeLateGame(myPos, carriedFoodCount, timeRemaining, out canReachHomeInTime);
             }

            float closestEnemyDist = float.MaxValue;
            TrackedEnemyInfo closestEnemy = null;

            if (trackedEnemies != null)
            {
                foreach (var enemy in trackedEnemies)
                {
                    if (enemy == null || !enemy.HasPosition)
                        continue;

                    float dist = Vector3.Distance(myPos, enemy.Position);
                    if (dist < closestEnemyDist)
                    {
                        closestEnemyDist = dist;
                        closestEnemy = enemy;
                    }
                }
            }

            int dangerStartFood = Mathf.Max(0, attackerGhostDangerStartFoodThreshold);
            const int dangerMaxFood = 8;
            float dangerProgress =
                carriedFoodCount <= dangerStartFood
                    ? 0f
                    : Mathf.Clamp01((carriedFoodCount - dangerStartFood) / (float)Mathf.Max(1, dangerMaxFood - dangerStartFood));

            float ghostDangerDistance = carriedFoodCount < dangerStartFood
                ? 0f
                : Mathf.Lerp(
                    baseGhostDangerDistance,
                    maxGhostDangerDistance,
                    dangerProgress);

            bool carryingFood = carriedFoodCount >= 1;
            bool lateGameBankUnreachable = carryingFood && !canReachHomeInTime;
            bool ghostInsideEnterRange = closestEnemy != null && closestEnemyDist < ghostDangerDistance;
            bool ghostInsideExitRange = closestEnemy != null && closestEnemyDist < (ghostDangerDistance + ghostDangerHysteresisDistance);
            if (isPowered || !carryingFood)
            {
                _attackerThreatRetreatActive = false;
            }
            else if (!_attackerThreatRetreatActive)
            {
                _attackerThreatRetreatActive = ghostInsideEnterRange;
            }
            else
            {
                bool reachedSafeAttackAnchor =
                    _hasAttackAnchor &&
                    IsInOwnTerritory(myPos) &&
                    Vector3.Distance(myPos, _attackAnchor) <= 0.75f;

                _attackerThreatRetreatActive = !reachedSafeAttackAnchor && ghostInsideExitRange;
            }

             bool ghostNearby = _attackerThreatRetreatActive;
 
             bool returnHomeReleasedDeepInsideOwnSide =
                 carriedFoodCount == 0 &&
                 IsDeepEnoughInOwnTerritory(myPos, returnHomeReleaseOwnSideDistance);

            if (shouldRushPowerCapsule)
            {
                _attackerThreatRetreatActive = false;
                _attackerRegroupAfterReturnHome = false;
                ghostNearby = false;
            }

            var attackAssignment = RoleAssigner.Instance?.AttackManager?.GetAssignment(this, activeFood, includePoweredDefenders: isPowered);
            var capsuleCampAssignment = RoleAssigner.Instance?.AttackManager?.GetCapsuleCampAssignment(this, activeEnemyCapsules);
            var capsuleRushAssignment = RoleAssigner.Instance?.AttackManager?.GetCapsuleRushAssignment(this, activeEnemyCapsules);
            bool hasPoweredCapsuleAssignment = isPowered && capsuleCampAssignment?.CapsuleTarget != null;

            bool shouldCommitReturnHome =
                (!shouldRushPowerCapsule && !isPowered && carriedFoodCount >= attackerForcedReturnFoodThreshold) ||
                (!shouldRushPowerCapsule && carryingFood && !isPowered && ghostInsideEnterRange) ||
                (isPowered && !hasPoweredCapsuleAssignment && carriedFoodCount >= poweredReturnFoodThreshold);

            bool shouldReturnHomeNow =
                _attackerRegroupAfterReturnHome ||
                shouldCommitReturnHome ||
                (!isPowered && ghostNearby) ||
                shouldReturnHomeLateGame;
            Vector3 homeTarget = GetStableHomeTarget(shouldReturnHomeNow);
            bool reachedHomeReturnTarget =
                carriedFoodCount == 0 &&
                IsInOwnTerritory(myPos) &&
                Vector3.Distance(myPos, homeTarget) <= 0.45f;

            if (shouldCommitReturnHome)
            {
                _attackerRegroupAfterReturnHome = true;
            }
            else if (_attackerRegroupAfterReturnHome &&
                     (reachedHomeReturnTarget || returnHomeReleasedDeepInsideOwnSide))
            {
                _attackerRegroupAfterReturnHome = false;
            }

            bb.shouldReturnHome =
                _attackerRegroupAfterReturnHome ||
                (!isPowered && ghostNearby) ||
                shouldReturnHomeLateGame;
            bb.shouldReturnHomeLateGame = shouldReturnHomeLateGame;
            if (!bb.shouldReturnHome)
                _hasLatchedHomeTarget = false;

            bb.homeTargetPosition = homeTarget;
            GameObject selectedFoodTarget = GetSafestFoodTarget(activeFood, GetPreferredFoodTarget(activeFood, attackAssignment?.FoodTarget));
            string selectedFoodReason = selectedFoodTarget != null
                ? "Safest enemy pill selected"
                : (attackAssignment?.Reason ?? "Safe enemy pill available");

            if (selectedFoodTarget != null && ShouldRetryFoodTarget(selectedFoodTarget.transform.localPosition))
            {
                GameObject alternateFoodTarget = GetAlternativeFoodTarget(activeFood, selectedFoodTarget);
                if (alternateFoodTarget != null)
                {
                    selectedFoodTarget = alternateFoodTarget;
                    RegisterUnsafeFoodRetarget();
                    selectedFoodReason = $"Assigned pill path too unsafe ({_lastPlannedUnsafeCellCount} unsafe cells), trying alternate";
                }
            }

            SetCurrentFoodTarget(selectedFoodTarget);
            if (bb.shouldReturnHome)
                SetCurrentFoodTarget(null);

            if (shouldRushPowerCapsule)
            {
                SetCurrentFoodTarget(null);
                bb.shouldReturnHome = false;
                bb.shouldGrabPowerCapsule = true;
                bb.powerCapsuleTargetPosition =
                    capsuleRushAssignment?.CapsuleTarget != null
                        ? capsuleRushAssignment.CapsuleTarget.transform.localPosition
                        : capsuleTarget;
            }

            if (isPowered)
            {
                if (hasPoweredCapsuleAssignment &&
                    powerRemaining <= Mathf.Max(0f, nextCapsuleGrabLeadTime))
                {
                    bb.shouldGrabPowerCapsule = true;
                    bb.powerCapsuleTargetPosition = capsuleCampAssignment.CapsuleTarget.transform.localPosition;
                }
                else if (hasPoweredCapsuleAssignment)
                {
                    bb.shouldCampNextPowerCapsule = true;
                    bb.powerCapsuleCampPosition = GetCapsuleCampPoint(capsuleCampAssignment.CapsuleTarget.transform.localPosition);
                }
                else
                {
                    bb.shouldReturnHome = carriedFoodCount >= poweredReturnFoodThreshold;
                    bb.shouldLootWhilePowered = selectedFoodTarget != null;

                    if (bb.shouldLootWhilePowered)
                    {
                        bb.enemyPillTargetPosition = selectedFoodTarget.transform.localPosition;
                    }
                    else if (!bb.shouldReturnHome &&
                             TryGetSafestFoodPosition(myPos, activeFood, out var fallbackPoweredFoodTarget))
                    {
                        bb.shouldLootWhilePowered = true;
                        bb.enemyPillTargetPosition = fallbackPoweredFoodTarget;
                    }
                }
            }

            if (!bb.shouldLootWhilePowered &&
                selectedFoodTarget != null &&
                (isPowered || !ghostNearby))
            {
                bb.safeEnemyPillsAvailable = true;
                bb.enemyPillTargetPosition = selectedFoodTarget.transform.localPosition;
            }

            bb.safeMiddlePillsAvailable = false;
            bb.middlePillTargetPosition = Vector3.zero;

            Vector3 attackAnchor = _hasAttackAnchor ? _attackAnchor : myPos;
            bb.attackPositionTarget = attackAnchor;
            bb.patrolTargetPosition = GetAttackPatrolPoint(attackAnchor);

            bb.outsideAttackZone =
                _hasAttackAnchor &&
                Vector3.Distance(myPos, _attackAnchor) > 1.5f;

            if (_attackerRegroupAfterReturnHome)
            {
                bool regroupComplete =
                    reachedHomeReturnTarget || returnHomeReleasedDeepInsideOwnSide;

                if (regroupComplete)
                {
                    _attackerRegroupAfterReturnHome = false;
                }
                else
                {
                    bb.shouldGrabPowerCapsule = false;
                    bb.shouldCampNextPowerCapsule = false;
                    bb.shouldLootWhilePowered = false;
                    bb.safeEnemyPillsAvailable = false;
                    bb.shouldReturnHome = true;
                    bb.debugReason = "Finish return-home path";
                }
            }

            if (_attackerRegroupAfterReturnHome)
                bb.debugReason = "Finish return-home path";
            else if (bb.shouldReturnHomeLateGame)
                bb.debugReason = $"Late game return home (time left {timeRemaining:F1}s)";
            else if (lateGameBankUnreachable)
                bb.debugReason = "Too late to bank food, keep pressuring";
            else if (bb.shouldGrabPowerCapsule)
                bb.debugReason = capsuleRushAssignment?.Reason ?? "Late game power capsule rush";
            else if (bb.shouldCampNextPowerCapsule)
                bb.debugReason = capsuleCampAssignment?.Reason ?? "Camp next enemy power capsule";
            else if (bb.shouldReturnHome && isPowered)
                bb.debugReason = "Powered loot threshold reached";
            else if (bb.shouldReturnHome && !isPowered && carriedFoodCount >= attackerForcedReturnFoodThreshold)
                bb.debugReason = "Forced return at high loot count";
            else if (bb.shouldLootWhilePowered)
                bb.debugReason = "Powered up loot mode";
            else if (bb.shouldReturnHome)
                bb.debugReason = $"Threat nearby while carrying food (danger radius {ghostDangerDistance:F1})";
            else if (bb.safeEnemyPillsAvailable)
                bb.debugReason = selectedFoodReason;
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
                        Position = hasObservationPosition ? observation.Position : estimatedPosition,
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
                    
                    tracked[enemy.serverIndex] = new TrackedEnemyInfo
                    {
                        ServerIndex = enemy.serverIndex,
                        Position = enemy.transform.localPosition,
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

        private List<Vector3> GetAttackerDefendedHomePoints()
        {
            if (RoleAssigner.Instance == null || _middleInfo.Lanes == null || _middleInfo.Lanes.Count == 0)
                return null;

            Team myTeam = TeamAssignmentUtil.CheckTeam(gameObject);
            bool isBlue = myTeam == Team.Blue;
            var defenders = RoleAssigner.Instance
                .GetRegisteredAgentsForTeam(myTeam)
                .Where(agent =>
                    agent != null &&
                    agent != this &&
                    agent.AssignedRole == StaticRole.Defend &&
                    agent.HasDefenseAnchor)
                .ToList();

            if (defenders.Count == 0)
                return null;

            var points = new List<Vector3>();
            var usedLaneIds = new HashSet<int>();

            foreach (var defender in defenders)
            {
                var lane = MapMiddleAnalyzer.GetClosestLane(defender.DefenseAnchor, _middleInfo, majorOnly: false);
                if (lane == null || !usedLaneIds.Add(lane.Id))
                    continue;

                points.Add(isBlue ? lane.LeftCenterLocal : lane.RightCenterLocal);
            }

            return points.Count > 0 ? points : null;
        }

        private Vector3 GetSafestHomePoint()
        {
            bool isBlue = TeamAssignmentUtil.CheckTeam(gameObject) == Team.Blue;
            List<Vector3> homePoints =
                _assignedRole == StaticRole.Attack
                    ? GetAttackerDefendedHomePoints()
                    : null;

            if (homePoints == null || homePoints.Count == 0)
            {
                homePoints = isBlue
                    ? _middleInfo.MiddleLeftLocalPositions
                    : _middleInfo.MiddleRightLocalPositions;
            }

            if (homePoints == null || homePoints.Count == 0)
            {
                return transform.localPosition;
            }

            Vector3 currentPos = transform.localPosition;
            Vector3 bestRetreatPoint = transform.localPosition;
            bool foundCandidate = false;
            float bestSafetyScore = float.MaxValue;
            float bestDistance = float.MaxValue;
            var trackedEnemies = GetTrackedEnemies()
                .Where(enemy => enemy != null && enemy.HasPosition)
                .Select(enemy => enemy.Position)
                .ToList();

            foreach (var point in homePoints)
            {
                Vector3 retreatPoint = point + new Vector3(
                    isBlue ? -returnHomeOwnSideOffset : returnHomeOwnSideOffset,
                    0f,
                    0f);

                Vector3 snappedRetreatPoint = SnapToNearestFreePoint(retreatPoint);
                float safetyScore = GetReturnHomeLaneSafetyScore(currentPos, snappedRetreatPoint, trackedEnemies);
                float distance = Vector3.Distance(currentPos, snappedRetreatPoint);

                if (!foundCandidate ||
                    safetyScore + 0.0001f < bestSafetyScore ||
                    (Mathf.Abs(safetyScore - bestSafetyScore) <= 0.0001f && distance < bestDistance))
                {
                    foundCandidate = true;
                    bestRetreatPoint = snappedRetreatPoint;
                    bestSafetyScore = safetyScore;
                    bestDistance = distance;
                }
            }

            return foundCandidate ? bestRetreatPoint : transform.localPosition;
        }

        private float GetReturnHomeLaneSafetyScore(Vector3 fromPosition, Vector3 retreatPoint, List<Vector3> enemyPositions)
        {
            float score = 0f;
            int samples = Mathf.Max(2, returnHomeLaneDangerSampleCount);
            float minEnemyDistance = float.MaxValue;

            for (int i = 0; i <= samples; i++)
            {
                float t = i / (float)samples;
                Vector3 sample = Vector3.Lerp(fromPosition, retreatPoint, t);
                float danger = GetPointDanger(sample);
                score += danger * 12f;

                if (!IsPointCellSafe(sample))
                    score += 25f;

                if (enemyPositions == null)
                    continue;

                foreach (var enemyPosition in enemyPositions)
                {
                    minEnemyDistance = Mathf.Min(minEnemyDistance, Vector3.Distance(sample, enemyPosition));
                }
            }

            if (minEnemyDistance < float.MaxValue * 0.5f)
            {
                score += 12f / Mathf.Max(0.5f, minEnemyDistance);
            }

            score += Vector3.Distance(fromPosition, retreatPoint) * 0.03f;
            return score;
        }

        private Vector3 GetStableHomeTarget(bool shouldReturnHome)
        {
            if (!shouldReturnHome)
                return GetSafestHomePoint();

            if (_hasLatchedHomeTarget)
                return _latchedHomeTarget;

            _latchedHomeTarget = GetSafestHomePoint();
            _hasLatchedHomeTarget = true;
            return _latchedHomeTarget;
        }

        private List<GameObject> GetActiveEnemyCapsules()
        {
            var capsules = _agent.GetCapsuleObjects();
            if (capsules == null)
                return new List<GameObject>();

            Team myTeam = TeamAssignmentUtil.CheckTeam(gameObject);

            return capsules
                .Where(capsule =>
                    capsule != null &&
                    capsule.activeSelf &&
                    TeamAssignmentUtil.CheckTeam(capsule) != myTeam &&
                    !IsConsumedEnemyCapsulePosition(capsule.transform.localPosition))
                .ToList();
        }

        private void RegisterConsumedEnemyCapsuleFromTeamPositions()
        {
            float detectionRadius = Mathf.Max(0.05f, consumedCapsuleContactDistance);
            var capsules = GetRawEnemyCapsules();
            if (capsules == null || capsules.Count == 0)
                return;

            var searchPositions = new List<Vector3> { transform.localPosition };
            var friendlies = _agent != null ? _agent.GetFriendlyAgents() : null;
            if (friendlies != null)
            {
                searchPositions.AddRange(friendlies
                    .Where(friendly => friendly != null)
                    .Select(friendly => friendly.transform.localPosition));
            }

            foreach (var position in searchPositions)
            {
                GameObject nearestCapsule = null;
                float nearestDistance = float.MaxValue;

                foreach (var capsule in capsules)
                {
                    if (capsule == null)
                        continue;

                    float distance = Vector3.Distance(position, capsule.transform.localPosition);
                    if (distance > detectionRadius || distance >= nearestDistance)
                        continue;

                    nearestDistance = distance;
                    nearestCapsule = capsule;
                }

                if (nearestCapsule != null && !IsConsumedEnemyCapsulePosition(nearestCapsule.transform.localPosition))
                {
                    GetConsumedEnemyCapsulePositions().Add(nearestCapsule.transform.localPosition);
                }
            }
        }

        private bool IsConsumedEnemyCapsulePosition(Vector3 position)
        {
            float detectionRadius = Mathf.Max(0.05f, consumedCapsuleContactDistance);
            foreach (var consumedPosition in GetConsumedEnemyCapsulePositions())
            {
                if (Vector3.Distance(consumedPosition, position) <= detectionRadius)
                    return true;
            }

            return false;
        }

        private List<GameObject> GetRawEnemyCapsules()
        {
            var capsules = _agent.GetCapsuleObjects();
            if (capsules == null)
                return new List<GameObject>();

            Team myTeam = TeamAssignmentUtil.CheckTeam(gameObject);
            return capsules
                .Where(capsule =>
                    capsule != null &&
                    TeamAssignmentUtil.CheckTeam(capsule) != myTeam)
                .ToList();
        }

        private List<Vector3> GetConsumedEnemyCapsulePositions()
        {
            Team myTeam = TeamAssignmentUtil.CheckTeam(gameObject);
            if (!ConsumedEnemyCapsulesByTeam.TryGetValue(myTeam, out var consumed))
            {
                consumed = new List<Vector3>();
                ConsumedEnemyCapsulesByTeam[myTeam] = consumed;
            }

            return consumed;
        }

        private List<GameObject> GetActiveCapsules()
        {
            var capsules = _agent.GetCapsuleObjects();
            if (capsules == null)
                return new List<GameObject>();

            return capsules
                .Where(capsule =>
                    capsule != null &&
                    capsule.activeSelf)
                .ToList();
        }

        private IEnumerable<Vector3> GetInflatedCapsuleObstaclePoints()
        {
            var capsules = GetActiveCapsules();
            if (capsules == null || capsules.Count == 0)
                yield break;

            float inflation = Mathf.Max(0f, friendlyCapsuleObstacleInflation);
            float step = _obstacleMap != null ? _obstacleMap.trueScale.x : 0.2f;
            int radiusSteps = Mathf.Max(0, Mathf.CeilToInt(inflation / Mathf.Max(0.01f, step)));

            foreach (var capsule in capsules)
            {
                if (capsule == null)
                    continue;

                Vector3 center = capsule.transform.localPosition;
                if (Vector3.Distance(center, _goalPosition) <= capsuleGoalIgnoreRadius)
                    continue;

                for (int dx = -radiusSteps; dx <= radiusSteps; dx++)
                {
                    for (int dz = -radiusSteps; dz <= radiusSteps; dz++)
                    {
                        Vector3 offset = new Vector3(dx * step, 0f, dz * step);
                        if (offset.sqrMagnitude > inflation * inflation)
                            continue;

                        yield return center + offset;
                    }
                }
            }
        }

        private bool TryGetClosestObjectPosition(Vector3 fromPosition, List<GameObject> objects, out Vector3 targetPosition)
        {
            targetPosition = Vector3.zero;

            if (objects == null || objects.Count == 0)
                return false;

            float bestDistance = float.MaxValue;
            GameObject bestObject = null;

            foreach (var obj in objects)
            {
                if (obj == null || !obj.activeSelf)
                    continue;

                float dist = (obj.transform.localPosition - fromPosition).sqrMagnitude;
                if (dist < bestDistance)
                {
                    bestDistance = dist;
                    bestObject = obj;
                }
            }

            if (bestObject == null)
                return false;

            targetPosition = bestObject.transform.localPosition;
            return true;
        }

        /// <summary>
        /// Finds the safest active food target and returns its local position.
        /// Safety is prioritized over distance, with distance used as a tie-breaker.
        /// </summary>
        /// <param name="fromPosition">Reference local position used for distance tie-breaking.</param>
        /// <param name="objects">Candidate food objects to evaluate.</param>
        /// <param name="targetPosition">Output local position of the selected safest food.</param>
        /// <returns>True if a valid safest food target is found; otherwise false.</returns>
        private bool TryGetSafestFoodPosition(Vector3 fromPosition, List<GameObject> objects, out Vector3 targetPosition)
        {
            targetPosition = Vector3.zero;
            GameObject bestFood = GetSafestFoodTarget(objects, null, fromPosition);
            if (bestFood == null)
                return false;

            targetPosition = bestFood.transform.localPosition;
            return true;
        }

        private bool ShouldRetryFoodTarget(Vector3 targetPosition)
        {
            if (_agent != null && _agent.GetStepsSinceMatchStart() < _foodTargetUnsafeRetargetBlockedUntilStep)
                return false;

            return _lastPlannedUnsafeCellCount > pillUnsafeCellRetryThreshold &&
                   Vector3.Distance(_lastPlannedGoalPosition, targetPosition) <= targetLockDistance;
        }

        private GameObject GetPreferredFoodTarget(List<GameObject> activeFood, GameObject assignedTarget = null)
        {
            bool IsValid(GameObject target) =>
                target != null &&
                target.activeSelf &&
                activeFood != null &&
                activeFood.Contains(target);

            if (IsValid(_currentFoodTarget))
                return _currentFoodTarget;

            if (IsValid(assignedTarget))
                return assignedTarget;

            return null;
        }

        private void SetCurrentFoodTarget(GameObject foodTarget)
        {
            _currentFoodTarget = foodTarget != null && foodTarget.activeSelf ? foodTarget : null;
        }

        private void RegisterUnsafeFoodRetarget()
        {
            if (_agent == null)
                return;

            _foodTargetUnsafeRetargetBlockedUntilStep =
                _agent.GetStepsSinceMatchStart() + Mathf.Max(1, pillUnsafeRetargetCooldownSteps);
        }

        private GameObject GetAlternativeFoodTarget(List<GameObject> activeFood, GameObject currentTarget)
        {
            if (activeFood == null || activeFood.Count == 0)
                return null;

            return activeFood
                .Where(food => food != null && food.activeSelf && food != currentTarget)
                .Where(food => IsFoodCellSafe(food.transform.localPosition))
                .OrderBy(food => GetFoodDanger(food.transform.localPosition))
                .ThenBy(food => Vector3.Distance(transform.localPosition, food.transform.localPosition))
                .Take(Mathf.Max(1, pillCandidateAttempts))
                .FirstOrDefault();
        }

        /// <summary>
        /// Selects the safest food object from active candidates using Voronoi safety/danger,
        /// and only uses distance as a tie-breaker.
        /// </summary>
        /// <param name="activeFood">Active enemy food candidates.</param>
        /// <param name="preferredTarget">Optional existing target to keep unless a safer option is clearly better.</param>
        /// <param name="fromPosition">Optional reference local position for distance tie-breaking; defaults to agent position.</param>
        /// <returns>The selected safest food object, or null if no valid candidate exists.</returns>
        private GameObject GetSafestFoodTarget(List<GameObject> activeFood, GameObject preferredTarget = null, Vector3? fromPosition = null)
        {
            if (activeFood == null || activeFood.Count == 0)
                return null;

            Vector3 origin = fromPosition ?? transform.localPosition;
            var candidates = activeFood
                .Where(food => food != null && food.activeSelf)
                .Distinct()
                .ToList();

            if (candidates.Count == 0)
                return null;

            var safeCandidates = candidates
                .Where(food => IsFoodCellSafe(food.transform.localPosition))
                .ToList();

            if (safeCandidates.Count == 0)
            {
                if (preferredTarget != null && IsFoodCellSafe(preferredTarget.transform.localPosition))
                    return preferredTarget;

                return null;
            }

            // Safety dominates; distance is only a tie-breaker among equally safe pills.
            var best = safeCandidates
                .OrderBy(food => GetFoodDanger(food.transform.localPosition))
                .ThenBy(food => (food.transform.localPosition - origin).sqrMagnitude)
                .FirstOrDefault();

            if (best == null)
                return preferredTarget;

            bool preferredStillValid =
                preferredTarget != null &&
                preferredTarget.activeSelf &&
                safeCandidates.Contains(preferredTarget);

            if (!preferredStillValid || preferredTarget == best)
                return best;

            bool bestIsSafe = IsFoodCellSafe(best.transform.localPosition);
            bool preferredIsSafe = IsFoodCellSafe(preferredTarget.transform.localPosition);

            if (bestIsSafe && !preferredIsSafe)
                return best;

            float bestDanger = GetFoodDanger(best.transform.localPosition);
            float preferredDanger = GetFoodDanger(preferredTarget.transform.localPosition);
            float dangerImprovement = preferredDanger - bestDanger;
            if (dangerImprovement >= pillDangerSwitchThreshold)
                return best;

            float bestDistance = (best.transform.localPosition - origin).magnitude;
            float preferredDistance = (preferredTarget.transform.localPosition - origin).magnitude;
            if (Mathf.Abs(bestDanger - preferredDanger) <= pillDangerSwitchThreshold * 0.5f &&
                (preferredDistance - bestDistance) >= pillDistanceSwitchThreshold)
            {
                return best;
            }

            return preferredTarget;
        }

        /// <summary>
        /// Checks whether the Voronoi cell containing the food is safe based on danger level.
        /// </summary>
        /// <param name="foodPosition">Food world/local position to evaluate.</param>
        /// <returns>True if the danger level is below the threshold, or if no Voronoi data is available.</returns>
        private bool IsFoodCellSafe(Vector3 foodPosition)
        {
            if (TryGetPointCellData(foodPosition, out var cellData))
                return cellData.Danger <= _voronoiSafetyThreshold;

            // When a Voronoi map exists, unknown cells are treated as unsafe to avoid risky target picks.
            if (_currentVoronoi != null && _currentVoronoi.Count > 0)
                return false;

            return true;
        }

        /// <summary>
        /// Gets a normalized danger score for the Voronoi cell containing the food.
        /// </summary>
        /// <param name="foodPosition">Food world/local position to evaluate.</param>
        /// <returns>Danger in range [0..1], where lower is safer; returns 0 when no Voronoi data exists.</returns>
        private float GetFoodDanger(Vector3 foodPosition)
        {
            if (TryGetPointCellData(foodPosition, out var cellData))
                return Mathf.Clamp01(cellData.Danger);

            return 0f;
        }

        private bool IsPointCellSafe(Vector3 pointPosition)
        {
            if (TryGetPointCellData(pointPosition, out var cellData))
                return cellData.Danger <= _voronoiSafetyThreshold;

            if (_currentVoronoi != null && _currentVoronoi.Count > 0)
                return false;

            return true;
        }

        private float GetPointDanger(Vector3 pointPosition)
        {
            if (TryGetPointCellData(pointPosition, out var cellData))
                return Mathf.Clamp01(cellData.Danger);

            return 0f;
        }

        /// <summary>
        /// Maps a food position to the coarse Voronoi grid and retrieves its cell data.
        /// </summary>
        /// <param name="foodPosition">Food world/local position to sample.</param>
        /// <param name="cellData">Output Voronoi data for the corresponding coarse cell.</param>
        /// <returns>True if the cell exists in the current Voronoi map; otherwise false.</returns>
        private bool TryGetFoodCellData(Vector3 foodPosition, out VoronoiCellData cellData)
        {
            return TryGetPointCellData(foodPosition, out cellData);
        }

        private bool TryGetPointCellData(Vector3 pointPosition, out VoronoiCellData cellData)
        {
            cellData = default;
            if (_currentVoronoi == null || _currentVoronoi.Count == 0 || _obstacleMap == null)
                return false;

            var cell = _obstacleMap.WorldToCell(pointPosition);
            var coarseKey = new Vector2Int(
                FloorDiv(cell.x, VoronoiCellScaleFactor),
                FloorDiv(cell.z, VoronoiCellScaleFactor));

            return _currentVoronoi.TryGetValue(coarseKey, out cellData);
        }

        /// <summary>
        /// Counts path cells that are unsafe according to the coarse Voronoi map.
        /// </summary>
        /// <param name="path">Planned path in world positions.</param>
        /// <returns>Number of cells on the path that are marked unsafe.</returns>
        private int CountUnsafeCellsOnPath(List<Vector3> path)
        {
            if (path == null || path.Count == 0 || _currentVoronoi == null || _obstacleMap == null)
                return 0;

            int unsafeCount = 0;
            foreach (var point in path)
            {
                var cell = _obstacleMap.WorldToCell(point);
                var coarseKey = new Vector2Int(
                    FloorDiv(cell.x, VoronoiCellScaleFactor),
                    FloorDiv(cell.z, VoronoiCellScaleFactor));

                if (!_currentVoronoi.TryGetValue(coarseKey, out var cellData) || !cellData.IsSafe)
                    unsafeCount++;
            }

            return unsafeCount;
        }

        /// <summary>
        /// Integer floor-division that remains correct for negative coordinates.
        /// </summary>
        /// <param name="value">Numerator.</param>
        /// <param name="divisor">Positive divisor.</param>
        /// <returns>Floor(value / divisor).</returns>
        private static int FloorDiv(int value, int divisor)
        {
            if (divisor <= 0)
                return value;

            if (value >= 0)
                return value / divisor;

            return -(((-value) + divisor - 1) / divisor);
        }

        private Vector3 GetCapsuleCampPoint(Vector3 capsulePosition)
        {
            Team myTeam = TeamAssignmentUtil.CheckTeam(gameObject);
            Vector3 desired = capsulePosition + new Vector3(myTeam == Team.Blue ? -0.8f : 0.8f, 0f, 0f);
            return SnapToNearestFreePoint(desired);
        }

        private bool TryGetMirrorLaneTarget(out Vector3 mirrorTarget, out string reason)
        {
            mirrorTarget = Vector3.zero;
            reason = null;

            if (!_hasDefenseAnchor || _middleInfo.Lanes == null || _middleInfo.Lanes.Count == 0)
                return false;

            var trackedEnemies = GetTrackedEnemies();
            if (trackedEnemies == null || trackedEnemies.Count == 0)
                return false;

            MapMiddleAnalyzer.Lane lane = MapMiddleAnalyzer.GetClosestLane(_defenseAnchor, _middleInfo, majorOnly: false);
            if (lane == null)
                return false;

            float laneMinZ = lane.MidCenterLocal.z;
            float laneMaxZ = lane.MidCenterLocal.z;
            if (lane.LeftLocalPositions != null && lane.LeftLocalPositions.Count > 0)
            {
                laneMinZ = Mathf.Min(laneMinZ, lane.LeftLocalPositions.Min(p => p.z));
                laneMaxZ = Mathf.Max(laneMaxZ, lane.LeftLocalPositions.Max(p => p.z));
            }
            if (lane.RightLocalPositions != null && lane.RightLocalPositions.Count > 0)
            {
                laneMinZ = Mathf.Min(laneMinZ, lane.RightLocalPositions.Min(p => p.z));
                laneMaxZ = Mathf.Max(laneMaxZ, lane.RightLocalPositions.Max(p => p.z));
            }

            float paddedLaneMinZ = laneMinZ - Mathf.Max(0f, defenderMirrorLanePadding);
            float paddedLaneMaxZ = laneMaxZ + Mathf.Max(0f, defenderMirrorLanePadding);
            Team myTeam = TeamAssignmentUtil.CheckTeam(gameObject);
            float midX = _middleInfo.MidXLocal;
            float enemySideDepth = Mathf.Max(0.1f, defenderMirrorEnemySideDepth);

            TrackedEnemyInfo bestEnemy = null;
            float bestScore = float.MaxValue;

            foreach (var enemy in trackedEnemies)
            {
                if (enemy == null || !enemy.HasPosition)
                    continue;

                Vector3 enemyPos = enemy.Position;
                if (IsInOwnTerritory(enemyPos))
                    continue;

                bool insideEnemySideFront =
                    myTeam == Team.Blue
                        ? enemyPos.x >= midX && enemyPos.x <= (midX + enemySideDepth)
                        : enemyPos.x <= midX && enemyPos.x >= (midX - enemySideDepth);

                if (!insideEnemySideFront)
                    continue;

                if (enemyPos.z < paddedLaneMinZ || enemyPos.z > paddedLaneMaxZ)
                    continue;

                float laneScore = Mathf.Abs(enemyPos.z - _defenseAnchor.z);
                float depthScore = Mathf.Abs(enemyPos.x - midX);
                float score = laneScore + depthScore * 0.25f;

                if (score >= bestScore)
                    continue;

                bestScore = score;
                bestEnemy = enemy;
            }

            if (bestEnemy == null)
                return false;

            Vector3 desiredMirror = new Vector3(_defenseAnchor.x, 0f, bestEnemy.Position.z);
            mirrorTarget = SnapToNearestFreePoint(desiredMirror);
            reason = bestEnemy.IsVisible ? "Mirroring visible enemy in lane front" : "Mirroring tracked enemy in lane front";
            return true;
        }

        private bool ShouldHoldDefenderLaneDueToFoodPile(
            out int protectedFoodCount,
            out Vector3 protectedFoodCenter,
            out List<Vector3> protectedFoodPositions)
        {
            protectedFoodCount = 0;
            protectedFoodCenter = _hasDefenseAnchor ? _defenseAnchor : transform.localPosition;
            protectedFoodPositions = new List<Vector3>();

            if (_assignedRole != StaticRole.Defend ||
                !_hasDefenseAnchor ||
                defenderLaneFoodPileHoldThreshold <= 0 ||
                _middleInfo.Lanes == null ||
                _middleInfo.Lanes.Count == 0 ||
                _agent == null)
            {
                return false;
            }

            MapMiddleAnalyzer.Lane lane = MapMiddleAnalyzer.GetClosestLane(_defenseAnchor, _middleInfo, majorOnly: false);
            if (lane == null)
                return false;

            GetLaneZBounds(lane, out float laneMinZ, out float laneMaxZ);
            Team myTeam = TeamAssignmentUtil.CheckTeam(gameObject);
            var teamDefenders = RoleAssigner.Instance != null
                ? RoleAssigner.Instance.GetRegisteredAgentsForTeam(myTeam)
                    .Where(agent =>
                        agent != null &&
                        agent.AssignedRole == StaticRole.Defend &&
                        agent.HasDefenseAnchor)
                    .ToList()
                : new List<PacManAIDebugBT>();
            var foodObjects = _agent.GetFoodObjects();
            if (foodObjects == null || foodObjects.Count == 0)
                return false;

            float pileRadiusSqr = Mathf.Max(0.1f, defenderLaneFoodPileRadius);
            pileRadiusSqr *= pileRadiusSqr;
            var candidateFoodPositions = new List<Vector3>();
            var seedFoodIndices = new List<int>();

            foreach (var food in foodObjects)
            {
                if (food == null || !food.activeSelf)
                    continue;

                Vector3 foodPos = food.transform.localPosition;
                if (!TryGetFoodSpawnInfo(food, out var spawnInfo))
                    continue;

                if (!IsFoodDisplacedFromSpawn(foodPos, spawnInfo.SpawnLocalPosition))
                    continue;

                if (!IsInOwnTerritory(foodPos))
                    continue;

                if (foodPos.z < laneMinZ || foodPos.z > laneMaxZ)
                    continue;

                if (!IsClosestDefenderAnchorToPoint(foodPos, teamDefenders))
                    continue;

                int candidateIndex = candidateFoodPositions.Count;
                candidateFoodPositions.Add(foodPos);

                if ((foodPos - _defenseAnchor).sqrMagnitude > pileRadiusSqr)
                    continue;

                seedFoodIndices.Add(candidateIndex);
            }

            protectedFoodPositions = GetConnectedFoodPile(candidateFoodPositions, seedFoodIndices);
            protectedFoodCount = protectedFoodPositions.Count;

            if (protectedFoodCount > 0)
                protectedFoodCenter = GetAveragePosition(protectedFoodPositions);

            return protectedFoodCount >= defenderLaneFoodPileHoldThreshold;
        }

        private List<Vector3> GetConnectedFoodPile(List<Vector3> candidateFoodPositions, List<int> seedFoodIndices)
        {
            var connectedFood = new List<Vector3>();
            if (candidateFoodPositions == null ||
                candidateFoodPositions.Count == 0 ||
                seedFoodIndices == null ||
                seedFoodIndices.Count == 0)
            {
                return connectedFood;
            }

            float chainRadius = Mathf.Max(0.1f, defenderLaneFoodPileChainRadius);
            float chainRadiusSqr = chainRadius * chainRadius;
            var visited = new bool[candidateFoodPositions.Count];
            var queue = new Queue<int>();

            foreach (int seedIndex in seedFoodIndices)
            {
                if (seedIndex < 0 || seedIndex >= candidateFoodPositions.Count || visited[seedIndex])
                    continue;

                visited[seedIndex] = true;
                queue.Enqueue(seedIndex);
            }

            while (queue.Count > 0)
            {
                int currentIndex = queue.Dequeue();
                Vector3 currentPosition = candidateFoodPositions[currentIndex];
                connectedFood.Add(currentPosition);

                for (int i = 0; i < candidateFoodPositions.Count; i++)
                {
                    if (visited[i])
                        continue;

                    if ((candidateFoodPositions[i] - currentPosition).sqrMagnitude > chainRadiusSqr)
                        continue;

                    visited[i] = true;
                    queue.Enqueue(i);
                }
            }

            return connectedFood;
        }

        private static Vector3 GetAveragePosition(List<Vector3> positions)
        {
            if (positions == null || positions.Count == 0)
                return Vector3.zero;

            Vector3 sum = Vector3.zero;
            foreach (var position in positions)
            {
                sum += position;
            }

            return sum / positions.Count;
        }

        private void CacheFoodSpawnInfo()
        {
            if (_agent == null)
                return;

            var foodObjects = _agent.GetFoodObjects();
            if (foodObjects == null)
                return;

            foreach (var food in foodObjects)
            {
                if (food == null)
                    continue;

                TryGetFoodSpawnInfo(food, out _);
            }
        }

        private bool TryGetFoodSpawnInfo(GameObject food, out FoodSpawnInfo spawnInfo)
        {
            spawnInfo = default;
            if (food == null)
                return false;

            int id = food.GetInstanceID();
            if (TryBuildFoodSpawnInfoFromMap(food, out spawnInfo))
            {
                FoodSpawnInfoById[id] = spawnInfo;
                return true;
            }

            if (FoodSpawnInfoById.TryGetValue(id, out spawnInfo))
                return true;

            spawnInfo = new FoodSpawnInfo
            {
                SpawnLocalPosition = food.transform.localPosition
            };
            FoodSpawnInfoById[id] = spawnInfo;
            return true;
        }

        private bool TryBuildFoodSpawnInfoFromMap(GameObject food, out FoodSpawnInfo spawnInfo)
        {
            spawnInfo = default;

            if (food == null ||
                _agent?.PacManGameManager == null ||
                _agent.PacManGameManager.foodList == null ||
                _mapManager == null ||
                _mapManager.targetPositions == null)
            {
                return false;
            }

            int foodIndex = _agent.PacManGameManager.foodList.IndexOf(food);
            if (foodIndex < 0 || foodIndex >= _mapManager.targetPositions.Count())
                return false;

            Vector3 targetLocalPosition = _mapManager.targetPositions.ElementAt(foodIndex);
            Transform targetsTransform = _mapManager.transform.Find("Targets");
            Vector3 spawnWorldPosition = targetsTransform != null
                ? targetsTransform.position + targetLocalPosition
                : targetLocalPosition;

            Transform foodParent = food.transform.parent;
            Vector3 spawnLocalPosition = foodParent != null
                ? foodParent.InverseTransformPoint(spawnWorldPosition)
                : spawnWorldPosition;
            spawnLocalPosition.y = food.transform.localPosition.y;

            spawnInfo = new FoodSpawnInfo
            {
                SpawnLocalPosition = spawnLocalPosition
            };

            return true;
        }

        private static bool IsFoodDisplacedFromSpawn(Vector3 currentPosition, Vector3 spawnPosition)
        {
            const float displacedDistance = 0.2f;
            return (currentPosition - spawnPosition).sqrMagnitude > displacedDistance * displacedDistance;
        }

        private bool IsClosestDefenderAnchorToPoint(Vector3 point, List<PacManAIDebugBT> teamDefenders)
        {
            if (teamDefenders == null || teamDefenders.Count <= 1)
                return true;

            float myDistance = (_defenseAnchor - point).sqrMagnitude;
            int myTieBreaker = _agent != null ? _agent.serverIndex : GetInstanceID();

            foreach (var defender in teamDefenders)
            {
                if (defender == null || defender == this || !defender.HasDefenseAnchor)
                    continue;

                float otherDistance = (defender.DefenseAnchor - point).sqrMagnitude;
                if (otherDistance + 0.0001f < myDistance)
                    return false;

                if (Mathf.Abs(otherDistance - myDistance) <= 0.0001f)
                {
                    int otherTieBreaker = defender.AgentManager != null
                        ? defender.AgentManager.serverIndex
                        : defender.GetInstanceID();

                    if (otherTieBreaker < myTieBreaker)
                        return false;
                }
            }

            return true;
        }

        private bool IsIntruderNearProtectedFoodPile(
            Vector3 intruderPosition,
            Vector3 protectedFoodCenter,
            List<Vector3> protectedFoodPositions)
        {
            float radius = Mathf.Max(0.1f, defenderLaneFoodPileIntruderRadius);
            float radiusSqr = radius * radius;

            if (protectedFoodPositions != null && protectedFoodPositions.Count > 0)
            {
                foreach (var foodPosition in protectedFoodPositions)
                {
                    if ((intruderPosition - foodPosition).sqrMagnitude <= radiusSqr)
                        return true;
                }
            }

            return (intruderPosition - protectedFoodCenter).sqrMagnitude <= radiusSqr;
        }

        private static void GetLaneZBounds(MapMiddleAnalyzer.Lane lane, out float laneMinZ, out float laneMaxZ)
        {
            laneMinZ = lane.MidCenterLocal.z;
            laneMaxZ = lane.MidCenterLocal.z;

            if (lane.LeftLocalPositions != null && lane.LeftLocalPositions.Count > 0)
            {
                laneMinZ = Mathf.Min(laneMinZ, lane.LeftLocalPositions.Min(p => p.z));
                laneMaxZ = Mathf.Max(laneMaxZ, lane.LeftLocalPositions.Max(p => p.z));
            }

            if (lane.RightLocalPositions != null && lane.RightLocalPositions.Count > 0)
            {
                laneMinZ = Mathf.Min(laneMinZ, lane.RightLocalPositions.Min(p => p.z));
                laneMaxZ = Mathf.Max(laneMaxZ, lane.RightLocalPositions.Max(p => p.z));
            }
        }

        private bool TryGetSafeMiddlePillTarget(List<GameObject> activeFood, out Vector3 targetPosition, out string reason)
        {
            targetPosition = Vector3.zero;
            reason = null;

            if (!_hasDefenseAnchor || activeFood == null || activeFood.Count == 0 || _middleInfo.Lanes == null || _middleInfo.Lanes.Count == 0)
                return false;

            MapMiddleAnalyzer.Lane lane = MapMiddleAnalyzer.GetClosestLane(_defenseAnchor, _middleInfo, majorOnly: false);
            if (lane == null)
                return false;

            GetLaneZBounds(lane, out float laneMinZ, out float laneMaxZ);

            float paddedLaneMinZ = laneMinZ - Mathf.Max(0f, defenderSafeMiddleLanePadding);
            float paddedLaneMaxZ = laneMaxZ + Mathf.Max(0f, defenderSafeMiddleLanePadding);
            float midX = _middleInfo.MidXLocal;
            float middleDepth = Mathf.Max(0.1f, defenderSafeMiddleDepth);
            Team myTeam = TeamAssignmentUtil.CheckTeam(gameObject);
            var trackedEnemies = GetTrackedEnemies();

            GameObject bestFood = null;
            float bestScore = float.MaxValue;

            foreach (var food in activeFood)
            {
                if (food == null || !food.activeSelf)
                    continue;

                Vector3 foodPos = food.transform.localPosition;
                bool inMiddleBand =
                    myTeam == Team.Blue
                        ? foodPos.x >= midX && foodPos.x <= (midX + middleDepth)
                        : foodPos.x <= midX && foodPos.x >= (midX - middleDepth);

                if (!inMiddleBand)
                    continue;

                if (foodPos.z < paddedLaneMinZ || foodPos.z > paddedLaneMaxZ)
                    continue;

                bool enemyNearby = false;
                if (trackedEnemies != null)
                {
                    foreach (var enemy in trackedEnemies)
                    {
                        if (enemy == null || !enemy.HasPosition)
                            continue;

                        if (Vector3.Distance(enemy.Position, foodPos) <= defenderSafeMiddleEnemyClearance)
                        {
                            enemyNearby = true;
                            break;
                        }
                    }
                }

                if (enemyNearby)
                    continue;

                float laneScore = Mathf.Abs(foodPos.z - _defenseAnchor.z);
                float distanceScore = (transform.localPosition - foodPos).sqrMagnitude * 0.05f;
                float depthScore = Mathf.Abs(foodPos.x - midX) * 0.25f;
                float score = laneScore + depthScore + distanceScore;

                if (score >= bestScore)
                    continue;

                bestScore = score;
                bestFood = food;
            }

            if (bestFood == null)
                return false;

            Vector3 selectedTarget = bestFood.transform.localPosition;
            if (ShouldRetryFoodTarget(selectedTarget))
            {
                GameObject alternateFood = activeFood
                    .Where(food =>
                        food != null &&
                        food.activeSelf &&
                        food != bestFood &&
                        food.transform.localPosition.z >= paddedLaneMinZ &&
                        food.transform.localPosition.z <= paddedLaneMaxZ)
                    .OrderBy(food => Vector3.Distance(transform.localPosition, food.transform.localPosition))
                    .FirstOrDefault();

                if (alternateFood != null)
                {
                    selectedTarget = alternateFood.transform.localPosition;
                    RegisterUnsafeFoodRetarget();
                }
            }

            targetPosition = selectedTarget;
            reason = "Safe middle pill available";
            return true;
        }

        private Vector3 SnapToNearestFreePoint(Vector3 desired, float radiusStep = 0.2f, int maxRadiusSteps = 8)
        {
            desired.y = 0f;
            return SnapToNearestFreePoint(desired, null, radiusStep, maxRadiusSteps);
        }

        private Vector3 SnapToNearestFreePoint(
            Vector3 desired,
            System.Func<Vector3, bool> extraConstraint,
            float radiusStep = 0.2f,
            int maxRadiusSteps = 8)
        {
            desired.y = 0f;

            if (_obstacleMap != null &&
                _obstacleMap.GetLocalPointTraversibility(desired) == ObstacleMapV2.Traversability.Free &&
                (extraConstraint == null || extraConstraint(desired)))
            {
                return desired;
            }

            for (int radius = 1; radius <= maxRadiusSteps; radius++)
            {
                float r = radius * radiusStep;
                for (int i = 0; i < 16; i++)
                {
                    float angle = i * Mathf.PI * 2f / 16f;
                    Vector3 candidate = desired + new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
                    if (_obstacleMap != null &&
                        _obstacleMap.GetLocalPointTraversibility(candidate) == ObstacleMapV2.Traversability.Free &&
                        (extraConstraint == null || extraConstraint(candidate)))
                    {
                        return candidate;
                    }
                }
            }

            return desired;
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

                case "GrabPowerCapsule":
                    return ExecuteGrabPowerCapsule(decision);

                case "CampNextPowerCapsule":
                    return ExecuteCampNextPowerCapsule(decision);

                case "Evade":
                    return ExecuteEvade(decision, velocity);

                default:
                    ClearCurrentPath();
                    return Vector2.zero;
            }
        }
        private bool IsInOwnTerritory(Vector3 localPosition)
        {
            Team myTeam = TeamAssignmentUtil.CheckTeam(gameObject);
            if (myTeam == Team.Blue)
                return localPosition.x <= _middleInfo.MidXLocal;

            if (myTeam == Team.Red)
                return localPosition.x >= _middleInfo.MidXLocal;

            return true;
        }

        private List<Vector3> BuildDynamicPathObstacles(
            Vector3 goalPosition,
            out int capsuleObstacleCount,
            out int teammateYieldObstacleCount)
        {
            var capsuleObstaclePoints = GetInflatedCapsuleObstaclePoints().ToList();
            capsuleObstacleCount = capsuleObstaclePoints.Count;
            teammateYieldObstacleCount = 0;
            var dynamicPathObstacles = new List<Vector3>(capsuleObstaclePoints);

            if (IsTeammateYieldObstacleActive() &&
                Vector3.Distance(_teammateYieldObstaclePosition, goalPosition) > teammateYieldGoalIgnoreRadius)
            {
                var yieldObstaclePoints = GetInflatedTeammateYieldObstaclePoints().ToList();
                teammateYieldObstacleCount = yieldObstaclePoints.Count;
                dynamicPathObstacles.AddRange(yieldObstaclePoints);
            }

            return dynamicPathObstacles;
        }

        private Vector3 ClampToOwnTerritoryEdge(Vector3 localPosition)
        {
            Team myTeam = TeamAssignmentUtil.CheckTeam(gameObject);
            float ownSideNudge = Mathf.Max(0.05f, _obstacleMap != null ? _obstacleMap.trueScale.x : 0.2f);
            Vector3 clamped = localPosition;

            if (myTeam == Team.Blue && clamped.x > _middleInfo.MidXLocal)
                clamped.x = _middleInfo.MidXLocal - ownSideNudge;
            else if (myTeam == Team.Red && clamped.x < _middleInfo.MidXLocal)
                clamped.x = _middleInfo.MidXLocal + ownSideNudge;

            return clamped;
        }

        private bool IsDeepEnoughInOwnTerritory(Vector3 localPosition, float extraDistance)
        {
            Team myTeam = TeamAssignmentUtil.CheckTeam(gameObject);
            float requiredDistance = Mathf.Max(0f, extraDistance);

            if (myTeam == Team.Blue)
                return localPosition.x <= (_middleInfo.MidXLocal - requiredDistance);

            if (myTeam == Team.Red)
                return localPosition.x >= (_middleInfo.MidXLocal + requiredDistance);

            return true;
        }

        private Vector2 MoveToTarget(
            Vector3 target,
            float arriveDistance = 0.35f,
            bool ownTerritoryOnly = false,
            bool periodicPillRepath = false,
            int periodicRepathIntervalSteps = 0)
        {
            Vector3 myLocalPos = transform.localPosition;

            if (Vector3.Distance(myLocalPos, target) <= arriveDistance)
            {
                ClearCurrentPath();
                return Vector2.zero;
            }

            if (_hasGoal && Vector3.Distance(_goalPosition, target) <= targetLockDistance)
            {
                target = _goalPosition;
            }

            int currentStep = _agent.GetStepsSinceMatchStart();
            bool repathCooldownElapsed = (currentStep - _lastPathPlanStep) >= Mathf.Max(1, minStepsBetweenRepaths);
            bool failedPathRetryCooldownElapsed = (currentStep - _lastFailedPathPlanStep) >= Mathf.Max(1, failedPathRetryCooldownSteps);
            int periodicInterval = periodicPillRepath ? pillRepathIntervalSteps : periodicRepathIntervalSteps;
            bool periodicRepathElapsed = periodicInterval > 0 &&
                                         (currentStep - _lastPathPlanStep) >= Mathf.Max(1, periodicInterval);
            float targetShiftDistance = Vector3.Distance(_goalPosition, target);

            bool needNewPath =
                failedPathRetryCooldownElapsed &&
                (
                    !_hasGoal ||
                    (targetShiftDistance > retargetDistanceThreshold && repathCooldownElapsed) ||
                    (_hasGoal && periodicRepathElapsed)
                );

            if (needNewPath)
            {
                bool hadExistingPath = _droneControlling != null && _initialDroneState != null && _waypoints != null && _waypoints.Count > 1;
                Vector3 previousGoal = _goalPosition;
                bool previousHasGoal = _hasGoal;
                List<Node> previousWaypoints = _waypoints;
                DroneControlling previousController = _droneControlling;
                Transform previousDroneState = _initialDroneState;

                _goalPosition = target;

                bool pathOk = MakePath(ownTerritoryOnly);
                if (!pathOk)
                {
                    _lastFailedPathPlanStep = currentStep;

                    if (hadExistingPath)
                    {
                        _goalPosition = previousGoal;
                        _hasGoal = previousHasGoal;
                        _waypoints = previousWaypoints;
                        _droneControlling = previousController;
                        _initialDroneState = previousDroneState;
                    }
                    else
                    {
                        ClearCurrentPath();
                        return Vector2.zero;
                    }
                }
                else
                {
                    _hasGoal = true;
                }
            }

            if (_droneControlling == null || _initialDroneState == null)
            {
                ClearCurrentPath();
                return Vector2.zero;
            }

            _droneControlling.PDCalculateMove(droneTransform: _initialDroneState);
            return new Vector2(_droneControlling.h, _droneControlling.v);
        }

        private void TryTriggerTeammateYield()
        {
            if (_agent == null)
                return;

            int currentStep = _agent.GetStepsSinceMatchStart();

            if (ShouldIgnoreTeammateYieldWhileSettled())
                return;

            float releaseDistance = Mathf.Max(teammateYieldDetectDistance + 0.05f, teammateYieldReleaseDistance);
            if (_teammateYieldWaitingForSeparation)
            {
                if (TryGetTeammateYieldContact(out var currentTeammatePosition))
                {
                    _teammateYieldObstaclePosition = currentTeammatePosition;
                    if (Vector3.Distance(transform.localPosition, currentTeammatePosition) < releaseDistance)
                        return;
                }

                ResetTeammateYieldSeparationState();
            }

            if (currentStep < _teammateYieldRetriggerBlockedUntilStep)
                return;

            if (!TryGetTeammateYieldContact(out var teammatePosition))
                return;

            Vector3 away3 = transform.localPosition - teammatePosition;
            away3.y = 0f;

            if (away3.sqrMagnitude < 0.0001f)
            {
                Vector3 fallback = transform.localPosition - _goalPosition;
                fallback.y = 0f;
                away3 = fallback.sqrMagnitude > 0.0001f ? fallback : -transform.forward;
                away3.y = 0f;
            }

            Vector3 awayNormalized = away3.sqrMagnitude > 0.0001f ? away3.normalized : Vector3.back;
            Vector3 yieldDirection = ComputeTeammateYieldDirection(teammatePosition, awayNormalized);
            _teammateYieldBackoffAcceleration = new Vector2(yieldDirection.x, yieldDirection.z);
            _teammateYieldObstaclePosition = teammatePosition;
            _teammateYieldBackoffUntilStep = currentStep + Mathf.Max(1, teammateYieldBackoffSteps);
            _teammateYieldObstacleUntilStep = currentStep + Mathf.Max(1, teammateYieldObstacleSteps);
            _teammateYieldRetriggerBlockedUntilStep = currentStep + Mathf.Max(1, teammateYieldRetriggerCooldownSteps);
            _teammateYieldWaitingForSeparation = true;
            ClearCurrentPath();
        }

        private Vector3 ComputeTeammateYieldDirection(Vector3 teammatePosition, Vector3 awayNormalized)
        {
            Vector3 desiredDirection = Vector3.zero;
            Vector3 myPos = transform.localPosition;

            if (_lastDecision != null && _lastDecision.HasTarget)
            {
                desiredDirection = _lastDecision.TargetPosition - myPos;
            }
            else if (_hasGoal)
            {
                desiredDirection = _goalPosition - myPos;
            }
            else if (_agent != null)
            {
                desiredDirection = _agent.GetVelocity();
            }

            desiredDirection.y = 0f;
            if (desiredDirection.sqrMagnitude > 0.0001f)
                desiredDirection.Normalize();

            Vector3 contactDirection = teammatePosition - myPos;
            contactDirection.y = 0f;
            if (contactDirection.sqrMagnitude <= 0.0001f)
                return awayNormalized;

            contactDirection.Normalize();
            Vector3 leftPerpendicular = new Vector3(-contactDirection.z, 0f, contactDirection.x);
            Vector3 rightPerpendicular = -leftPerpendicular;

            float leftScore = ScoreYieldDirection(leftPerpendicular, desiredDirection);
            float rightScore = ScoreYieldDirection(rightPerpendicular, desiredDirection);
            Vector3 sidestep = leftScore >= rightScore ? leftPerpendicular : rightPerpendicular;

            // Keep a smaller "move apart" component so the sidestep still opens space for replanning.
            Vector3 combined = sidestep * 0.85f + awayNormalized * 0.35f;
            combined.y = 0f;
            return combined.sqrMagnitude > 0.0001f ? combined.normalized : awayNormalized;
        }

        private float ScoreYieldDirection(Vector3 direction, Vector3 desiredDirection)
        {
            float score = 0f;

            if (desiredDirection.sqrMagnitude > 0.0001f)
                score += Vector3.Dot(direction, desiredDirection);

            if (_obstacleMap != null)
            {
                Vector3 samplePoint = transform.localPosition + direction * Mathf.Max(0.3f, teammateYieldDetectDistance * 0.75f);
                if (_obstacleMap.GetLocalPointTraversibility(samplePoint) == ObstacleMapV2.Traversability.Free)
                    score += 0.5f;
                else
                    score -= 1.0f;
            }

            return score;
        }

        private void ResetTeammateYieldSeparationState()
        {
            _teammateYieldWaitingForSeparation = false;
            _teammateYieldObstacleUntilStep = -1;
            _teammateYieldObstaclePosition = Vector3.zero;
        }

        private bool TryGetTeammateYieldContact(out Vector3 teammatePosition)
        {
            teammatePosition = Vector3.zero;
            if (_agent == null)
                return false;

            var friendlies = _agent.GetFriendlyAgents();
            if (friendlies == null || friendlies.Count == 0)
                return false;

            float bestDistance = float.MaxValue;
            Vector3 myPos = transform.localPosition;

            foreach (var friendly in friendlies)
            {
                if (friendly == null || friendly.gameObject == gameObject)
                    continue;

                Vector3 otherPos = friendly.transform.localPosition;
                float distance = Vector3.Distance(myPos, otherPos);
                if (distance > teammateYieldDetectDistance || distance >= bestDistance)
                    continue;

                bestDistance = distance;
                teammatePosition = otherPos;
            }

            return bestDistance < float.MaxValue;
        }

        private bool IsTeammateYieldBackoffActive()
        {
            return _agent != null && _agent.GetStepsSinceMatchStart() < _teammateYieldBackoffUntilStep;
        }

        private bool IsTeammateYieldObstacleActive()
        {
            return _agent != null && _agent.GetStepsSinceMatchStart() < _teammateYieldObstacleUntilStep;
        }

        private bool ShouldIgnoreTeammateYieldWhileSettled()
        {
            if (_assignedRole != StaticRole.Defend || _lastDecision == null || !_lastDecision.HasTarget)
                return false;

            bool isHoldingPosition =
                _lastDecision.DebugLabel == "MoveToFormation" ||
                _lastDecision.DebugLabel == "HoldDropZone";

            if (!isHoldingPosition)
                return false;

            float settledDistance = Mathf.Max(0.05f, teammateYieldSettledTargetDistance);
            return Vector3.Distance(transform.localPosition, _lastDecision.TargetPosition) <= settledDistance;
        }

        private IEnumerable<Vector3> GetInflatedTeammateYieldObstaclePoints()
        {
            float inflation = Mathf.Max(0f, teammateYieldObstacleInflation);
            float step = _obstacleMap != null ? _obstacleMap.trueScale.x : 0.2f;
            int radiusSteps = Mathf.Max(0, Mathf.CeilToInt(inflation / Mathf.Max(0.01f, step)));

            for (int dx = -radiusSteps; dx <= radiusSteps; dx++)
            {
                for (int dz = -radiusSteps; dz <= radiusSteps; dz++)
                {
                    Vector3 offset = new Vector3(dx * step, 0f, dz * step);
                    if (offset.sqrMagnitude > inflation * inflation)
                        continue;

                    yield return _teammateYieldObstaclePosition + offset;
                }
            }
        }
        
        
        private Vector2 ExecuteInterceptIntruder(BTDecision decision)
        {
            if (decision == null || !decision.HasTarget)
            {
                ClearCurrentPath();
                return Vector2.zero;
            }

            Vector3 interceptTarget = ClampToOwnTerritoryEdge(decision.TargetPosition);
            interceptTarget = SnapToNearestFreePoint(interceptTarget, IsInOwnTerritory, radiusStep: 0.2f, maxRadiusSteps: 24);
            if (_obstacleMap == null ||
                _obstacleMap.GetLocalPointTraversibility(interceptTarget) != ObstacleMapV2.Traversability.Free ||
                !IsInOwnTerritory(interceptTarget))
            {
                Vector3 fallbackTarget = _hasDefenseAnchor ? _defenseAnchor : transform.localPosition;
                interceptTarget = SnapToNearestFreePoint(fallbackTarget, IsInOwnTerritory, radiusStep: 0.2f, maxRadiusSteps: 24);
            }

            if (TryGetCloseIntruderPursuitAcceleration(interceptTarget, out var pursuitAcceleration))
            {
                ClearCurrentPath();
                return pursuitAcceleration;
            }
            
            return MoveToTarget(interceptTarget, arriveDistance: 0.25f, ownTerritoryOnly: true);
        }
        private Vector2 ExecuteBlockCrossing(BTDecision decision)
        {
            if (decision == null || !decision.HasTarget)
            {
                ClearCurrentPath();
                return Vector2.zero;
            }

            return MoveToTarget(
                decision.TargetPosition,
                arriveDistance: 0.35f,
                ownTerritoryOnly: true,
                periodicRepathIntervalSteps: Mathf.Max(1, defenderMirrorRepathIntervalSteps));
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

            return MoveToTarget(decision.TargetPosition, arriveDistance: 0.35f, ownTerritoryOnly: true);
        }
        private Vector2 ExecuteHoldDropZone(BTDecision decision)
        {
            if (decision == null || !decision.HasTarget)
            {
                ClearCurrentPath();
                return Vector2.zero;
            }

            return MoveToTarget(decision.TargetPosition, arriveDistance: 0.30f, ownTerritoryOnly: true);
        }
        private Vector2 ExecuteReturnHome(BTDecision decision)
        {
            if (decision == null || !decision.HasTarget)
            {
                ClearCurrentPath();
                return Vector2.zero;
            }

            return MoveToTarget(
                decision.TargetPosition,
                arriveDistance: 0.30f,
                periodicRepathIntervalSteps: Mathf.Max(1, returnHomeRepathIntervalSteps));
        }
        private Vector2 ExecuteCollectEnemyPills(BTDecision decision)
        {
            if (decision == null || !decision.HasTarget)
            {
                ClearCurrentPath();
                return Vector2.zero;
            }

            return MoveToTarget(
                decision.TargetPosition,
                arriveDistance: 0.20f,
                periodicPillRepath: true);
        }
        private Vector2 ExecuteAttackerCollectSafeMiddlePills(BTDecision decision)
        {
            if (decision == null || !decision.HasTarget)
            {
                ClearCurrentPath();
                return Vector2.zero;
            }

            return MoveToTarget(
                decision.TargetPosition,
                arriveDistance: 0.25f,
                periodicPillRepath: true);
        }
        private Vector2 ExecuteMoveToAttackPosition(BTDecision decision)
        {
            if (decision == null || !decision.HasTarget)
            {
                ClearCurrentPath();
                return Vector2.zero;
            }

            return MoveToTarget(decision.TargetPosition, arriveDistance: 0.35f, ownTerritoryOnly: true);
        }
        private Vector2 ExecutePatrolAttackZone(BTDecision decision)
        {
            if (decision == null || !decision.HasTarget)
            {
                ClearCurrentPath();
                return Vector2.zero;
            }

            return MoveToTarget(decision.TargetPosition, arriveDistance: attackPatrolArriveDistance, ownTerritoryOnly: true);
        }
        private Vector2 ExecuteGrabPowerCapsule(BTDecision decision)
        {
            if (decision == null || !decision.HasTarget)
            {
                ClearCurrentPath();
                return Vector2.zero;
            }

            return MoveToTarget(decision.TargetPosition, arriveDistance: 0.20f, periodicRepathIntervalSteps: capsuleRepathIntervalSteps);
        }
        private Vector2 ExecuteCampNextPowerCapsule(BTDecision decision)
        {
            if (decision == null || !decision.HasTarget)
            {
                ClearCurrentPath();
                return Vector2.zero;
            }

            return MoveToTarget(decision.TargetPosition, arriveDistance: 0.25f, periodicRepathIntervalSteps: capsuleRepathIntervalSteps);
        }
        private Vector2 ExecuteEvade(BTDecision decision, Vector3 velocity)
        {
            return GetEvadeAcceleration(velocity);
        }

        private bool TryGetCloseIntruderPursuitAcceleration(Vector3 trackedTargetPosition, out Vector2 pursuitAcceleration)
        {
            pursuitAcceleration = Vector2.zero;

            Vector3 myPos = transform.localPosition;
            float switchDistance = Mathf.Max(0.1f, defenderPurePursuitSwitchDistance);
            float distToTrackedTarget = Vector3.Distance(myPos, trackedTargetPosition);

            if (distToTrackedTarget > switchDistance)
                return false;

            Vector3 pursuitDirection = (trackedTargetPosition - myPos).normalized;
            pursuitAcceleration = new Vector2(pursuitDirection.x, pursuitDirection.z);
            return true;
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
            if (DebugManager.Instance != null && DebugManager.Instance.path)
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

            if (DebugManager.Instance != null && DebugManager.Instance.middle)
            {
                DrawMiddleGizmos();
            }

            DrawDefenderLaneFoodPileRadiusGizmo();
            
            if (_voronoiPartitioning != null && _currentVoronoi != null)
            {
                // Filter to show only opponent side cells that are safe enough
                int midCellX = Mathf.RoundToInt(_middleInfo.MidXLocal);
                bool isBlueTeam = TeamAssignmentUtil.CheckTeam(gameObject) == Team.Blue;
                System.Func<Vector2Int, bool> opponentSideFilter =
                    isBlueTeam
                        ? cell => cell.x >= midCellX
                        : cell => cell.x < midCellX;
                
                _voronoiPartitioning.DrawVoronoiDebug(_currentVoronoi, _voronoiSafetyThreshold, opponentSideFilter);
            }
        }

        private void OnGUI()
        {
            if (DebugManager.Instance != null && !DebugManager.Instance.agentHud)
                return;

            Camera camera = Camera.main;
            if (camera == null)
                return;

            Vector3 worldAnchor = transform.position + Vector3.up * 1.35f;
            Vector3 screen = camera.WorldToScreenPoint(worldAnchor);
            if (screen.z <= 0f)
                return;

            if (_agentHudStyle == null)
            {
                _agentHudStyle = new GUIStyle(GUI.skin.label);
                _agentHudStyle.alignment = TextAnchor.UpperLeft;
                _agentHudStyle.fontSize = 12;
                _agentHudStyle.wordWrap = true;
                _agentHudStyle.normal.textColor = Color.white;
            }

            string text = $"Role: {_assignedRole}\nMode: {_currentMode}\nReason: {_btReason}";
            const float labelWidth = 220f;
            float labelHeight = _agentHudStyle.CalcHeight(new GUIContent(text), labelWidth);
            Rect rect = new Rect(
                screen.x - labelWidth * 0.5f - 8f,
                Screen.height - screen.y - labelHeight - 18f,
                labelWidth + 16f,
                labelHeight + 10f);

            GUI.color = new Color(0f, 0f, 0f, 0.75f);
            GUI.Box(rect, GUIContent.none);
            GUI.color = Color.white;
            GUI.Label(new Rect(rect.x + 8f, rect.y + 5f, rect.width - 16f, rect.height - 10f), text, _agentHudStyle);
        }

        private void DrawDefenderLaneFoodPileRadiusGizmo()
        {
            if (!drawDefenderLaneFoodPileRadius ||
                _assignedRole != StaticRole.Defend ||
                !_hasDefenseAnchor)
            {
                return;
            }

            float radius = Mathf.Max(0.1f, defenderLaneFoodPileRadius);
            Vector3 center = transform.parent != null
                ? transform.parent.TransformPoint(_defenseAnchor)
                : _defenseAnchor;
            center.y = transform.position.y + 0.08f;

            Gizmos.color = new Color(1f, 0.55f, 0f, 0.9f);
            DrawWireCircleXZ(center, radius, 72);
            Gizmos.DrawSphere(center, 0.12f);

        #if UNITY_EDITOR
            UnityEditor.Handles.color = new Color(1f, 0.55f, 0f, 0.95f);
            UnityEditor.Handles.Label(
                center + Vector3.up * 0.35f,
                $"Lane food seed: {radius:0.##}\nChain: {Mathf.Max(0.1f, defenderLaneFoodPileChainRadius):0.##}"
            );
        #endif
        }

        private static void DrawWireCircleXZ(Vector3 center, float radius, int segments)
        {
            segments = Mathf.Max(8, segments);
            Vector3 previous = center + new Vector3(radius, 0f, 0f);

            for (int i = 1; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                Vector3 next = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                Gizmos.DrawLine(previous, next);
                previous = next;
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
