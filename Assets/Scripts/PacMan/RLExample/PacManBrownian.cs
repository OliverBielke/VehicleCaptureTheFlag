using PacMan.Game;
using UnityEngine;
using Random = UnityEngine.Random;

namespace PacMan.RLExample
{
    [RequireComponent(typeof(PacManMovementController))]
    public class PacManBrownian : MonoBehaviour
    {
        [SerializeField] private float minDirectionDuration = 0.6f;
        [SerializeField] private float maxDirectionDuration = 1.5f;

        private PacManMovementController controller;
        private Vector2 currentDirection;
        private float directionTimeRemaining;

        private void Awake()
        {
            controller = GetComponent<PacManMovementController>();
            PickNewDirection();
        }

        private void FixedUpdate()
        {
            directionTimeRemaining -= Time.fixedDeltaTime;
            if (directionTimeRemaining <= 0f)
            {
                PickNewDirection();
            }

            controller.ApplyDesiredControl(currentDirection);
        }

        private void PickNewDirection()
        {
            var angle = Random.Range(0f, Mathf.PI * 2f);
            currentDirection = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            var minDuration = Mathf.Max(0.05f, Mathf.Min(minDirectionDuration, maxDirectionDuration));
            var maxDuration = Mathf.Max(minDuration, Mathf.Max(minDirectionDuration, maxDirectionDuration));
            directionTimeRemaining = Random.Range(minDuration, maxDuration);
        }
    }
}
