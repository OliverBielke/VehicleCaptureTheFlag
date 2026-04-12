using System.Collections.Generic;
using System.Linq;
using Scripts.Map;
using UnityEngine;

namespace PacMan.Agent.Map
{
    public class MapMiddleAnalyzer
    {
        public class Lane
        {
            public int Id;
            public int OrderedIndex;

            public List<Vector2Int> LeftCells = new();
            public List<Vector2Int> RightCells = new();

            public List<Vector3> LeftLocalPositions = new();
            public List<Vector3> RightLocalPositions = new();

            public int MinZ;
            public int MaxZ;
            public float CenterZCell;

            public Vector3 LeftCenterLocal;
            public Vector3 RightCenterLocal;
            public Vector3 MidCenterLocal;

            public int WidthCells => LeftCells.Count;

            public bool IsMajor;
            public string Label;
        }

        public struct MiddleInfo
        {
            public int MinFreeX;
            public int MaxFreeX;
            public int MinFreeZ;
            public int MaxFreeZ;

            public float MidXCell;
            public float MidXLocal;

            public int LeftMiddleColumn;
            public int RightMiddleColumn;

            public List<Vector2Int> MiddleLeftCells;
            public List<Vector2Int> MiddleRightCells;

            public List<Vector3> MiddleLeftLocalPositions;
            public List<Vector3> MiddleRightLocalPositions;

            public Vector3 MiddleLeftMedianLocal;
            public Vector3 MiddleRightMedianLocal;

            public List<Lane> Lanes;
            public int LaneCount;
        }

        private readonly ObstacleMapV2 _map;

        public MapMiddleAnalyzer(ObstacleMapV2 obstacleMap)
        {
            _map = obstacleMap;
        }

        public MiddleInfo Analyze()
        {
            var freeCells = _map.traversabilityPerCell
                .Where(kvp => kvp.Value == ObstacleMapV2.Traversability.Free)
                .Select(kvp => kvp.Key)
                .ToList();

            if (freeCells.Count == 0)
            {
                Debug.LogError("MapMiddleAnalyzer: no free cells found.");
                return default;
            }

            int minX = freeCells.Min(c => c.x);
            int maxX = freeCells.Max(c => c.x);
            int minZ = freeCells.Min(c => c.y);
            int maxZ = freeCells.Max(c => c.y);

            float midXCell = (minX + maxX) * 0.5f;

            int leftMiddleColumn = Mathf.FloorToInt(midXCell);
            int rightMiddleColumn = leftMiddleColumn + 1;

            var middleLeftCells = new List<Vector2Int>();
            var middleRightCells = new List<Vector2Int>();

            // First try exact center pair
            for (int z = minZ; z <= maxZ; z++)
            {
                var leftCell = new Vector2Int(leftMiddleColumn, z);
                var rightCell = new Vector2Int(rightMiddleColumn, z);

                if (IsFree(leftCell) && IsFree(rightCell))
                {
                    middleLeftCells.Add(leftCell);
                    middleRightCells.Add(rightCell);
                }
            }

            // Fallback: if exact center has no crossings, find best adjacent pair nearest the center
            if (middleLeftCells.Count == 0)
            {
                FindNearestMiddlePair(
                    minX, maxX, minZ, maxZ,
                    out leftMiddleColumn, out rightMiddleColumn,
                    out middleLeftCells, out middleRightCells
                );
            }

            var middleLeftLocal = middleLeftCells.Select(CellToLocalCenter).ToList();
            var middleRightLocal = middleRightCells.Select(CellToLocalCenter).ToList();

            var lanes = BuildLanes(middleLeftCells, middleRightCells);
            FinalizeLaneLabels(lanes);

            return new MiddleInfo
            {
                MinFreeX = minX,
                MaxFreeX = maxX,
                MinFreeZ = minZ,
                MaxFreeZ = maxZ,

                MidXCell = (leftMiddleColumn + rightMiddleColumn) * 0.5f,
                MidXLocal = ((leftMiddleColumn + rightMiddleColumn) * 0.5f) * _map.trueScale.x,

                LeftMiddleColumn = leftMiddleColumn,
                RightMiddleColumn = rightMiddleColumn,

                MiddleLeftCells = middleLeftCells,
                MiddleRightCells = middleRightCells,

                MiddleLeftLocalPositions = middleLeftLocal,
                MiddleRightLocalPositions = middleRightLocal,

                MiddleLeftMedianLocal = GetMedianPosition(middleLeftLocal),
                MiddleRightMedianLocal = GetMedianPosition(middleRightLocal),

                Lanes = lanes,
                LaneCount = lanes.Count
            };
        }

