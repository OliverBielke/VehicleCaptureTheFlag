using UnityEngine;

namespace PacMan.Agent.BehaviorTreeFolder
{
    [System.Serializable]
    public class DefenderBlackboard
    {
        public bool shouldGrabPowerCapsule;
        public bool shouldLootWhilePowered;
        public bool shouldReturnHome;
        public bool enemyPacmanIntruderSuspected;
        public bool enemyLikelyCrossingMyLane;
        public bool safeMiddlePillsAvailable;
        public bool outsideDefensiveZone;

        public Vector3 powerCapsuleTargetPosition;
        public Vector3 homeTargetPosition;
        public Vector3 enemyPillTargetPosition;
        public Vector3 suspectedIntruderPosition;
        public Vector3 predictedCrossingPoint;
        public Vector3 safeMiddlePillPosition;
        public Vector3 formationPoint;
        public Vector3 dropZonePoint;

        public string debugReason;
    }

    public static class DefenderTreeFactory
    {
        public static BehaviorTree<DefenderBlackboard> Create()
        {
            BTNode<DefenderBlackboard> root =
                new SelectorNode<DefenderBlackboard>(
                    "Defender Selector",
                    new System.Collections.Generic.List<BTNode<DefenderBlackboard>>
                    {
                        new SequenceNode<DefenderBlackboard>(
                            "Grab Power Capsule Sequence",
                            new System.Collections.Generic.List<BTNode<DefenderBlackboard>>
                            {
                                new ConditionNode<DefenderBlackboard>(
                                    "shouldGrabPowerCapsule",
                                    bb => bb.shouldGrabPowerCapsule
                                ),
                                new ActionNode<DefenderBlackboard>(
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

                        new SequenceNode<DefenderBlackboard>(
                            "Return Home Sequence",
                            new System.Collections.Generic.List<BTNode<DefenderBlackboard>>
                            {
                                new ConditionNode<DefenderBlackboard>(
                                    "shouldReturnHome",
                                    bb => bb.shouldReturnHome
                                ),
                                new ActionNode<DefenderBlackboard>(
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

                        new SequenceNode<DefenderBlackboard>(
                            "Loot While Powered Sequence",
                            new System.Collections.Generic.List<BTNode<DefenderBlackboard>>
                            {
                                new ConditionNode<DefenderBlackboard>(
                                    "shouldLootWhilePowered",
                                    bb => bb.shouldLootWhilePowered
                                ),
                                new ActionNode<DefenderBlackboard>(
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

                        new SequenceNode<DefenderBlackboard>(
                            "Intercept Intruder Sequence",
                            new System.Collections.Generic.List<BTNode<DefenderBlackboard>>
                            {
                                new ConditionNode<DefenderBlackboard>(
                                    "enemyPacmanIntruderSuspected",
                                    bb => bb.enemyPacmanIntruderSuspected
                                ),
                                new ActionNode<DefenderBlackboard>(
                                    "InterceptIntruder",
                                    bb => BTDecision.Running(
                                        AgentMode.Defend,
                                        "InterceptIntruder",
                                        hasTarget: true,
                                        targetPosition: bb.suspectedIntruderPosition
                                    )
                                )
                            }
                        ),

                        new SequenceNode<DefenderBlackboard>(
                            "Block Crossing Sequence",
                            new System.Collections.Generic.List<BTNode<DefenderBlackboard>>
                            {
                                new ConditionNode<DefenderBlackboard>(
                                    "enemyLikelyCrossingMyLane",
                                    bb => bb.enemyLikelyCrossingMyLane
                                ),
                                new ActionNode<DefenderBlackboard>(
                                    "BlockCrossing",
                                    bb => BTDecision.Running(
                                        AgentMode.Defend,
                                        "BlockCrossing",
                                        hasTarget: true,
                                        targetPosition: bb.predictedCrossingPoint
                                    )
                                )
                            }
                        ),

                        new SequenceNode<DefenderBlackboard>(
                            "Collect Safe Middle Pills Sequence",
                            new System.Collections.Generic.List<BTNode<DefenderBlackboard>>
                            {
                                new ConditionNode<DefenderBlackboard>(
                                    "safeMiddlePillsAvailable",
                                    bb => bb.safeMiddlePillsAvailable
                                ),
                                new ActionNode<DefenderBlackboard>(
                                    "CollectSafeMiddlePills",
                                    bb => BTDecision.Running(
                                        AgentMode.Defend,
                                        "CollectSafeMiddlePills",
                                        hasTarget: true,
                                        targetPosition: bb.safeMiddlePillPosition
                                    )
                                )
                            }
                        ),

                        new SequenceNode<DefenderBlackboard>(
                            "Move To Formation Sequence",
                            new System.Collections.Generic.List<BTNode<DefenderBlackboard>>
                            {
                                new ConditionNode<DefenderBlackboard>(
                                    "outsideDefensiveZone",
                                    bb => bb.outsideDefensiveZone
                                ),
                                new ActionNode<DefenderBlackboard>(
                                    "MoveToFormation",
                                    bb => BTDecision.Running(
                                        AgentMode.Defend,
                                        "MoveToFormation",
                                        hasTarget: true,
                                        targetPosition: bb.formationPoint
                                    )
                                )
                            }
                        ),

                        new ActionNode<DefenderBlackboard>(
                            "HoldDropZone",
                            bb => BTDecision.Running(
                                AgentMode.Defend,
                                "HoldDropZone",
                                hasTarget: true,
                                targetPosition: bb.dropZonePoint
                            )
                        )
                    }
                );

            return new BehaviorTree<DefenderBlackboard>(root);
        }
    }
}
