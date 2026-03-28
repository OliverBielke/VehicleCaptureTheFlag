using System.Collections.Generic;
using System.Linq;
using PacMan.Interface.PacMan;
using PacMan.Local;
using Scripts.Map;
using UnityEngine;
using TMPro;
using UnityEngine;
using Scripts.Map;

namespace PacMan.Agent
{
    public class PacManAIDebugBT : PacManAI
    {
        [Header("Debug")]
        [SerializeField] private bool useManualBlackboard = false;
        [SerializeField] private PacManBlackboard debugBlackboard = new();
        [SerializeField] private TextMeshPro debugText;
        [SerializeField] private bool allowManualOverride = true;
        private BehaviorTree _behaviorTree;
        private AgentMode _currentMode;

        public override void Initialize(MapManager mapManager)
        {
            base.Initialize(mapManager);
            _behaviorTree = new BehaviorTree();
        }

        public override PacManAction Tick()
        {
            PacManBlackboard bb;
            if (useManualBlackboard)
            {
                bb = debugBlackboard;
            }
            else
            {
                var visibleEnemies = _agent.GetVisibleEnemyAgents();

                bb = new PacManBlackboard
                {
                    isGhost = _agent.IsGhost(),
                    isScared = _agent.IsScared(),
                    carriedFood = _agent.GetCarriedFoodCount(),
                    visibleEnemyCount = visibleEnemies.Count,
                    enemyVisible = visibleEnemies.Count > 0,
                    shouldReturnHome = _agent.GetCarriedFoodCount() >= 3
                };

                debugBlackboard = bb;
            }

            _currentMode = _behaviorTree.Evaluate(bb);

            if (debugText != null)
            {
                debugText.text =
                    $"Mode: {_currentMode}\n" +
                    $"Ghost: {bb.isGhost}\n" +
                    $"Scared: {bb.isScared}\n" +
                    $"Food: {bb.carriedFood}\n" +
                    $"VisibleEnemies: {bb.visibleEnemyCount}\n" +
                    $"Return: {bb.shouldReturnHome}";
            }

            Debug.Log($"[{gameObject.name}] Mode = {_currentMode}");
            Vector2 accel = Vector2.zero;

            switch (_currentMode)
            {
                case AgentMode.Attack:
                    accel = GetAttackAcceleration();
                    break;

                case AgentMode.ReturnHome:
                    accel = GetReturnHomeAcceleration();
                    break;

                case AgentMode.Defend:
                    accel = GetDefendAcceleration();
                    break;

                case AgentMode.Evade:
                    accel = GetEvadeAcceleration();
                    break;

                case AgentMode.Patrol:
                default:
                    accel = GetPatrolAcceleration();
                    break;
            }

            // Manual override with keyboard
            Vector2 manualAccel = GetManualAcceleration();
            if (manualAccel != Vector2.zero)
            {
                accel = manualAccel;
            }

            return new PacManAction
            {
                Acceleration = accel
            };
            }
        private Vector2 GetAttackAcceleration()
        {
            // Blue attacks to the right, Red attacks to the left
            float x = CompareTag("Blue") ? 1f : -1f;
            return new Vector2(x, 0f);
        }

        private Vector2 GetReturnHomeAcceleration()
        {
            // Blue home is left, Red home is right
            float x = CompareTag("Blue") ? -1f : 1f;
            return new Vector2(x, 0f);
        }

        private Vector2 GetPatrolAcceleration()
        {
            // Stay still for now
            return Vector2.zero;
        }

        private Vector2 GetDefendAcceleration()
        {
            var visibleEnemies = _agent.GetVisibleEnemyAgents();
            if (visibleEnemies != null && visibleEnemies.Count > 0)
            {
                Vector3 myPos = transform.localPosition;
                Vector3 enemyPos = visibleEnemies[0].transform.localPosition;
                Vector3 dir = (enemyPos - myPos).normalized;
                return new Vector2(dir.x, dir.z);
            }

            return Vector2.zero;
        }

        private Vector2 GetEvadeAcceleration()
        {
            var visibleEnemies = _agent.GetVisibleEnemyAgents();
            if (visibleEnemies != null && visibleEnemies.Count > 0)
            {
                Vector3 myPos = transform.localPosition;
                Vector3 enemyPos = visibleEnemies[0].transform.localPosition;
                Vector3 dir = (myPos - enemyPos).normalized;
                return new Vector2(dir.x, dir.z);
            }

            return GetReturnHomeAcceleration();
        }
        private Vector2 GetManualAcceleration()
        {
            int x = 0;
            int z = 0;

            bool isBlue = TeamAssignmentUtil.CheckTeam(gameObject) == Team.Blue;
            bool isRed = TeamAssignmentUtil.CheckTeam(gameObject) == Team.Red;

            if ((isBlue && Input.GetKey("w")) || (isRed && Input.GetKey(KeyCode.UpArrow)))
            {
                z = 1;
            }

            if ((isBlue && Input.GetKey("s")) || (isRed && Input.GetKey(KeyCode.DownArrow)))
            {
                z = -1;
            }

            if ((isBlue && Input.GetKey("a")) || (isRed && Input.GetKey(KeyCode.LeftArrow)))
            {
                x = -1;
            }

            if ((isBlue && Input.GetKey("d")) || (isRed && Input.GetKey(KeyCode.RightArrow)))
            {
                x = 1;
            }

            return new Vector2(x, z);
        }
        private void OnGUI()
        {
            if (!Application.isPlaying)
                return;

            Vector3 worldPos = transform.position + Vector3.up * 2f;
            Vector3 screenPos = Camera.main.WorldToScreenPoint(worldPos);

            if (screenPos.z > 0f)
            {
                float x = screenPos.x;
                float y = Screen.height - screenPos.y;

                GUI.Label(
                    new Rect(x, y, 220f, 120f),
                    $"Mode: {_currentMode}\n" +
                    $"Ghost: {debugBlackboard.isGhost}\n" +
                    $"Food: {debugBlackboard.carriedFood}\n" +
                    $"Enemies: {debugBlackboard.visibleEnemyCount}\n" +
                    $"Return: {debugBlackboard.shouldReturnHome}"
                );
            }
        }
    }
}