using System;
using System.Collections.Generic;
using UnityEngine;

namespace PacMan.Agent.BehaviorTreeFolder
{
    public enum BTStatus
    {
        Success,
        Failure,
        Running
    }

    public enum AgentMode
    {
        Patrol,
        Attack,
        Defend,
        ReturnHome,
        Evade
    }

    [Serializable]
    public class BTDecision
    {
        public BTStatus Status;
        public AgentMode Mode;
        public bool HasTarget;
        public Vector3 TargetPosition;
        public string DebugLabel;

        public static BTDecision Failure(string label = "Failure")
        {
            return new BTDecision
            {
                Status = BTStatus.Failure,
                Mode = AgentMode.Patrol,
                HasTarget = false,
                TargetPosition = Vector3.zero,
                DebugLabel = label
            };
        }

        public static BTDecision Success(
            AgentMode mode,
            string label,
            bool hasTarget = false,
            Vector3 targetPosition = default)
        {
            return new BTDecision
            {
                Status = BTStatus.Success,
                Mode = mode,
                HasTarget = hasTarget,
                TargetPosition = targetPosition,
                DebugLabel = label
            };
        }

        public static BTDecision Running(
            AgentMode mode,
            string label,
            bool hasTarget = false,
            Vector3 targetPosition = default)
        {
            return new BTDecision
            {
                Status = BTStatus.Running,
                Mode = mode,
                HasTarget = hasTarget,
                TargetPosition = targetPosition,
                DebugLabel = label
            };
        }
    }

    public abstract class BTNode<TBlackboard>
    {
        public abstract BTDecision Evaluate(TBlackboard blackboard);
    }

    public class SelectorNode<TBlackboard> : BTNode<TBlackboard>
    {
        private readonly List<BTNode<TBlackboard>> _children;
        private readonly string _label;

        public SelectorNode(string label, List<BTNode<TBlackboard>> children)
        {
            _label = label;
            _children = children ?? new List<BTNode<TBlackboard>>();
        }

        public override BTDecision Evaluate(TBlackboard blackboard)
        {
            foreach (var child in _children)
            {
                var result = child.Evaluate(blackboard);

                if (result.Status == BTStatus.Success || result.Status == BTStatus.Running)
                    return result;
            }

            return BTDecision.Failure($"{_label}: no child succeeded");
        }
    }

    public class SequenceNode<TBlackboard> : BTNode<TBlackboard>
    {
        private readonly List<BTNode<TBlackboard>> _children;
        private readonly string _label;

        public SequenceNode(string label, List<BTNode<TBlackboard>> children)
        {
            _label = label;
            _children = children ?? new List<BTNode<TBlackboard>>();
        }

        public override BTDecision Evaluate(TBlackboard blackboard)
        {
            BTDecision lastSuccess = null;

            foreach (var child in _children)
            {
                var result = child.Evaluate(blackboard);

                if (result.Status == BTStatus.Failure)
                    return BTDecision.Failure($"{_label}: child failed -> {result.DebugLabel}");

                if (result.Status == BTStatus.Running)
                    return result;

                lastSuccess = result;
            }

            return lastSuccess ?? BTDecision.Failure($"{_label}: empty sequence");
        }
    }

    public class ConditionNode<TBlackboard> : BTNode<TBlackboard>
    {
        private readonly Func<TBlackboard, bool> _condition;
        private readonly string _label;

        public ConditionNode(string label, Func<TBlackboard, bool> condition)
        {
            _label = label;
            _condition = condition;
        }

        public override BTDecision Evaluate(TBlackboard blackboard)
        {
            bool passed = _condition != null && _condition(blackboard);

            return passed
                ? BTDecision.Success(AgentMode.Patrol, _label)
                : BTDecision.Failure(_label);
        }
    }

    public class ActionNode<TBlackboard> : BTNode<TBlackboard>
    {
        private readonly Func<TBlackboard, BTDecision> _action;
        private readonly string _label;

        public ActionNode(string label, Func<TBlackboard, BTDecision> action)
        {
            _label = label;
            _action = action;
        }

        public override BTDecision Evaluate(TBlackboard blackboard)
        {
            if (_action == null)
                return BTDecision.Failure($"{_label}: action is null");

            var result = _action(blackboard);

            if (result == null)
                return BTDecision.Failure($"{_label}: action returned null");

            if (string.IsNullOrEmpty(result.DebugLabel))
                result.DebugLabel = _label;

            return result;
        }
    }

    public class BehaviorTree<TBlackboard>
    {
        private readonly BTNode<TBlackboard> _root;

        public BehaviorTree(BTNode<TBlackboard> root)
        {
            _root = root;
        }

        public BTDecision Evaluate(TBlackboard blackboard)
        {
            if (_root == null)
                return BTDecision.Failure("BehaviorTree has no root");

            return _root.Evaluate(blackboard);
        }
    }
}