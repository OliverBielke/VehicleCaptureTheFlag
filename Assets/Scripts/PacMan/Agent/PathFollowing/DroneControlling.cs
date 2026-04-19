using System.Collections.Generic;
using UnityEngine;
using PacMan.Agent.PathFinding;
using System;
using Scripts.Vehicle;
using PacMan.Game;

namespace PacMan.Agent.PathFollowing
{
    public class DroneControlling
    {
        // Constants
        private const float K_P_POSITION = 1f;
        private const float K_D_POSITION = 1.5f;
        private const float K_P_VELOCITY = 15f;
        private const float K_D_VELOCITY = 0f;
        private const float K_VELOCITY_DIRECTION = 15.0f;
        private readonly PacManMovementController movementController;
        public bool HasReachedGoal = false;
        public float StoppingDistance = 0.2f; // Adjust based on the size of your car/goal
        
        // Outputs
        public float h { get; set; }  // Horizontal acceleration command [-1, 1]
        public float v { get; set; }  // Forward acceleration command [-1, 1]
        
        // State tracking
        public List<Node> waypoints { get; set; }
        public Vector3 goal { get; set; }
        public Transform currDroneState { get; set; }
    
        // Path tracking
        public float targetDistance { get; set; }
        public Vector2 closestPoint { get; set; }
        public Vector2 targetPoint { get; set; }
        public int bestStartIndex { get; set; }
        public List<float> targetSpeeds { get; set; }
        private Vector2 lastVelError { get; set; }
        private Vector2 lastPosError { get; set; }
        
        public DroneControlling(List<Node> waypoints, Vector3 goal, Transform droneState, PacManMovementController movementController)
        {
            this.waypoints = waypoints;
            this.goal = goal;
            currDroneState = droneState;
            this.movementController = movementController;
            bestStartIndex = 0;
            targetDistance = 0.5f;
            lastVelError = Vector2.zero;
            lastPosError = Vector2.zero;
            h = 0f;
            v = 0f;
            targetSpeeds = GenerateTargetSpeeds(waypoints, movementController.max_speed, movementController.max_acceleration);
        }
        
        public void PDCalculateMove(Transform droneTransform)
        {
            currDroneState = droneTransform;
            
            if (CheckGoalReached()) return;
            
            //UpdateTargetDistance();
    
            var targetPoint = GetTargetPoint(droneTransform.position);
            var currentPos2D = new Vector2(droneTransform.position.x, droneTransform.position.z);

            var rigidBody = droneTransform.GetComponent<Rigidbody>();
            Vector3 linearVelocity = rigidBody != null ? rigidBody.linearVelocity : Vector3.zero;
            var currentVel = new Vector2(linearVelocity.x, linearVelocity.z);
            
            var targetSpeed = GetTargetSpeed(closestPoint, bestStartIndex, waypoints);
            var targetDir = (targetPoint - currentPos2D).normalized;
            var desiredVelocity = targetDir * targetSpeed;

            var velocityError = desiredVelocity - currentVel;
            var positionError =  closestPoint - currentPos2D;
            
            var velDeriv = (velocityError - lastVelError) / Time.fixedDeltaTime;
            var posDeriv = (positionError - lastPosError) / Time.fixedDeltaTime;
            
            var velForce =  velocityError * K_P_VELOCITY + velDeriv*K_D_VELOCITY;
            var posForce = positionError * K_P_POSITION + posDeriv* K_D_POSITION;
            
            var feedforward = targetDir * K_VELOCITY_DIRECTION;
            var total = velForce + posForce + feedforward;
            
            //Debug.Log("Target Speed: " + targetSpeed);
            //Debug.Log("Current Speed" + currentVel.magnitude);

            var maxAcceleration = Mathf.Max(0.0001f, movementController.max_acceleration);
            h = Mathf.Clamp(total.x / maxAcceleration, -1f, 1f);
            v = Mathf.Clamp(total.y / maxAcceleration, -1f, 1f);

            lastVelError = velocityError;
            lastPosError = positionError;
            }
        
