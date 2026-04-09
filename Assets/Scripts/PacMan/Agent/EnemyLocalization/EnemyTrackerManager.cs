using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using PacMan.Local;
using PacMan.Interface.PacMan;
using Scripts.Map;

namespace PacMan.Agent.EnemyLocalization
{
    public class EnemyTrackerManager : MonoBehaviour
    {
        public static EnemyTrackerManager Instance { get; private set; }

        [Header("Particle Filter Settings")]
        [SerializeField] private int particleCount = 300;
        [SerializeField] private float motionNoiseStd = 0.35f;
        [SerializeField] private float maxStepDistance = 2.0f;
        [SerializeField] private float resampleJitter = 0.2f;
        [SerializeField] private float minWeight = 1e-6f;
        [SerializeField] private float exactObservationNoise = 0.05f;

        // New:
        [SerializeField] private float velocityNoiseStd = 0.2f;
        [SerializeField] private float maxParticleSpeed = 2.1f;
        [SerializeField] private float initialSpeedStd = 0.5f;
        [SerializeField] [Range(0f, 1f)] private float velocityPersistence = 0.95f;

        [Header("Motion Trend Settings")]
        [SerializeField] private float maxReasonableSpeed = 2.1f;
        [SerializeField] [Range(0f, 1f)] private float velocitySmoothingOldWeight = 0.7f;
        [SerializeField] [Range(0f, 1f)] private float velocitySmoothingNewWeight = 0.3f;

        [Header("Tracker Bounds")]
        [SerializeField] private Vector3 boundsCenter = Vector3.zero;
        [SerializeField] private Vector3 boundsSize = new Vector3(60f, 2f, 60f);

        [Header("Auto Source Selection")]
        [SerializeField] private PacManAgentManager trackingSourceOverride;
        [SerializeField] private bool autoFindTrackingSource = true;

        [Header("Debug")]
        [SerializeField] private bool drawDebug = true;
        [SerializeField] private float particleRadius = 0.05f;
        [SerializeField] private float estimateRadius = 0.2f;
        [SerializeField] private bool logTrackingSource = false;
        [SerializeField] private MapManager mapManager;

        private ObstacleMapV2 _obstacleMap;
        private readonly Dictionary<int, ParticleFilter> _enemyFilters = new Dictionary<int, ParticleFilter>();
        private readonly Dictionary<int, EnemyTrackState> _trackStates = new Dictionary<int, EnemyTrackState>();

        private Bounds _pfBounds;
        private bool _initialized = false;
        private System.Func<Vector3, bool> _isTraversable;
        private PacManAgentManager _trackingSource;

        [System.Serializable]
        public class EnemyTrackState
        {
            public Vector3 LastObservationPosition;
            public Vector3 PreviousObservationPosition;
            public float LastObservationTime;
            public Vector3 EstimatedVelocity;
            public bool HasObservationHistory;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void Start()
        {
            InitializeIfNeeded();
        }

        private void FixedUpdate()
        {
            InitializeIfNeeded();

            if (autoFindTrackingSource)
            {
                RefreshTrackingSource();
            }

            if (_trackingSource == null)
                return;

            var visibleEnemyAgents = _trackingSource.GetVisibleEnemyAgents();
            var observations = _trackingSource.GetEnemyObservations();

            UpdateTracker(visibleEnemyAgents, observations);
        }

        private void InitializeIfNeeded()
        {
            if (_initialized)
                return;

            _pfBounds = new Bounds(boundsCenter, boundsSize);

            if (mapManager == null)
            {
                mapManager = FindFirstObjectByType<MapManager>();
            }

            if (mapManager != null)
            {
                _obstacleMap = ObstacleMapV2.Initialize(
                    mapManager,
                    new List<GameObject>(),
                    new Vector3(0.2f, 1f, 0.2f),
                    new Vector3(1f, 1f, 1f),
                    0
                );

                _isTraversable = IsTraversable;
            }
            else
            {
                _isTraversable = null;
            }

            _enemyFilters.Clear();
            _trackStates.Clear();
            _initialized = true;
        }

        private void RefreshTrackingSource()
        {
            if (trackingSourceOverride != null && trackingSourceOverride.gameObject.activeInHierarchy)
            {
                _trackingSource = trackingSourceOverride;
                return;
            }

            if (_trackingSource != null && _trackingSource.gameObject.activeInHierarchy)
                return;

            var allAgents = FindObjectsByType<PacManAgentManager>(FindObjectsSortMode.None);

            _trackingSource = allAgents
                .Where(a => a != null && a.gameObject.activeInHierarchy)
                .OrderBy(a => a.serverIndex)
                .FirstOrDefault();

            if (logTrackingSource && _trackingSource != null)
            {
                Debug.Log($"EnemyTrackerManager source: {_trackingSource.name}, tag={_trackingSource.tag}, serverIndex={_trackingSource.serverIndex}");
            }
        }