        private void FindNearestMiddlePair(
            int minX,
            int maxX,
            int minZ,
            int maxZ,
            out int bestLeftColumn,
            out int bestRightColumn,
            out List<Vector2Int> bestLeftCells,
            out List<Vector2Int> bestRightCells)
        {
            float trueMid = (minX + maxX) * 0.5f;

            bestLeftColumn = Mathf.FloorToInt(trueMid);
            bestRightColumn = bestLeftColumn + 1;
            bestLeftCells = new List<Vector2Int>();
            bestRightCells = new List<Vector2Int>();

            int bestScore = -1;
            float bestDistance = float.MaxValue;

            for (int leftX = minX; leftX < maxX; leftX++)
            {
                int rightX = leftX + 1;

                var leftCells = new List<Vector2Int>();
                var rightCells = new List<Vector2Int>();

                for (int z = minZ; z <= maxZ; z++)
                {
                    var leftCell = new Vector2Int(leftX, z);
                    var rightCell = new Vector2Int(rightX, z);

                    if (IsFree(leftCell) && IsFree(rightCell))
                    {
                        leftCells.Add(leftCell);
                        rightCells.Add(rightCell);
                    }
                }

                int score = leftCells.Count;
                float distance = Mathf.Abs(((leftX + rightX) * 0.5f) - trueMid);

                if (score > bestScore || (score == bestScore && distance < bestDistance))
                {
                    bestScore = score;
                    bestDistance = distance;
                    bestLeftColumn = leftX;
                    bestRightColumn = rightX;
                    bestLeftCells = leftCells;
                    bestRightCells = rightCells;
                }
            }
        }

        private List<Lane> BuildLanes(List<Vector2Int> leftCells, List<Vector2Int> rightCells)
        {
            var lanes = new List<Lane>();

            if (leftCells == null || rightCells == null || leftCells.Count == 0 || rightCells.Count == 0)
                return lanes;

            var orderedLeft = leftCells.OrderBy(c => c.y).ToList();
            var orderedRight = rightCells.OrderBy(c => c.y).ToList();

            Lane currentLane = null;
            int laneId = 0;

            for (int i = 0; i < orderedLeft.Count; i++)
            {
                var left = orderedLeft[i];
                var right = orderedRight[i];

                if (currentLane == null)
                {
                    currentLane = CreateLane(laneId++, left, right);
                    continue;
                }

                var prevLeft = currentLane.LeftCells[currentLane.LeftCells.Count - 1];
                bool contiguousInZ = left.y == prevLeft.y + 1;

                if (contiguousInZ)
                {
                    AddToLane(currentLane, left, right);
                }
                else
                {
                    FinalizeLane(currentLane);
                    lanes.Add(currentLane);
                    currentLane = CreateLane(laneId++, left, right);
                }
            }

            if (currentLane != null)
            {
                FinalizeLane(currentLane);
                lanes.Add(currentLane);
            }

            return lanes;
        }

        private void FinalizeLaneLabels(List<Lane> lanes)
        {
            if (lanes == null || lanes.Count == 0)
                return;

            var ordered = lanes.OrderBy(l => l.MidCenterLocal.z).ToList();

            int maxWidth = ordered.Max(l => l.WidthCells);
            int majorThreshold = Mathf.Max(1, Mathf.CeilToInt(0.5f * maxWidth));

            for (int i = 0; i < ordered.Count; i++)
            {
                ordered[i].OrderedIndex = i;
                ordered[i].Label = $"Lane {i}";
                ordered[i].IsMajor = ordered[i].WidthCells >= majorThreshold;
            }
        }

        private Lane CreateLane(int id, Vector2Int left, Vector2Int right)
        {
            var lane = new Lane { Id = id };
            AddToLane(lane, left, right);
            return lane;
        }

        private void AddToLane(Lane lane, Vector2Int left, Vector2Int right)
        {
            lane.LeftCells.Add(left);
            lane.RightCells.Add(right);
        }

