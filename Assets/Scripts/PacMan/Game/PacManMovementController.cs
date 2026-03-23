using System.Collections.Generic;
using UnityEngine;

namespace PacMan.Game
{
    public class PacManMovementController : MonoBehaviour
    {
        public float max_speed = 15f;
        public float max_acceleration = 15f;

        private List<Transform> propellers;

        public bool collisionEnabled = true;
        private Rigidbody rigidBody;
        public bool MovementEnabled { get; set; }

        public void Awake()
        {
            rigidBody = GetComponent<Rigidbody>();
            MovementEnabled = true;
        }

        public void ApplyDesiredControl(Vector2 actionMoveVector)
        {
            if (!MovementEnabled) actionMoveVector = Vector2.zero;

            actionMoveVector = actionMoveVector.normalized * Mathf.Min(actionMoveVector.magnitude, actionMoveVector.normalized.magnitude);

            var acceleration = (Vector3.right * actionMoveVector.x + Vector3.forward * actionMoveVector.y) * max_acceleration;

            rigidBody.AddForce(acceleration, ForceMode.Acceleration);

            if (rigidBody.linearVelocity.magnitude > max_speed)
            {
                rigidBody.linearVelocity = rigidBody.linearVelocity.normalized * max_speed;
            }

            if (acceleration.magnitude > 0)
            {
                var targetRotation = Quaternion.LookRotation(
                    Vector3.forward * 40 - new Vector3(0, acceleration.z, 0),
                    Vector3.up * 40 + new Vector3(acceleration.x, 0, 0)) * Quaternion.LookRotation(acceleration);
                targetRotation = Quaternion.Slerp(transform.rotation, targetRotation, 15.0f * Time.fixedDeltaTime);
                transform.rotation = targetRotation;
            }
        }
    }
}