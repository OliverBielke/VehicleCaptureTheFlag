using System.Collections.Generic;
using System.Linq;
using PacMan.Agent.Map;
using UnityEngine;

namespace PacMan.Agent.RoleAssignment
{
    public class AttackManager
    {
        public class Assignment
        {
            public GameObject FoodTarget;
            public string Reason;
        }

        public class CapsuleCampAssignment
        {
            public GameObject CapsuleTarget;
            public string Reason;
        }

        private readonly RoleAssigner _roleAssigner;

        public AttackManager(RoleAssigner roleAssigner)
        {
            _roleAssigner = roleAssigner;
        }

        public Assignment GetAssignment(PacManAIDebugBT requester, List<GameObject> activeEnemyFood, bool includePoweredDefenders = false)
        {
            if (requester == null || activeEnemyFood == null || activeEnemyFood.Count == 0)
                return null;

            Team team = TeamAssignmentUtil.CheckTeam(requester.gameObject);
            if (team == Team.Undefined)
                return null;

            var attackers = _roleAssigner
                .GetRegisteredAgentsForTeam(team)
                .Where(agent =>
                    agent != null &&
                    (agent.AssignedRole == StaticRole.Attack ||
                     (includePoweredDefenders && agent.AgentManager != null && agent.AgentManager.IsPoweredUp())))
                .ToList();

            if (attackers.Count == 0)
                return null;

            var foodCandidates = activeEnemyFood
                .Where(food => food != null && food.activeSelf)
                .Distinct()
                .ToList();

            if (foodCandidates.Count == 0)
                return null;

            var scoredAssignments = attackers
                .SelectMany(attacker => foodCandidates.Select(food => new
                {
                    Attacker = attacker,
                    Food = food,
                    Score = ScoreFood(attacker, food)
                }))
                .OrderBy(candidate => candidate.Score)
                .ToList();

            var bestAssignments = new Dictionary<PacManAIDebugBT, Assignment>();
            var usedAttackers = new HashSet<PacManAIDebugBT>();
            var usedFood = new HashSet<GameObject>();

            foreach (var candidate in scoredAssignments)
            {
                if (usedAttackers.Contains(candidate.Attacker) || usedFood.Contains(candidate.Food))
                    continue;

                usedAttackers.Add(candidate.Attacker);
                usedFood.Add(candidate.Food);
                bestAssignments[candidate.Attacker] = new Assignment
                {
                    FoodTarget = candidate.Food,
                    Reason = "Assigned unique enemy pill"
                };
            }

            return bestAssignments.TryGetValue(requester, out var assignment) ? assignment : null;
        }

        public CapsuleCampAssignment GetCapsuleCampAssignment(PacManAIDebugBT requester, List<GameObject> activeEnemyCapsules)
        {
            if (requester == null || activeEnemyCapsules == null || activeEnemyCapsules.Count == 0)
                return null;

            Team team = TeamAssignmentUtil.CheckTeam(requester.gameObject);
            if (team == Team.Undefined)
                return null;

            var attackers = _roleAssigner
                .GetRegisteredAgentsForTeam(team)
                .Where(agent => agent != null && agent.AssignedRole == StaticRole.Attack)
                .ToList();

            if (attackers.Count < 2)
                return null;

            var capsuleCandidates = activeEnemyCapsules
                .Where(capsule => capsule != null && capsule.activeSelf)
                .Distinct()
                .ToList();

            if (capsuleCandidates.Count == 0)
                return null;

            var bestCandidate = attackers
                .SelectMany(attacker => capsuleCandidates.Select(capsule => new
                {
                    Attacker = attacker,
                    Capsule = capsule,
                    Score = (attacker.transform.localPosition - capsule.transform.localPosition).sqrMagnitude
                }))
                .OrderBy(candidate => candidate.Score)
                .FirstOrDefault();

            if (bestCandidate == null || bestCandidate.Attacker != requester)
                return null;

            return new CapsuleCampAssignment
            {
                CapsuleTarget = bestCandidate.Capsule,
                Reason = "Camp next enemy power capsule"
            };
        }

        private static float ScoreFood(PacManAIDebugBT attacker, GameObject food)
        {
            Vector3 attackerPos = attacker.transform.localPosition;
            Vector3 foodPos = food.transform.localPosition;
            float distanceScore = (attackerPos - foodPos).sqrMagnitude;

            Vector3 anchor = Vector3.zero;
            bool hasAnchor = false;

            if (attacker.HasAttackAnchor)
            {
                anchor = attacker.AttackAnchor;
                hasAnchor = true;
            }
            else if (attacker.HasDefenseAnchor)
            {
                anchor = attacker.DefenseAnchor;
                hasAnchor = true;
            }

            if (!hasAnchor)
                return distanceScore;

            float laneDelta = Mathf.Abs(anchor.z - foodPos.z);
            float anchorDelta = (anchor - foodPos).sqrMagnitude;

            // Recompute greedily from live positions so attackers can swap pills
            // when one becomes clearly closer, while still using the lane anchor
            // as a gentle tie-breaker to avoid unnecessary overlap.
            return distanceScore + laneDelta * laneDelta * 0.08f + anchorDelta * 0.03f;
        }
    }
}
