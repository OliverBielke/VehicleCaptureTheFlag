using System.Collections.Generic;
using System.Linq;
using PacMan.Agent.Map;
using PacMan.Local;
using UnityEngine;

namespace PacMan.Agent.RoleAssignment
{
    public class DefendManager
    {
        public class Assignment
        {
            public int EnemyServerIndex;
            public Vector3 TargetPosition;
            public bool IsVisible;
            public string Reason;
        }

        private readonly RoleAssigner _roleAssigner;

        public DefendManager(RoleAssigner roleAssigner)
        {
            _roleAssigner = roleAssigner;
        }

        public Assignment GetAssignment(PacManAIDebugBT requester)
        {
            if (requester == null)
                return null;

            Team team = TeamAssignmentUtil.CheckTeam(requester.gameObject);
            if (team == Team.Undefined)
                return null;

            var defenders = _roleAssigner
                .GetRegisteredAgentsForTeam(team)
                .Where(agent => agent != null && agent.AssignedRole == StaticRole.Defend)
                .ToList();

            if (defenders.Count == 0)
                return null;

            var trackedIntruders = new Dictionary<int, Assignment>();

            foreach (var defender in defenders)
            {
                var trackedEnemies = defender.GetTrackedEnemies();
                if (trackedEnemies == null)
                    continue;

                foreach (var enemy in trackedEnemies)
                {
                    if (enemy == null || !enemy.HasPosition || enemy.IsGhost)
                        continue;

                    if (!trackedIntruders.TryGetValue(enemy.ServerIndex, out var current))
                    {
                        trackedIntruders[enemy.ServerIndex] = new Assignment
                        {
                            EnemyServerIndex = enemy.ServerIndex,
                            TargetPosition = enemy.Position,
                            IsVisible = enemy.IsVisible,
                            Reason = enemy.IsVisible ? "Assigned visible intruder" : "Assigned particle-filter intruder"
                        };
                        continue;
                    }

                    // Prefer exact visible positions when available, otherwise keep the latest estimate.
                    if (!current.IsVisible || enemy.IsVisible)
                    {
                        current.TargetPosition = enemy.Position;
                        current.IsVisible = enemy.IsVisible;
                        current.Reason = enemy.IsVisible ? "Assigned visible intruder" : "Assigned particle-filter intruder";
                    }
                }
            }

            if (trackedIntruders.Count == 0)
                return null;

            var bestAssignments = new Dictionary<PacManAIDebugBT, Assignment>();
            var intruderCandidates = trackedIntruders.Values
                .SelectMany(intruder => defenders.Select(defender => new
                {
                    Defender = defender,
                    Intruder = intruder,
                    Score = ScoreIntercept(defender, intruder.TargetPosition)
                }))
                .OrderBy(candidate => candidate.Score)
                .ToList();

            var usedDefenders = new HashSet<PacManAIDebugBT>();
            var usedIntruders = new HashSet<int>();

            foreach (var candidate in intruderCandidates)
            {
                if (usedDefenders.Contains(candidate.Defender) || usedIntruders.Contains(candidate.Intruder.EnemyServerIndex))
                    continue;

                usedDefenders.Add(candidate.Defender);
                usedIntruders.Add(candidate.Intruder.EnemyServerIndex);
                bestAssignments[candidate.Defender] = candidate.Intruder;
            }

            return bestAssignments.TryGetValue(requester, out var assignment) ? assignment : null;
        }

        private static float ScoreIntercept(PacManAIDebugBT defender, Vector3 intruderPosition)
        {
            Vector3 defenderPos = defender.transform.localPosition;
            float distanceScore = (defenderPos - intruderPosition).sqrMagnitude;

            if (!defender.HasDefenseAnchor)
                return distanceScore;

            float laneDelta = Mathf.Abs(defender.DefenseAnchor.z - intruderPosition.z);
            return distanceScore + laneDelta * laneDelta * 0.35f;
        }
    }
}