        private void UpdateTrackStateFromObservation(int enemyId, Vector3 obsPosition, float obsTime)
        {
            if (!_trackStates.TryGetValue(enemyId, out var state))
            {
                state = new EnemyTrackState
                {
                    LastObservationPosition = obsPosition,
                    PreviousObservationPosition = obsPosition,
                    LastObservationTime = obsTime,
                    EstimatedVelocity = Vector3.zero,
                    HasObservationHistory = false
                };

                _trackStates[enemyId] = state;
                return;
            }

            float dt = obsTime - state.LastObservationTime;

            state.PreviousObservationPosition = state.LastObservationPosition;
            state.LastObservationPosition = obsPosition;

            if (dt > 1e-4f)
            {
                Vector3 measuredVelocity = (state.LastObservationPosition - state.PreviousObservationPosition) / dt;
                measuredVelocity.y = 0f;

                if (measuredVelocity.magnitude > maxReasonableSpeed)
                {
                    measuredVelocity = measuredVelocity.normalized * maxReasonableSpeed;
                }

                float total = velocitySmoothingOldWeight + velocitySmoothingNewWeight;
                float oldW = total > 1e-6f ? velocitySmoothingOldWeight / total : 0.7f;
                float newW = total > 1e-6f ? velocitySmoothingNewWeight / total : 0.3f;

                state.EstimatedVelocity = oldW * state.EstimatedVelocity + newW * measuredVelocity;
                state.HasObservationHistory = true;
            }

            state.LastObservationTime = obsTime;
        }

        public void SetTrackingSource(PacManAgentManager source)
        {
            _trackingSource = source;
        }

        public PacManAgentManager GetTrackingSource()
        {
            return _trackingSource;
        }

        public void UpdateTracker(List<PacManAgentManager> visibleEnemyAgents, PacManObservations observations)
        {
            HashSet<int> visibleEnemyIds = new HashSet<int>();
            if (!_initialized)
                return;

            float now = Time.fixedTime;
            float dt = Time.fixedDeltaTime;

            foreach (var kv in _enemyFilters)
            {
                int enemyId = kv.Key;
                var pf = kv.Value;

                Vector3 drift = Vector3.zero;
                if (_trackStates.TryGetValue(enemyId, out var state) && state.HasObservationHistory)
                {
                    drift = state.EstimatedVelocity;
                }

                pf.Predict(drift, dt);
            }

            if (visibleEnemyAgents != null)
            {
                foreach (var enemy in visibleEnemyAgents)
                {
                    if (enemy == null) continue;
                    
                    int enemyId = enemy.serverIndex;
                    Vector3 exactPos = enemy.transform.localPosition;
                    visibleEnemyIds.Add(enemyId);
                    ParticleFilter pf = GetOrCreateFilter(enemyId, exactPos);
                    UpdateTrackStateFromObservation(enemyId, exactPos, now);
                    pf.UpdateWithExactObservation(exactPos);
                }
            }

            if (observations.Observations != null)
            {
                foreach (var obs in observations.Observations)
                {
                    if (obs.Visible)
                        continue;

                    if (obs.Position == Vector3.zero)
                        continue;

                    int enemyId = obs.ServerIndex;
                    Vector3 correctedObsPos = CorrectNoisyObservationPosition(obs.Position);

                    ParticleFilter pf = GetOrCreateFilter(enemyId, correctedObsPos);
                    UpdateTrackStateFromObservation(enemyId, correctedObsPos, now);
                    pf.UpdateWithNoisyObservation(correctedObsPos, obs.ReadingDispersion);
                }
            }
            // foreach (var kv in _enemyFilters)
            // {
            //     int enemyId = kv.Key;
            //     ParticleFilter pf = kv.Value;

            //     // If currently visible, do not apply hidden-state LoS exclusion
            //     if (visibleEnemyIds.Contains(enemyId))
            //         continue;

            //     pf.PruneParticles(pos => !IsPointInAnyFriendlyLoS(pos));
            // }
        }

        public bool TryGetEstimate(int enemyId, out Vector3 estimate)
        {
            estimate = Vector3.zero;

            if (!_enemyFilters.TryGetValue(enemyId, out var pf))
                return false;

            estimate = SnapEstimateToTraversable(pf.GetEstimatedPosition());
            return true;
        }

        public Dictionary<int, Vector3> GetAllEstimates()
        {
            Dictionary<int, Vector3> result = new Dictionary<int, Vector3>();

            foreach (var kv in _enemyFilters)
            {
                result[kv.Key] = SnapEstimateToTraversable(kv.Value.GetEstimatedPosition());
            }

            return result;
        }

        public IReadOnlyDictionary<int, ParticleFilter> GetAllFilters()
        {
            return _enemyFilters;
        }

        public bool HasFilter(int enemyId)
        {
            return _enemyFilters.ContainsKey(enemyId);
        }

        public void ClearAll()
        {
            _enemyFilters.Clear();
            _trackStates.Clear();
        }

