using System;
using System.Collections.Generic;
using UnityEngine;

namespace PacMan.Agent
{
    public enum BTStatus {Success, Failure, Running}

    public enum AgentMode {Patrol, Attack, Defend, ReturnHome, Evade}

    // Small blackboard: only 6 things
    [Serializable]
    public class PacManBlackboard
    {
        public bool isGhost;
        public bool isScared;
        public int carriedFood;
        public int visibleEnemyCount;
        public bool enemyVisible;
        public bool shouldReturnHome;
    }

    public abstract class BTNode
    {
        public abstract BTStatus Evaluate(PacManBlackboard blackboard, out AgentMode mode);
    }

    public class SelectorNode : BTNode
    {
        private readonly List<BTNode> _children;

        public SelectorNode(List<BTNode> children)
        {
            _children = children;
        }

        public override BTStatus Evaluate(PacManBlackboard blackboard, out AgentMode mode)
        {
            foreach (var child in _children)
            {
                var status = child.Evaluate(blackboard, out mode);
                if (status == BTStatus.Success || status == BTStatus.Running)
                {
                    return status;
                }
            }

            mode = AgentMode.Patrol;
            return BTStatus.Failure;
        }
    }

    public class SequenceNode : BTNode
    {
        private readonly List<BTNode> _children;

        public SequenceNode(List<BTNode> children)
        {
            _children = children;
        }

        public override BTStatus Evaluate(PacManBlackboard blackboard, out AgentMode mode)
        {
            mode = AgentMode.Patrol;

            foreach (var child in _children)
            {
                var status = child.Evaluate(blackboard, out AgentMode childMode);

                if (status == BTStatus.Failure)
                {
                    mode = AgentMode.Patrol;
                    return BTStatus.Failure;
                }

                if (status == BTStatus.Running)
                {
                    mode = childMode;
                    return BTStatus.Running;
                }

                mode = childMode;
            }

            return BTStatus.Success;
        }
    }

    public class ConditionNode : BTNode
    {
        private readonly Func<PacManBlackboard, bool> _condition;

        public ConditionNode(Func<PacManBlackboard, bool> condition)
        {
            _condition = condition;
        }

        public override BTStatus Evaluate(PacManBlackboard blackboard, out AgentMode mode)
        {
            mode = AgentMode.Patrol;
            return _condition(blackboard) ? BTStatus.Success : BTStatus.Failure;
        }
    }

    public class ActionNode : BTNode
    {
        private readonly AgentMode _modeToSet;

        public ActionNode(AgentMode modeToSet)
        {
            _modeToSet = modeToSet;
        }

        public override BTStatus Evaluate(PacManBlackboard blackboard, out AgentMode mode)
        {
            mode = _modeToSet;
            return BTStatus.Success;
        }
    }

    public class BehaviorTree
    {
        private readonly BTNode _root;

        public BehaviorTree()
        {
            _root = BuildTree();
        }

        public AgentMode Evaluate(PacManBlackboard blackboard)
        {
            _root.Evaluate(blackboard, out AgentMode mode);
            return mode;
        }

        private BTNode BuildTree()
        {
            return new SelectorNode(new List<BTNode>
            {
                // 1. If ghost and enemy visible -> Defend
                new SequenceNode(new List<BTNode>
                {
                    new ConditionNode(bb => bb.isGhost && bb.enemyVisible),
                    new ActionNode(AgentMode.Defend)
                }),

                // 2. If should return home -> ReturnHome
                new SequenceNode(new List<BTNode>
                {
                    new ConditionNode(bb => bb.shouldReturnHome),
                    new ActionNode(AgentMode.ReturnHome)
                }),

                // 3. If pacman, enemy visible, and not ghost -> Evade
                new SequenceNode(new List<BTNode>
                {
                    new ConditionNode(bb => !bb.isGhost && bb.enemyVisible),
                    new ActionNode(AgentMode.Evade)
                }),

                // 4. If pacman and no visible enemy -> Attack
                new SequenceNode(new List<BTNode>
                {
                    new ConditionNode(bb => !bb.isGhost && !bb.enemyVisible),
                    new ActionNode(AgentMode.Attack)
                }),

                // 5. Fallback
                new ActionNode(AgentMode.Patrol)
            });
        }
    }
}