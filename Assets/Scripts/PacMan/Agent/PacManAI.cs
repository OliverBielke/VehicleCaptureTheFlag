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
        [SerializeField] private float lateGameCapsuleRushSeconds = 45f;
        [SerializeField] private int poweredReturnFoodThreshold = 6;
        [SerializeField] private float nextCapsuleGrabLeadTime = 0.15f;
        [SerializeField] private float consumedCapsuleContactDistance = 0.25f;
        [Header("Retreat")]
        [SerializeField] private float returnHomeOwnSideOffset = 0.8f;
        [SerializeField] private float returnHomeReleaseOwnSideDistance = 1.2f;
        [SerializeField] private float friendlyCapsuleObstacleInflation = 1.0f;
        [SerializeField] private float baseGhostDangerDistance = 2.5f;
        [SerializeField] private float maxGhostDangerDistance = 6.0f;
        [SerializeField] private float ghostDangerHysteresisDistance = 1.0f;
        [SerializeField] private float defenderPurePursuitSwitchDistance = 1.5f;
        [Header("Path Stability")]
        [SerializeField] private int minStepsBetweenRepaths = 12;
        [SerializeField] private float retargetDistanceThreshold = 0.75f;
        [SerializeField] private float targetLockDistance = 0.35f;
        [SerializeField] private int pillRepathIntervalSteps = 20;
        [SerializeField] private int pillUnsafeCellRetryThreshold = 6;
        [SerializeField] private int pillCandidateAttempts = 2;
        [SerializeField] private int capsuleRepathIntervalSteps = 12;
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

        // To track respawns
        private int _previousRespawnStep = -1;
        private int _lastPathPlanStep = -99999;
        private int _lastKnownRespawnStep = -1;
        private bool _attackerThreatRetreatActive = false;
        private int _previousCarriedFoodCount = 0;
        private bool _attackerRegroupAfterReturnHome = false;
        private int _lastPlannedUnsafeCellCount = 0;
        private Vector3 _lastPlannedGoalPosition = Vector3.zero;
        private bool _hasLatchedHomeTarget = false;
        private Vector3 _latchedHomeTarget = Vector3.zero;
        private int _teammateYieldBackoffUntilStep = -1;
        private int _teammateYieldObstacleUntilStep = -1;
        private int _teammateYieldRetriggerBlockedUntilStep = -1;
        private Vector3 _teammateYieldObstaclePosition = Vector3.zero;
        private Vector2 _teammateYieldBackoffAcceleration = Vector2.zero;
        private bool _teammateYieldWaitingForSeparation = false;

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
            
            _voronoiPartitioning = new VoronoiPartitioning(_obstacleMap);
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
            bool isPoweredNow = _agent.IsPoweredUp();

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
            var curPos = _initialDroneState.localPosition;
            var dynamicEnemyObstacles = GetTrackedEnemies()
                .Where(enemy => enemy != null && enemy.HasPosition)
                .Where(enemy => Vector3.Distance(enemy.Position, _goalPosition) > 0.35f)
                .Select(enemy => enemy.Position)
                .Concat(GetInflatedFriendlyCapsuleObstaclePoints())
                .ToList();

            if (IsTeammateYieldObstacleActive() &&
                Vector3.Distance(_teammateYieldObstaclePosition, _goalPosition) > teammateYieldGoalIgnoreRadius)
            {
                dynamicEnemyObstacles.AddRange(GetInflatedTeammateYieldObstaclePoints());
            }

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
                dynamicEnemyObstacles,
                enforceOwnTerritoryPath ? IsInOwnTerritory : null);
            List<Vector3> aStarPath = aStar.PlanPathAStar(curPos, _goalPosition, _currentVoronoi);
            _lastPlannedGoalPosition = _goalPosition;
            _lastPlannedUnsafeCellCount = CountUnsafeCellsOnPath(aStarPath);

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
            _lastPathPlanStep = _agent.GetStepsSinceMatchStart();
            return true;
        }
        
        
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
            
            var visibleEnemies = _agent.GetVisibleEnemyAgents();
    
            if (visibleEnemies == null || visibleEnemies.Count == 0)
            {
                _currentVoronoi = null;
                return;
            }

            bool isBlue = TeamAssignmentUtil.CheckTeam(gameObject) == Team.Blue;
            bool isOnOpponentSide = isBlue ? transform.localPosition.x > 0 : transform.localPosition.x < 0; 

            if (isOnOpponentSide)
            {
                var enemyPositions = visibleEnemies.Select(e => e.transform.position).ToList();
        
                // Pass the agent's position to act as the "Safe" source
                _currentVoronoi = _voronoiPartitioning.ComputeVoronoi(transform.position, enemyPositions);
            }
            else
            {
                _currentVoronoi = null;
            }
        }
        

        private DefenderBlackboard BuildDefenderBlackboard()
        {
            DefenderBlackboard bb = new DefenderBlackboard();

            Vector3 myPos = transform.localPosition;
            var defendAssignment = RoleAssigner.Instance?.DefendManager?.GetAssignment(this);
            var activeFood = _agent.GetFoodObjects().FindAll(f => f.activeSelf &&
                                                TeamAssignmentUtil.CheckTeam(f) != TeamAssignmentUtil.CheckTeam(gameObject));
            bool isPowered = _agent.IsPoweredUp();
            int carriedFood = _agent.GetCarriedFoodCount();
            Vector3 homeTarget = GetClosestHomePoint();

            bb.homeTargetPosition = homeTarget;

            if (isPowered)
            {
                bb.shouldReturnHome = carriedFood >= poweredReturnFoodThreshold;

                var poweredAssignment = RoleAssigner.Instance?.AttackManager?.GetAssignment(this, activeFood, includePoweredDefenders: true);
                if (!bb.shouldReturnHome && poweredAssignment?.FoodTarget != null)
                {
                    bb.shouldLootWhilePowered = true;
                    bb.enemyPillTargetPosition = poweredAssignment.FoodTarget.transform.localPosition;
                    bb.debugReason = "Powered up loot mode";
                }
                else if (bb.shouldReturnHome)
                {
                    bb.debugReason = "Powered loot threshold reached";
                }
            }

            if (!bb.shouldLootWhilePowered && !bb.shouldReturnHome && defendAssignment != null)
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
            UpdateVoronoiData();
            var trackedEnemies = GetTrackedEnemies();
            var activeFood = _agent.GetFoodObjects().FindAll(f => f.activeSelf &&
                                                TeamAssignmentUtil.CheckTeam(f) != TeamAssignmentUtil.CheckTeam(gameObject));
            var activeEnemyCapsules = GetActiveEnemyCapsules();
            float timeRemaining = _agent.GetTimeRemaining();
            bool isPowered = _agent.IsPoweredUp();
            float powerRemaining = Mathf.Max(0f, _agent.GetPowerRemainingDuration());
            int carriedFoodCount = _agent.GetCarriedFoodCount();

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

            float ghostDangerDistance = Mathf.Lerp(
                baseGhostDangerDistance,
                maxGhostDangerDistance,
                Mathf.Clamp01(carriedFoodCount / 5f));

            bool carryingFood = carriedFoodCount >= 1;
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
            Vector3 capsuleTarget = Vector3.zero;
            bool shouldRushPowerCapsule =
                !isPowered &&
                timeRemaining <= lateGameCapsuleRushSeconds &&
                TryGetClosestObjectPosition(myPos, activeEnemyCapsules, out capsuleTarget);

            bool returnHomeReleasedDeepInsideOwnSide =
                carriedFoodCount == 0 &&
                IsDeepEnoughInOwnTerritory(myPos, returnHomeReleaseOwnSideDistance);

            if (shouldRushPowerCapsule)
            {
                _attackerThreatRetreatActive = false;
                _attackerRegroupAfterReturnHome = false;
                ghostNearby = false;
            }

            bool shouldCommitReturnHome =
                (!shouldRushPowerCapsule && carryingFood && !isPowered && ghostInsideEnterRange) ||
                (isPowered && carriedFoodCount >= poweredReturnFoodThreshold);

            bool shouldReturnHomeNow =
                _attackerRegroupAfterReturnHome ||
                shouldCommitReturnHome ||
                (!isPowered && ghostNearby);
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
                (!isPowered && ghostNearby);
            if (!bb.shouldReturnHome)
                _hasLatchedHomeTarget = false;

            bb.homeTargetPosition = homeTarget;

            var attackAssignment = RoleAssigner.Instance?.AttackManager?.GetAssignment(this, activeFood, includePoweredDefenders: isPowered);
            var capsuleCampAssignment = RoleAssigner.Instance?.AttackManager?.GetCapsuleCampAssignment(this, activeEnemyCapsules);
            var capsuleRushAssignment = RoleAssigner.Instance?.AttackManager?.GetCapsuleRushAssignment(this, activeEnemyCapsules);
            GameObject selectedFoodTarget = attackAssignment?.FoodTarget;
            string selectedFoodReason = attackAssignment?.Reason ?? "Safe enemy pill available";

            if (selectedFoodTarget != null && ShouldRetryFoodTarget(selectedFoodTarget.transform.localPosition))
            {
                GameObject alternateFoodTarget = GetAlternativeFoodTarget(activeFood, selectedFoodTarget);
                if (alternateFoodTarget != null)
                {
                    selectedFoodTarget = alternateFoodTarget;
                    selectedFoodReason = $"Assigned pill path too unsafe ({_lastPlannedUnsafeCellCount} unsafe cells), trying alternate";
                }
            }

            if (shouldRushPowerCapsule)
            {
                bb.shouldReturnHome = false;
                bb.shouldGrabPowerCapsule = true;
                bb.powerCapsuleTargetPosition =
                    capsuleRushAssignment?.CapsuleTarget != null
                        ? capsuleRushAssignment.CapsuleTarget.transform.localPosition
                        : capsuleTarget;
            }

            if (isPowered)
            {
                bb.shouldReturnHome = carriedFoodCount >= poweredReturnFoodThreshold;

                if (!bb.shouldReturnHome &&
                    capsuleCampAssignment?.CapsuleTarget != null &&
                    powerRemaining <= Mathf.Max(0f, nextCapsuleGrabLeadTime))
                {
                    bb.shouldGrabPowerCapsule = true;
                    bb.powerCapsuleTargetPosition = capsuleCampAssignment.CapsuleTarget.transform.localPosition;
                }
                else if (!bb.shouldReturnHome && capsuleCampAssignment?.CapsuleTarget != null)
                {
                    bb.shouldCampNextPowerCapsule = true;
                    bb.powerCapsuleCampPosition = GetCapsuleCampPoint(capsuleCampAssignment.CapsuleTarget.transform.localPosition);
                }
                else
                {
                    bb.shouldLootWhilePowered = selectedFoodTarget != null;

                    if (bb.shouldLootWhilePowered)
                    {
                        bb.enemyPillTargetPosition = selectedFoodTarget.transform.localPosition;
                    }
                    else if (!bb.shouldReturnHome &&
                             TryGetClosestObjectPosition(myPos, activeFood, out var fallbackPoweredFoodTarget))
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
            else if (bb.shouldGrabPowerCapsule)
                bb.debugReason = capsuleRushAssignment?.Reason ?? "Late game power capsule rush";
            else if (bb.shouldCampNextPowerCapsule)
                bb.debugReason = capsuleCampAssignment?.Reason ?? "Camp next enemy power capsule";
            else if (bb.shouldReturnHome && isPowered)
                bb.debugReason = "Powered loot threshold reached";
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

            Vector3 retreatPoint = closestHomePoint + new Vector3(
                isBlue ? -returnHomeOwnSideOffset : returnHomeOwnSideOffset,
                0f,
                0f);

            return SnapToNearestFreePoint(retreatPoint);
        }

        private Vector3 GetStableHomeTarget(bool shouldReturnHome)
        {
            if (!shouldReturnHome)
                return GetClosestHomePoint();

            if (_hasLatchedHomeTarget)
                return _latchedHomeTarget;

            _latchedHomeTarget = GetClosestHomePoint();
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

        private List<GameObject> GetActiveFriendlyCapsules()
        {
            var capsules = _agent.GetCapsuleObjects();
            if (capsules == null)
                return new List<GameObject>();

            Team myTeam = TeamAssignmentUtil.CheckTeam(gameObject);
            return capsules
                .Where(capsule =>
                    capsule != null &&
                    capsule.activeSelf &&
                    TeamAssignmentUtil.CheckTeam(capsule) == myTeam)
                .ToList();
        }

        private IEnumerable<Vector3> GetInflatedFriendlyCapsuleObstaclePoints()
        {
            var capsules = GetActiveFriendlyCapsules();
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

        private bool ShouldRetryFoodTarget(Vector3 targetPosition)
        {
            return _lastPlannedUnsafeCellCount > pillUnsafeCellRetryThreshold &&
                   Vector3.Distance(_lastPlannedGoalPosition, targetPosition) <= targetLockDistance;
        }

        private GameObject GetAlternativeFoodTarget(List<GameObject> activeFood, GameObject currentTarget)
        {
            if (activeFood == null || activeFood.Count == 0)
                return null;

            return activeFood
                .Where(food => food != null && food.activeSelf && food != currentTarget)
                .OrderBy(food => Vector3.Distance(transform.localPosition, food.transform.localPosition))
                .Take(Mathf.Max(1, pillCandidateAttempts))
                .FirstOrDefault();
        }

        private int CountUnsafeCellsOnPath(List<Vector3> path)
        {
            if (path == null || path.Count == 0 || _currentVoronoi == null || _obstacleMap == null)
                return 0;

            int unsafeCount = 0;
            foreach (var point in path)
            {
                var cell = _obstacleMap.WorldToCell(point);
                var key = new Vector2Int(cell.x, cell.z);

                if (!_currentVoronoi.TryGetValue(key, out var cellData) || !cellData.IsSafe)
                    unsafeCount++;
            }

            return unsafeCount;
        }

        private Vector3 GetCapsuleCampPoint(Vector3 capsulePosition)
        {
            Team myTeam = TeamAssignmentUtil.CheckTeam(gameObject);
            Vector3 desired = capsulePosition + new Vector3(myTeam == Team.Blue ? -0.8f : 0.8f, 0f, 0f);
            return SnapToNearestFreePoint(desired);
        }

        private Vector3 SnapToNearestFreePoint(Vector3 desired, float radiusStep = 0.2f, int maxRadiusSteps = 8)
        {
            desired.y = 0f;

            if (_obstacleMap != null &&
                _obstacleMap.GetLocalPointTraversibility(desired) == ObstacleMapV2.Traversability.Free)
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
                        _obstacleMap.GetLocalPointTraversibility(candidate) == ObstacleMapV2.Traversability.Free)
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
            int periodicInterval = periodicPillRepath ? pillRepathIntervalSteps : periodicRepathIntervalSteps;
            bool periodicRepathElapsed = periodicInterval > 0 &&
                                         (currentStep - _lastPathPlanStep) >= Mathf.Max(1, periodicInterval);
            float targetShiftDistance = Vector3.Distance(_goalPosition, target);

            bool needNewPath =
                !_hasGoal ||
                (targetShiftDistance > retargetDistanceThreshold && repathCooldownElapsed) ||
                (_hasGoal && periodicRepathElapsed);

            if (needNewPath)
            {
                _goalPosition = target;

                bool pathOk = MakePath(ownTerritoryOnly);
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

        private void TryTriggerTeammateYield()
        {
            if (_agent == null)
                return;

            if (ShouldIgnoreTeammateYieldWhileSettled())
                return;

            float releaseDistance = Mathf.Max(teammateYieldDetectDistance + 0.05f, teammateYieldReleaseDistance);
            if (_teammateYieldWaitingForSeparation)
            {
                if (Vector3.Distance(transform.localPosition, _teammateYieldObstaclePosition) < releaseDistance)
                    return;

                _teammateYieldWaitingForSeparation = false;
            }

            int currentStep = _agent.GetStepsSinceMatchStart();
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
            _teammateYieldBackoffAcceleration = new Vector2(awayNormalized.x, awayNormalized.z);
            _teammateYieldObstaclePosition = teammatePosition;
            _teammateYieldBackoffUntilStep = currentStep + Mathf.Max(1, teammateYieldBackoffSteps);
            _teammateYieldObstacleUntilStep = currentStep + Mathf.Max(1, teammateYieldObstacleSteps);
            _teammateYieldRetriggerBlockedUntilStep = currentStep + Mathf.Max(1, teammateYieldRetriggerCooldownSteps);
            _teammateYieldWaitingForSeparation = true;
            ClearCurrentPath();
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

            if (TryGetCloseIntruderPursuitAcceleration(decision.TargetPosition, out var pursuitAcceleration))
            {
                ClearCurrentPath();
                return pursuitAcceleration;
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

            return MoveToTarget(decision.TargetPosition, arriveDistance: 0.20f, periodicPillRepath: true);
        }
        private Vector2 ExecuteAttackerCollectSafeMiddlePills(BTDecision decision)
        {
            if (decision == null || !decision.HasTarget)
            {
                ClearCurrentPath();
                return Vector2.zero;
            }

            return MoveToTarget(decision.TargetPosition, arriveDistance: 0.25f, periodicPillRepath: true);
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
            
            if (_voronoiPartitioning != null && _currentVoronoi != null)
            {
                _voronoiPartitioning.DrawVoronoiDebug(_currentVoronoi);
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
