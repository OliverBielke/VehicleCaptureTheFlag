using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using PacMan;
using PacMan.Agent.Map;
using Scripts.Map;

namespace PacMan.Agent.RoleAssignment
{
    public class RoleAssigner : MonoBehaviour
    {
        public static RoleAssigner Instance { get; private set; }

        [Header("Scene References")]
        [SerializeField] private MapManager mapManager;

        [Header("Map Analysis")]
        [SerializeField] private float gridSize = 0.2f;
        [SerializeField] private bool useMajorLanesOnlyForDefense = true;

        [Header("Timing")]
        [SerializeField] private float settleTime = 0.5f;
        [SerializeField] private bool verboseLogs = true;

        private readonly List<PacManAIDebugBT> _registeredAgents = new();
        private ObstacleMapV2 _obstacleMap;
        private MapMiddleAnalyzer _middleAnalyzer;
        private MapMiddleAnalyzer.MiddleInfo _middleInfo;

        private Coroutine _assignCoroutine;

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
            if (mapManager == null)
            {
                mapManager = FindObjectOfType<MapManager>();
            }

            if (mapManager == null)
            {
                Debug.LogError("RoleAssigner: MapManager not found.");
                enabled = false;
                return;
            }

            _obstacleMap = ObstacleMapV2.Initialize(
                mapManager,
                new List<GameObject>(),
                new Vector3(gridSize, 1f, gridSize),
                new Vector3(1f, 1f, 1f)
            );

            _middleAnalyzer = new MapMiddleAnalyzer(_obstacleMap);
            _middleInfo = _middleAnalyzer.Analyze();

            if (verboseLogs)
                Debug.Log($"RoleAssigner: initialized. Lane count = {_middleInfo.LaneCount}");
        }

        public void RegisterAgent(PacManAIDebugBT agent)
        {
            if (agent == null || _registeredAgents.Contains(agent))
                return;

            _registeredAgents.Add(agent);

            if (verboseLogs)
                Debug.Log($"RoleAssigner: registered {agent.name} team={TeamAssignmentUtil.CheckTeam(agent.gameObject)}");

            if (_assignCoroutine != null)
                StopCoroutine(_assignCoroutine);

            _assignCoroutine = StartCoroutine(AssignAfterSettle());
        }

        public void UnregisterAgent(PacManAIDebugBT agent)
        {
            if (agent == null)
                return;

            _registeredAgents.Remove(agent);
        }

        private IEnumerator AssignAfterSettle()
        {
            yield return new WaitForSeconds(settleTime);
            AssignRolesFromRegisteredAgents();
        }

        private void AssignRolesFromRegisteredAgents()
        {
            var validAgents = _registeredAgents
                .Where(a => a != null && a.gameObject != null && a.isActiveAndEnabled)
                .ToList();

            if (verboseLogs)
                Debug.Log($"RoleAssigner: assigning roles to {validAgents.Count} registered agents");

            if (validAgents.Count == 0)
                return;

            var groupedByTeam = validAgents
                .Where(a => TeamAssignmentUtil.CheckTeam(a.gameObject) != Team.Undefined)
                .GroupBy(a => TeamAssignmentUtil.CheckTeam(a.gameObject));

            foreach (var teamGroup in groupedByTeam)
            {
                var teamAgents = teamGroup.ToList();

                if (verboseLogs)
                    Debug.Log($"RoleAssigner: team {teamGroup.Key} has {teamAgents.Count} agents");

                AssignRolesForTeam(teamAgents);
            }
        }

        private void AssignRolesForTeam(List<PacManAIDebugBT> team)
        {
            if (team == null || team.Count == 0)
                return;

            int attackerCount = GetAttackerCount(team.Count);

            var sortedByMiddleDistance = team
                .OrderBy(ai => Mathf.Abs(ai.transform.localPosition.x - _middleInfo.MidXLocal))
                .ThenBy(ai => ai.transform.localPosition.z)
                .ToList();

            var attackers = sortedByMiddleDistance.Take(attackerCount).ToList();
            var defenders = sortedByMiddleDistance.Skip(attackerCount).ToList();

            foreach (var attacker in attackers)
            {
                attacker.SetAssignedRole(StaticRole.Attack);
                attacker.ClearDefenseAnchor();

                if (verboseLogs)
                    Debug.Log($"RoleAssigner: {attacker.name} -> ATTACK");
            }

            foreach (var defender in defenders)
            {
                defender.SetAssignedRole(StaticRole.Defend);

                if (verboseLogs)
                    Debug.Log($"RoleAssigner: {defender.name} -> DEFEND");
            }

            AssignDefenseAnchors(defenders);
        }

        private int GetAttackerCount(int teamSize)
        {
            if (teamSize >= 4) return 2;
            if (teamSize == 3) return 1;
            return 1;
        }

        private void AssignDefenseAnchors(List<PacManAIDebugBT> defenders)
        {
            if (defenders == null || defenders.Count == 0)
                return;

            var lanes = useMajorLanesOnlyForDefense
                ? MapMiddleAnalyzer.GetMajorLanes(_middleInfo)
                : MapMiddleAnalyzer.GetLanesOrdered(_middleInfo);

            if (lanes == null || lanes.Count == 0)
                lanes = MapMiddleAnalyzer.GetLanesOrdered(_middleInfo);

            if (lanes == null || lanes.Count == 0)
            {
                Debug.LogWarning("RoleAssigner: no lanes available for defense anchors.");
                return;
            }

            var orderedLanes = lanes.OrderBy(l => l.MidCenterLocal.z).ToList();

            if (defenders.Count == 1)
            {
                Vector3 anchor = orderedLanes[orderedLanes.Count / 2].MidCenterLocal;
                defenders[0].SetDefenseAnchor(anchor);
                Debug.Log($"{defenders[0].name} defense anchor -> {anchor}");
                return;
            }

            if (defenders.Count == 2)
            {
                Vector3 bottomAnchor = orderedLanes.First().MidCenterLocal;
                Vector3 topAnchor = orderedLanes.Last().MidCenterLocal;

                defenders[0].SetDefenseAnchor(bottomAnchor);
                defenders[1].SetDefenseAnchor(topAnchor);

                Debug.Log($"{defenders[0].name} defense anchor -> {bottomAnchor}");
                Debug.Log($"{defenders[1].name} defense anchor -> {topAnchor}");
                return;
            }

            for (int i = 0; i < defenders.Count; i++)
            {
                int laneIndex = Mathf.RoundToInt((float)i / (defenders.Count - 1) * (orderedLanes.Count - 1));
                Vector3 anchor = orderedLanes[laneIndex].MidCenterLocal;
                defenders[i].SetDefenseAnchor(anchor);
                Debug.Log($"{defenders[i].name} defense anchor -> {anchor}");
            }
        }
    }
}