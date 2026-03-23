using System;
using System.Collections.Generic;
using UnityEngine;

namespace PacMan
{
    public enum Team
    {
        Red,
        Blue,
        Undefined
    }

    public static class TeamAssignmentUtil
    {
        public const int ExpectedNetworkClientCount = 2;

        public static Team CheckTeam(GameObject toCheck)
        {
            if (toCheck.CompareTag("Red")) return Team.Red;
            if (toCheck.CompareTag("Blue")) return Team.Blue;
            if (toCheck.transform.localPosition.x < 0) return Team.Blue; //TODO: Use collider zones instead? Performance?
            if (toCheck.transform.localPosition.x > 0) return Team.Red;
            return Team.Undefined;
        }

        public static List<int> GetControlledAgentIndicesForClientSlot(IReadOnlyList<string> agentTags, int clientSlot, int expectedClientCount = ExpectedNetworkClientCount)
        {
            if (clientSlot < 0)
            {
                return new List<int>();
            }

            var orderedTeamTags = GetOrderedTeamTags(agentTags);
            if (clientSlot < orderedTeamTags.Count)
            {
                return GetAgentIndicesForTeamTag(agentTags, orderedTeamTags[clientSlot]);
            }

            return GetFallbackAgentIndices(agentTags?.Count ?? 0, clientSlot, expectedClientCount);
        }

        public static List<int> GetControlledAgentIndicesForAnchorAgent(IReadOnlyList<string> agentTags, int anchorAgentIndex)
        {
            if (!TryGetControlledTeamTag(agentTags, anchorAgentIndex, out var controlledTeamTag))
            {
                return new List<int>();
            }

            return GetAgentIndicesForTeamTag(agentTags, controlledTeamTag);
        }

        public static int GetControlledAgentAnchorForClientSlot(IReadOnlyList<string> agentTags, int clientSlot, int expectedClientCount = ExpectedNetworkClientCount)
        {
            var controlledAgentIndices = GetControlledAgentIndicesForClientSlot(agentTags, clientSlot, expectedClientCount);
            return controlledAgentIndices.Count > 0 ? controlledAgentIndices[0] : Math.Max(clientSlot, 0);
        }

        public static bool TryGetControlledTeamTag(IReadOnlyList<string> agentTags, int anchorAgentIndex, out string controlledTeamTag)
        {
            controlledTeamTag = null;
            if (agentTags == null || anchorAgentIndex < 0 || anchorAgentIndex >= agentTags.Count)
            {
                return false;
            }

            controlledTeamTag = agentTags[anchorAgentIndex];
            return !string.IsNullOrWhiteSpace(controlledTeamTag);
        }

        public static bool IsAgentOwnedByClientSlot(IReadOnlyList<string> agentTags, int clientSlot, int agentIndex, int expectedClientCount = ExpectedNetworkClientCount)
        {
            var controlledAgentIndices = GetControlledAgentIndicesForClientSlot(agentTags, clientSlot, expectedClientCount);
            return controlledAgentIndices.Contains(agentIndex);
        }

        public static bool HasBalancedNetworkTeams(IReadOnlyList<string> agentTags, int expectedClientCount = ExpectedNetworkClientCount)
        {
            var orderedTeamTags = GetOrderedTeamTags(agentTags);
            if (orderedTeamTags.Count != expectedClientCount)
            {
                return false;
            }

            int? expectedAgentCount = null;
            foreach (var teamTag in orderedTeamTags)
            {
                var teamAgentCount = GetAgentIndicesForTeamTag(agentTags, teamTag).Count;
                if (teamAgentCount == 0)
                {
                    return false;
                }

                if (!expectedAgentCount.HasValue)
                {
                    expectedAgentCount = teamAgentCount;
                    continue;
                }

                if (expectedAgentCount.Value != teamAgentCount)
                {
                    return false;
                }
            }

            return true;
        }

        public static string DescribeNetworkOwnership(IReadOnlyList<string> agentTags, int expectedClientCount = ExpectedNetworkClientCount)
        {
            var descriptions = new List<string>();
            for (var clientSlot = 0; clientSlot < expectedClientCount; clientSlot++)
            {
                var controlledAgentIndices = GetControlledAgentIndicesForClientSlot(agentTags, clientSlot, expectedClientCount);
                var teamTag = controlledAgentIndices.Count > 0 && controlledAgentIndices[0] < (agentTags?.Count ?? 0)
                    ? agentTags[controlledAgentIndices[0]]
                    : "unassigned";
                descriptions.Add($"client {clientSlot}: {teamTag} [{string.Join(",", controlledAgentIndices)}]");
            }

            return string.Join(" | ", descriptions);
        }

        private static List<string> GetOrderedTeamTags(IReadOnlyList<string> agentTags)
        {
            var orderedTeamTags = new List<string>();
            if (agentTags == null)
            {
                return orderedTeamTags;
            }

            var seenTeamTags = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < agentTags.Count; i++)
            {
                var teamTag = agentTags[i];
                if (string.IsNullOrWhiteSpace(teamTag) || !seenTeamTags.Add(teamTag))
                {
                    continue;
                }

                orderedTeamTags.Add(teamTag);
            }

            return orderedTeamTags;
        }

        private static List<int> GetAgentIndicesForTeamTag(IReadOnlyList<string> agentTags, string teamTag)
        {
            var controlledAgentIndices = new List<int>();
            if (agentTags == null || string.IsNullOrWhiteSpace(teamTag))
            {
                return controlledAgentIndices;
            }

            for (var i = 0; i < agentTags.Count; i++)
            {
                if (string.Equals(agentTags[i], teamTag, StringComparison.Ordinal))
                {
                    controlledAgentIndices.Add(i);
                }
            }

            return controlledAgentIndices;
        }

        private static List<int> GetFallbackAgentIndices(int agentCount, int clientSlot, int expectedClientCount)
        {
            var controlledAgentIndices = new List<int>();
            if (agentCount <= 0 || clientSlot < 0 || expectedClientCount <= 0 || clientSlot >= expectedClientCount)
            {
                return controlledAgentIndices;
            }

            var baseAgentCountPerClient = agentCount / expectedClientCount;
            var remainder = agentCount % expectedClientCount;
            var startIndex = 0;
            for (var i = 0; i < clientSlot; i++)
            {
                startIndex += baseAgentCountPerClient + (i < remainder ? 1 : 0);
            }

            var controlledCount = baseAgentCountPerClient + (clientSlot < remainder ? 1 : 0);
            for (var i = 0; i < controlledCount; i++)
            {
                var agentIndex = startIndex + i;
                if (agentIndex >= agentCount)
                {
                    break;
                }

                controlledAgentIndices.Add(agentIndex);
            }

            return controlledAgentIndices;
        }
    }
}
