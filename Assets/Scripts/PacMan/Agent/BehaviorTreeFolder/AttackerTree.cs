using UnityEngine;

namespace PacMan.Agent.BehaviorTreeFolder
{
    [System.Serializable]
    public class AttackerBlackboard
    {
        public bool shouldGrabPowerCapsule;
        public bool shouldCampNextPowerCapsule;
        public bool shouldLootWhilePowered;
        public bool shouldReturnHome;
        public bool shouldReturnHomeLateGame;
        public bool safeEnemyPillsAvailable;
        public bool safeMiddlePillsAvailable;
        public bool outsideAttackZone;

        public Vector3 powerCapsuleTargetPosition;
        public Vector3 powerCapsuleCampPosition;
        public Vector3 homeTargetPosition;
        public Vector3 enemyPillTargetPosition;
        public Vector3 middlePillTargetPosition;
        public Vector3 attackPositionTarget;
        public Vector3 patrolTargetPosition;

        public string debugReason;
    }

    public static class AttackerTreeFactory
    {
        public static BehaviorTree<AttackerBlackboard> Create()
        {
            BTNode<AttackerBlackboard> root =
                new SelectorNode<AttackerBlackboard>(
                    "Attacker Selector",
                    new System.Collections.Generic.List<BTNode<AttackerBlackboard>>
                    {
                        new SequenceNode<AttackerBlackboard>(
                            "Late Game Return Home Sequence",
                            new System.Collections.Generic.List<BTNode<AttackerBlackboard>>
                            {
                                new ConditionNode<AttackerBlackboard>(
                                    "shouldReturnHomeLateGame",
                                    bb => bb.shouldReturnHomeLateGame
                                ),
                                new ActionNode<AttackerBlackboard>(
                                    "ReturnHome",
                                    bb => BTDecision.Running(
                                        AgentMode.ReturnHome,
                                        "ReturnHome",
                                        hasTarget: true,
                                        targetPosition: bb.homeTargetPosition
                                    )
                                )
                            }
                        ),

                        new SequenceNode<AttackerBlackboard>(
                            "Grab Power Capsule Sequence",
                            new System.Collections.Generic.List<BTNode<AttackerBlackboard>>
                            {
                                new ConditionNode<AttackerBlackboard>(
                                    "shouldGrabPowerCapsule",
                                    bb => bb.shouldGrabPowerCapsule
                                ),
                                new ActionNode<AttackerBlackboard>(
                                    "GrabPowerCapsule",
                                    bb => BTDecision.Running(
                                        AgentMode.Attack,
                                        "GrabPowerCapsule",
                                        hasTarget: true,
                                        targetPosition: bb.powerCapsuleTargetPosition
                                    )
                                )
                            }
                        ),

                        new SequenceNode<AttackerBlackboard>(
                            "Camp Next Power Capsule Sequence",
                            new System.Collections.Generic.List<BTNode<AttackerBlackboard>>
                            {
                                new ConditionNode<AttackerBlackboard>(
                                    "shouldCampNextPowerCapsule",
                                    bb => bb.shouldCampNextPowerCapsule
                                ),
                                new ActionNode<AttackerBlackboard>(
                                    "CampNextPowerCapsule",
                                    bb => BTDecision.Running(
                                        AgentMode.Attack,
                                        "CampNextPowerCapsule",
                                        hasTarget: true,
                                        targetPosition: bb.powerCapsuleCampPosition
                                    )
                                )
                            }
                        ),

                        new SequenceNode<AttackerBlackboard>(
                            "Return Home Sequence",
                            new System.Collections.Generic.List<BTNode<AttackerBlackboard>>
                            {
                                new ConditionNode<AttackerBlackboard>(
                                    "shouldReturnHome",
                                    bb => bb.shouldReturnHome
                                ),
                                new ActionNode<AttackerBlackboard>(
                                    "ReturnHome",
                                    bb => BTDecision.Running(
                                        AgentMode.ReturnHome,
                                        "ReturnHome",
                                        hasTarget: true,
                                        targetPosition: bb.homeTargetPosition
                                    )
                                )
                            }
                        ),

                        new SequenceNode<AttackerBlackboard>(
                            "Collect Enemy Pills Sequence",
                            new System.Collections.Generic.List<BTNode<AttackerBlackboard>>
                            {
                                new ConditionNode<AttackerBlackboard>(
                                    "canCollectEnemyPills",
                                    bb => bb.shouldLootWhilePowered || bb.safeEnemyPillsAvailable
                                ),
                                new ActionNode<AttackerBlackboard>(
                                    "CollectEnemyPills",
                                    bb => BTDecision.Running(
                                        AgentMode.Attack,
                                        "CollectEnemyPills",
                                        hasTarget: true,
                                        targetPosition: bb.enemyPillTargetPosition
                                    )
                                )
                            }
                        ),

                        new SequenceNode<AttackerBlackboard>(
                            "Collect Safe Middle Pills Sequence",
                            new System.Collections.Generic.List<BTNode<AttackerBlackboard>>
                            {
                                new ConditionNode<AttackerBlackboard>(
                                    "safeMiddlePillsAvailable",
                                    bb => bb.safeMiddlePillsAvailable
                                ),
                                new ActionNode<AttackerBlackboard>(
                                    "CollectSafeMiddlePills",
                                    bb => BTDecision.Running(
                                        AgentMode.Attack,
                                        "CollectSafeMiddlePills",
                                        hasTarget: true,
                                        targetPosition: bb.middlePillTargetPosition
                                    )
                                )
                            }
                        ),

                        new SequenceNode<AttackerBlackboard>(
                            "Move To Attack Position Sequence",
                            new System.Collections.Generic.List<BTNode<AttackerBlackboard>>
                            {
                                new ConditionNode<AttackerBlackboard>(
                                    "outsideAttackZone",
                                    bb => bb.outsideAttackZone
                                ),
                                new ActionNode<AttackerBlackboard>(
                                    "MoveToAttackPosition",
                                    bb => BTDecision.Running(
                                        AgentMode.Attack,
                                        "MoveToAttackPosition",
                                        hasTarget: true,
                                        targetPosition: bb.attackPositionTarget
                                    )
                                )
                            }
                        ),

                        new ActionNode<AttackerBlackboard>(
                            "PatrolAttackZone",
                            bb => BTDecision.Running(
                                AgentMode.Attack,
                                "PatrolAttackZone",
                                hasTarget: true,
                                targetPosition: bb.patrolTargetPosition
                            )
                        )
                    }
                );

            return new BehaviorTree<AttackerBlackboard>(root);
        }
    }
}