        private float GetMinTargetSpeed(int index, List<Node> path)
        {
            float dist = 10f;
            float lowestSpeed = float.MaxValue;
            while (true)
            {
                if (index >= path.Count - 2)
                {
                    return lowestSpeed;
                }
                float currSpeed = GetTargetSpeed(path[index].position, index,  path); 
                lowestSpeed = (currSpeed < lowestSpeed) ? currSpeed : lowestSpeed;  
                float segLen = Vector2.Distance(path[index].position, path[index+1].position);
                dist -=  segLen;
                if (dist < 0)
                {
                    return lowestSpeed;
                }
                index++;
            }
        }
        
        private float GetTargetSpeed(Vector2 pos, int index, List<Node> path)
        {
            var prev = path[index].position;
            var next = path[index+1].position;
            var nextDist = Vector2.Distance(pos, next);
            var totalDist = Vector2.Distance(prev, next);
            var prevSpeed = targetSpeeds[index];
            var nextSpeed = targetSpeeds[index+1];
            // Linear interpolation
            var targetSpeed = nextDist/totalDist*prevSpeed + (1 - nextDist / totalDist)*nextSpeed;
            return targetSpeed;
        }
        
        public static List<float> GenerateTargetSpeeds(List<Node> path, float maxSpeed, float maxAcceleration)
        {
            // Kapania, Subosits, Gerdes "A Sequential Two-Step Algorithm for Fast Generation of Vehicle Racing Trajectories"
            
            maxAcceleration = Mathf.Max(0.0001f, maxAcceleration);
            maxSpeed = Mathf.Max(0f, maxSpeed);
            
            List<float> speeds = new();
            speeds.Add(0);
            for (var i = 1; i < path.Count - 1; i++)
            {
                var prev = path[i - 1].position;
                var curr = path[i].position;
                var next = path[i + 1].position;
                
                var enterDir = (curr - prev).normalized;
                var leaveDir = (next - curr).normalized;
                var angle = Vector2.Angle(enterDir, leaveDir);
                var dist = Math.Max((Vector2.Distance(curr, prev) + Vector2.Distance(next, curr)) / 2, 0.001f);
                var curvature = angle * Mathf.Deg2Rad / dist;
                
                if (curvature < 0.001f)
                {
                    speeds.Add(maxSpeed);
                    continue;
                }
                var segmentMaxSpeed = Mathf.Sqrt(maxAcceleration / curvature);
                segmentMaxSpeed = Mathf.Min(maxSpeed, segmentMaxSpeed);
                speeds.Add(segmentMaxSpeed);
            }
            speeds[0] = maxSpeed;
            speeds.Add(maxSpeed);
            
            // Backwards
            for (var i = path.Count - 2; i >= 0; i--)
            {
                var curr = path[i].position;
                var next = path[i + 1].position;
                var dist =  Vector2.Distance(curr, next);
                
                var vNext = speeds[i + 1];

                // Safety margin as someof the accel is located to correcting errors
                var decel = maxAcceleration;
                
                var vCurr = (float) Math.Sqrt(Math.Pow(vNext, 2) + 2*decel*dist);

                speeds[i] = Math.Min(vCurr, speeds[i]);
            }
            
            for (var i = 1; path.Count > i; i++)
            {
                var curr = path[i].position;
                var prev = path[i - 1].position;
                var dist =  Vector2.Distance(curr, prev);
                
                var vPrev = speeds[i - 1];
                
                var accel = maxAcceleration;
                var vCurr = (float) Math.Sqrt(Math.Pow(vPrev, 2) + 2*accel*dist);

                speeds[i] = Math.Min(vCurr, speeds[i]);
            }
            
            return speeds;
        }
        
        private void UpdateTargetDistance()
        {
            var rigidBody = this.currDroneState != null ? this.currDroneState.GetComponent<Rigidbody>() : null;
            float currentSpeed = rigidBody != null ? rigidBody.linearVelocity.magnitude : 0f;
            targetDistance = Mathf.Clamp(currentSpeed / 2f, 1f, 2f);
        }