        private ParticleFilter GetOrCreateFilter(int enemyId, Vector3 initialGuess)
        {
            if (_enemyFilters.TryGetValue(enemyId, out var pf))
                return pf;

            pf = new ParticleFilter
            {
                ParticleCount = particleCount,
                MotionNoiseStd = motionNoiseStd,
                MaxStepDistance = maxStepDistance,
                ResampleJitter = resampleJitter,
                MinWeight = minWeight,
                ExactObservationNoise = exactObservationNoise,

                VelocityNoiseStd = velocityNoiseStd,
                MaxSpeed = maxParticleSpeed,
                InitialSpeedStd = initialSpeedStd,
                VelocityPersistence = velocityPersistence
            };

            Vector3 initialVelocity = Vector3.zero;
            if (_trackStates.TryGetValue(enemyId, out var state) && state.HasObservationHistory)
            {
                initialVelocity = state.EstimatedVelocity;
            }

            pf.Initialize(_pfBounds, _isTraversable, initialGuess, initialVelocity);
            _enemyFilters[enemyId] = pf;
            return pf;
        }
        private bool IsPointVisibleFromAgent(PacManAgentManager agent, Vector3 pointLocal)
        {
            Vector3 origin = agent.transform.position;
            Vector3 target = agent.PacManGameManager.transform.TransformPoint(pointLocal);

            Vector3 dir = target - origin;
            float dist = dir.magnitude;

            if (dist <= 1e-4f)
                return true;

            dir /= dist;

            int obstacleMask = LayerMask.GetMask("Obstacle");
            bool blocked = Physics.Raycast(origin, dir, dist, obstacleMask, QueryTriggerInteraction.Ignore);

            return !blocked;
        }
        private bool IsPointInAnyFriendlyLoS(Vector3 point)
        {
            if (_trackingSource == null)
                return false;

            var friendlyAgents = _trackingSource.GetTeamAgents();
            if (friendlyAgents == null)
                return false;

            foreach (var agent in friendlyAgents)
            {
                if (agent == null || !agent.gameObject.activeInHierarchy)
                    continue;

                if (IsPointVisibleFromAgent(agent, point))
                    return true;
            }

            return false;
        }
        private Vector3 SnapToNearestFreeSpace(Vector3 point)
        {
            point = ClampPointToTrackerBounds(point);

            if (IsTraversable(point))
                return point;

            float maxRadius = 4f;
            float radiusStep = 1f;
            int angleSteps = 8;

            Vector3 bestPoint = point;
            float bestDistSq = float.MaxValue;
            bool found = false;

            for (float r = radiusStep; r <= maxRadius; r += radiusStep)
            {
                for (int a = 0; a < angleSteps; a++)
                {
                    float angle = (2f * Mathf.PI * a) / angleSteps;

                    Vector3 candidate = new Vector3(
                        point.x + Mathf.Cos(angle) * r,
                        0f,
                        point.z + Mathf.Sin(angle) * r
                    );

                    candidate = ClampPointToTrackerBounds(candidate);

                    if (!IsTraversable(candidate))
                        continue;

                    float distSq = (candidate - point).sqrMagnitude;
                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        bestPoint = candidate;
                        found = true;
                    }
                }

                if (found)
                    return bestPoint;
            }

            return point;
        }

        private Vector3 ClampPointToTrackerBounds(Vector3 p)
        {
            return new Vector3(
                Mathf.Clamp(p.x, _pfBounds.min.x, _pfBounds.max.x),
                0f,
                Mathf.Clamp(p.z, _pfBounds.min.z, _pfBounds.max.z)
            );
        }

        private Vector3 CorrectNoisyObservationPosition(Vector3 rawObservation)
        {
            Vector3 clamped = ClampPointToTrackerBounds(rawObservation);

            if (_obstacleMap == null)
                return clamped;

            return SnapToNearestFreeSpace(clamped);
        }

        private Vector3 SnapEstimateToTraversable(Vector3 estimate)
        {
            Vector3 clamped = ClampPointToTrackerBounds(estimate);

            if (_obstacleMap == null)
                return clamped;

            return SnapToNearestFreeSpace(clamped);
        }



        private bool IsTraversable(Vector3 p)
        {
            if (_obstacleMap == null)
                return true;
            return _obstacleMap.GetLocalPointTraversibility(p) == ObstacleMapV2.Traversability.Free;
        }

        private void OnDrawGizmos()
        {
            if (!drawDebug)
                return;

            Gizmos.color = Color.white;
            Gizmos.DrawWireCube(boundsCenter, boundsSize);

            foreach (var kv in _enemyFilters)
            {
                var pf = kv.Value;
                if (pf == null || !pf.IsInitialized)
                    continue;

                Gizmos.color = Color.magenta;
                foreach (var particle in pf.Particles)
                {
                    Gizmos.DrawSphere(particle.Position + Vector3.up * 0.15f, particleRadius);
                }

                Gizmos.color = Color.green;
                Vector3 est = pf.GetEstimatedPosition();
                Gizmos.DrawSphere(est + Vector3.up * 0.3f, estimateRadius);
            }
        }
    }
}