        private void FinalizeLane(Lane lane)
        {
            lane.LeftLocalPositions = lane.LeftCells.Select(CellToLocalCenter).ToList();
            lane.RightLocalPositions = lane.RightCells.Select(CellToLocalCenter).ToList();

            lane.MinZ = lane.LeftCells.Min(c => c.y);
            lane.MaxZ = lane.LeftCells.Max(c => c.y);
            lane.CenterZCell = 0.5f * (lane.MinZ + lane.MaxZ);

            lane.LeftCenterLocal = GetMedianPosition(lane.LeftLocalPositions);
            lane.RightCenterLocal = GetMedianPosition(lane.RightLocalPositions);
            lane.MidCenterLocal = 0.5f * (lane.LeftCenterLocal + lane.RightCenterLocal);
        }

        private bool IsFree(Vector2Int cell)
        {
            return _map.traversabilityPerCell.TryGetValue(cell, out var traversability) &&
                   traversability == ObstacleMapV2.Traversability.Free;
        }

        private Vector3 CellToLocalCenter(Vector2Int cell)
        {
            return new Vector3(
                (cell.x + 0.5f) * _map.trueScale.x,
                0f,
                (cell.y + 0.5f) * _map.trueScale.z
            );
        }

        private Vector3 GetMedianPosition(List<Vector3> positions)
        {
            if (positions == null || positions.Count == 0)
                return Vector3.zero;

            var ordered = positions.OrderBy(p => p.z).ToList();
            return ordered[ordered.Count / 2];
        }

        public static Vector3 GetClosestPosition(Vector3 fromLocalPos, List<Vector3> candidates)
        {
            if (candidates == null || candidates.Count == 0)
                return fromLocalPos;

            float bestDist = float.MaxValue;
            Vector3 best = candidates[0];

            foreach (var p in candidates)
            {
                float d = (p - fromLocalPos).sqrMagnitude;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = p;
                }
            }

            return best;
        }

        public static List<Lane> GetLanesOrdered(MiddleInfo info)
        {
            if (info.Lanes == null)
                return new List<Lane>();

            return info.Lanes.OrderBy(l => l.MidCenterLocal.z).ToList();
        }

        public static List<Lane> GetMajorLanes(MiddleInfo info)
        {
            if (info.Lanes == null)
                return new List<Lane>();

            return info.Lanes
                .Where(l => l.IsMajor)
                .OrderBy(l => l.MidCenterLocal.z)
                .ToList();
        }

        public static Lane GetClosestLane(Vector3 localPosition, MiddleInfo info, bool majorOnly = false)
        {
            if (info.Lanes == null || info.Lanes.Count == 0)
                return null;

            var candidates = majorOnly
                ? info.Lanes.Where(l => l.IsMajor).ToList()
                : info.Lanes.ToList();

            if (candidates.Count == 0)
                return null;

            Lane bestLane = candidates[0];
            float bestDist = float.MaxValue;

            foreach (var lane in candidates)
            {
                float d = (lane.MidCenterLocal - localPosition).sqrMagnitude;
                if (d < bestDist)
                {
                    bestDist = d;
                    bestLane = lane;
                }
            }

            return bestLane;
        }
        public static Vector3 GetDefendAnchorForLane(Lane lane, bool isBlueTeam, ObstacleMapV2 map, float offsetFromMiddle = 1.2f,
            float searchStep = 0.2f,
            int maxSteps = 20)
        {
            Vector3 desired = lane.MidCenterLocal;
            desired.x += isBlueTeam ? -offsetFromMiddle : offsetFromMiddle;
            desired.y = 0f;

            if (map.GetLocalPointTraversibility(desired) == ObstacleMapV2.Traversability.Free)
                return desired;

            for (int i = 1; i <= maxSteps; i++)
            {
                float dx = i * searchStep;

                Vector3 a = desired + new Vector3(dx, 0f, 0f);
                Vector3 b = desired - new Vector3(dx, 0f, 0f);

                bool aFree = map.GetLocalPointTraversibility(a) == ObstacleMapV2.Traversability.Free;
                bool bFree = map.GetLocalPointTraversibility(b) == ObstacleMapV2.Traversability.Free;

                if (isBlueTeam)
                {
                    if (bFree) return b;
                    if (aFree) return a;
                }
                else
                {
                    if (aFree) return a;
                    if (bFree) return b;
                }
            }

            return lane.MidCenterLocal;
        }
        public static Lane GetLaneByOrderedIndex(MiddleInfo info, int orderedIndex)
        {
            if (info.Lanes == null || info.Lanes.Count == 0)
                return null;

            return info.Lanes.FirstOrDefault(l => l.OrderedIndex == orderedIndex);
        }
    }
}