        private Vector2 GetTargetPoint(Vector3 currentPosition)
        {
            var (closestPoint, startIndex) = GetClosestPointOnPath(currentPosition);

            // Choose correct line segment for the target
            float remainingDistance = this.targetDistance;
            remainingDistance -= Vector2.Distance(this.waypoints[startIndex + 1].position, closestPoint);
            while (true)
            {
                if (startIndex >= this.waypoints.Count - 2)
                {
                    // Reached the end
                    return this.waypoints[this.waypoints.Count - 1].position;
                }

                if (remainingDistance < 0)
                {
                    break;
                }

                startIndex++;
                remainingDistance -= Vector2.Distance(this.waypoints[startIndex].position,
                    this.waypoints[startIndex + 1].position);
            }
            
            remainingDistance += Vector2.Distance(this.waypoints[startIndex].position,
                this.waypoints[startIndex + 1].position);
            Node start = this.waypoints[startIndex];
            Node end = this.waypoints[startIndex + 1];
            Vector2 direction = Vector2.Normalize(end.position - start.position);
            Vector2 targetPoint = start.position + (direction * remainingDistance);
            this.targetPoint = targetPoint;
            return targetPoint;
        }
        
        
        private (Vector2, int) GetClosestPointOnPath(Vector3 currentPosition)
        {
            Vector2 closestPointPath = new Vector2(0f, 0f);
            float closestDistance = float.MaxValue;
            int segmentCount = this.waypoints.Count - 1;
            int searchStart = Mathf.Clamp(this.bestStartIndex, 0, Mathf.Max(0, segmentCount - 1));
            int searchEnd = Mathf.Min(segmentCount - 1, searchStart + 6);
            int bestIndex = searchStart;

            for (int i = searchStart; i <= searchEnd; i++)
            {
                Node start = this.waypoints[i];
                Node end = this.waypoints[i + 1];
                Vector2 closestPointLine = GetClosestPointToLine(start.position, end.position,
                    new Vector2(currentPosition.x, currentPosition.z));
                float distance = Vector2.Distance(closestPointLine, new Vector2(currentPosition.x, currentPosition.z));
                if (distance < closestDistance && !CollisionCheck(closestPointLine, currentPosition))
                {
                    closestPointPath = closestPointLine;
                    closestDistance = distance;
                    bestIndex = i;
                }
            }

            if (closestDistance == float.MaxValue)
            {
                for (int i = 0; i < segmentCount; i++)
                {
                    Node start = this.waypoints[i];
                    Node end = this.waypoints[i + 1];
                    Vector2 closestPointLine = GetClosestPointToLine(start.position, end.position,
                        new Vector2(currentPosition.x, currentPosition.z));
                    float distance = Vector2.Distance(closestPointLine, new Vector2(currentPosition.x, currentPosition.z));
                    if (distance < closestDistance && !CollisionCheck(closestPointLine, currentPosition))
                    {
                        closestPointPath = closestPointLine;
                        closestDistance = distance;
                        bestIndex = i;
                    }
                }
            }

            this.bestStartIndex = bestIndex;
            this.closestPoint = closestPointPath;
            return (closestPointPath, bestIndex);
        }

        
        private bool CollisionCheck(Vector2 start, Vector3 currentPosition)
        {
            // TODO: Might implement later
            return false;
            //return Physics.Linecast(
            //new Vector3(start.x, currentPosition.y, start.y),
            //currentPosition);
        }

        private Vector2 GetClosestPointToLine(Vector2 start, Vector2 end, Vector2 pos)
        {
            // Simple projection along the two nodes
            Vector2 segmentDirection = Vector2.Normalize(end - start);
            Vector2 relativePostion = pos - start;
            float magnitude = Vector2.Dot(segmentDirection, relativePostion);
            float cappedMagnitude = Mathf.Clamp(magnitude, 0f, (end - start).magnitude);
            return start + (segmentDirection * cappedMagnitude);
        }

        
        private bool CheckGoalReached()
        {
            // Check distance on the X/Z plane to ignore elevation differences
            Vector2 currentPos2D = new Vector2(currDroneState.position.x, currDroneState.position.z);
            Vector2 goal2D = new Vector2(goal.x, goal.z);

            if (Vector2.Distance(currentPos2D, goal2D) <= StoppingDistance)
            {
                // Debug.Log("Goal reached!");
                HasReachedGoal = true;
                h = 0f;
                v = 0f;
                return true;
            }
    
            return false;
        }
        
    }
}
