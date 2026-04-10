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
        
        // Used in PacManAI to set the same Obstacle Map for all classes
        public void SetObstacleMap(ObstacleMapV2 map)
        {
            // Ensure we only initialize this once, as multiple agents will try to inject it
            if (_obstacleMap != null) 
                return;

            _obstacleMap = map;

            // Generate the middle info now that we have the map
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

            var availableLanes = lanes.OrderBy(l => l.MidCenterLocal.z).ToList();

            // 1 defender -> middle lane
            if (defenders.Count == 1)
            {
                var defender = defenders[0];
                Team team = TeamAssignmentUtil.CheckTeam(defender.gameObject);

                var middleLane = availableLanes[availableLanes.Count / 2];
                Vector3 anchor = GetDefenseAnchorForLane(middleLane, team, 2f);

                defender.SetDefenseAnchor(anchor);
                Debug.Log($"{defender.name} defense anchor -> {anchor} | lane={middleLane.Label}");
                return;
            }

            // 2 defenders -> always cover bottom and top, choose best pairing
            if (defenders.Count == 2 && availableLanes.Count >= 2)
            {
                var bottomLane = availableLanes.First();
                var topLane = availableLanes.Last();

                var d0 = defenders[0];
                var d1 = defenders[1];

                Team t0 = TeamAssignmentUtil.CheckTeam(d0.gameObject);
                Team t1 = TeamAssignmentUtil.CheckTeam(d1.gameObject);

                Vector3 d0BottomAnchor = GetDefenseAnchorForLane(bottomLane, t0, 2f);
                Vector3 d0TopAnchor = GetDefenseAnchorForLane(topLane, t0, 2f);
                Vector3 d1BottomAnchor = GetDefenseAnchorForLane(bottomLane, t1, 2f);
                Vector3 d1TopAnchor = GetDefenseAnchorForLane(topLane, t1, 2f);

                float pairingA =
                    (d0.transform.localPosition - d0BottomAnchor).sqrMagnitude +
                    (d1.transform.localPosition - d1TopAnchor).sqrMagnitude;

                float pairingB =
                    (d0.transform.localPosition - d0TopAnchor).sqrMagnitude +
                    (d1.transform.localPosition - d1BottomAnchor).sqrMagnitude;

                if (pairingA <= pairingB)
                {
                    d0.SetDefenseAnchor(d0BottomAnchor);
                    d1.SetDefenseAnchor(d1TopAnchor);

                    Debug.Log($"{d0.name} defense anchor -> {d0BottomAnchor} | lane={bottomLane.Label}");
                    Debug.Log($"{d1.name} defense anchor -> {d1TopAnchor} | lane={topLane.Label}");
                }
                else
                {
                    d0.SetDefenseAnchor(d0TopAnchor);
                    d1.SetDefenseAnchor(d1BottomAnchor);

                    Debug.Log($"{d0.name} defense anchor -> {d0TopAnchor} | lane={topLane.Label}");
                    Debug.Log($"{d1.name} defense anchor -> {d1BottomAnchor} | lane={bottomLane.Label}");
                }

                return;
            }

            // 3+ defenders -> greedy closest unique lane assignment
            var assignments = new List<(PacManAIDebugBT defender, MapMiddleAnalyzer.Lane lane, float distSqr)>();

            foreach (var defender in defenders)
            {
                Team team = TeamAssignmentUtil.CheckTeam(defender.gameObject);

                foreach (var lane in availableLanes)
                {
                    Vector3 candidateAnchor = GetDefenseAnchorForLane(lane, team, 2f);
                    float distSqr = (defender.transform.localPosition - candidateAnchor).sqrMagnitude;
                    assignments.Add((defender, lane, distSqr));
                }
            }

            var usedDefenders = new HashSet<PacManAIDebugBT>();
            var usedLanes = new HashSet<MapMiddleAnalyzer.Lane>();

            foreach (var candidate in assignments.OrderBy(a => a.distSqr))
            {
                if (usedDefenders.Contains(candidate.defender) || usedLanes.Contains(candidate.lane))
                    continue;

                Team team = TeamAssignmentUtil.CheckTeam(candidate.defender.gameObject);
                Vector3 anchor = GetDefenseAnchorForLane(candidate.lane, team, 2f);

                candidate.defender.SetDefenseAnchor(anchor);
                usedDefenders.Add(candidate.defender);
                usedLanes.Add(candidate.lane);

                Debug.Log($"{candidate.defender.name} defense anchor -> {anchor} | lane={candidate.lane.Label}");
            }

            // Fallback if there are more defenders than lanes
            foreach (var defender in defenders)
            {
                if (usedDefenders.Contains(defender))
                    continue;

                Team team = TeamAssignmentUtil.CheckTeam(defender.gameObject);

                var closestLane = availableLanes
                    .OrderBy(l =>
                    {
                        Vector3 anchor = GetDefenseAnchorForLane(l, team, 2f);
                        return (defender.transform.localPosition - anchor).sqrMagnitude;
                    })
                    .First();

                Vector3 fallbackAnchor = GetDefenseAnchorForLane(closestLane, team, 2f);
                defender.SetDefenseAnchor(fallbackAnchor);

                Debug.Log($"{defender.name} fallback defense anchor -> {fallbackAnchor} | lane={closestLane.Label}");
            }
        }
        private Vector3 GetDefenseAnchorForLane(MapMiddleAnalyzer.Lane lane, Team team, float xOffset = 1.2f)
        {
            Vector3 anchor = lane.MidCenterLocal;
            anchor.y = 0f;

            if (team == Team.Blue)
                anchor.x -= xOffset;
            else if (team == Team.Red)
                anchor.x += xOffset;

            return FindNearestFreePoint(anchor, team);
        }

        private Vector3 FindNearestFreePoint(Vector3 desired, Team team, float step = 0.2f, int maxSteps = 12)
        {
            desired.y = 0f;

            if (_obstacleMap.GetLocalPointTraversibility(desired) == ObstacleMapV2.Traversability.Free)
                return desired;

            for (int i = 1; i <= maxSteps; i++)
            {
                float dx = i * step;

                Vector3 towardOwnSide = desired + new Vector3(team == Team.Blue ? -dx : dx, 0f, 0f);
                Vector3 towardOtherSide = desired + new Vector3(team == Team.Blue ? dx : -dx, 0f, 0f);

                if (_obstacleMap.GetLocalPointTraversibility(towardOwnSide) == ObstacleMapV2.Traversability.Free)
                    return towardOwnSide;

                if (_obstacleMap.GetLocalPointTraversibility(towardOtherSide) == ObstacleMapV2.Traversability.Free)
                    return towardOtherSide;
            }

            return desired;
        }

        private Vector3 laneFallback(Vector3 p)
        {
            p.y = 0f;
            return p;
        }
    }
}