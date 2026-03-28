using System.Collections.Generic;
using UnityEngine;
using PacMan.Agent.PathFinding;
using System;
using Scripts.Vehicle;

namespace PacMan.Agent.PathFollowing
{
    public class DroneControlling
    {
        // Constants
        private const float K_P_POSITION = 1f;
        private const float K_D_POSITION = 1.5f;
        private const float K_P_VELOCITY = 10f;
        private const float K_D_VELOCITY = 0f;
        private float MAX_DRONE_ACCEL = 15f;
        private float MAX_DRONE_SPEED = 15f;
        
        public bool HasReachedGoal = false;
        public float StoppingDistance = 3f; // Adjust based on the size of your car/goal
        
        // Outputs
        public float h { get; set; }  // Horizontal acceleration command [-1, 1]
        public float v { get; set; }  // Forward acceleration command [-1, 1]
        
        // State tracking
        public List<Node> waypoints { get; set; }
        public Vector3 goal { get; set; }
        public Vector3 prevDronePos { get; set; }
        public Transform currDroneState { get; set; }
    
        // Path tracking
        public float targetDistance { get; set; }
        public Vector2 closestPoint { get; set; }
        public Vector2 targetPoint { get; set; }
        public int bestStartIndex { get; set; }
        public List<float> targetSpeeds { get; set; }
        private Vector2 lastVelError { get; set; }
        private Vector2 lastPosError { get; set; }
        
        public DroneControlling(List<Node> waypoints, Vector3 goal, Transform droneState)
        {
            this.waypoints = waypoints;
            this.goal = goal;
            prevDronePos = droneState.position;  
            currDroneState = droneState;       
            bestStartIndex = 0;
            targetDistance = 5f;
            lastVelError = Vector2.zero;
            lastPosError = Vector2.zero;
            h = 0f;
            v = 0f;
            targetSpeeds = GenerateTargetSpeeds(waypoints);
        }
        
        public void PDCalculateMove(Transform droneTransform, DroneController drone)
        {
            currDroneState = droneTransform;
            
            if (CheckGoalReached()) return;
            
            MAX_DRONE_ACCEL = drone.max_acceleration;
            MAX_DRONE_SPEED = drone.max_speed;
            
            UpdateTargetDistance();
    
            Vector2 targetPoint = GetTargetPoint(droneTransform.position);
            Vector2 currentPos2D = new Vector2(droneTransform.position.x, droneTransform.position.z);
    
            Vector2 currentVel = new Vector2(
                (droneTransform.position.x - prevDronePos.x) / Time.fixedDeltaTime,
                (droneTransform.position.z - prevDronePos.z) / Time.fixedDeltaTime
            );
            
            float targetSpeed = GetMinTargetSpeed(this.bestStartIndex, this.waypoints);
            Vector2 targetDir = (targetPoint - currentPos2D).normalized;
            Vector2 desiredVelocity = targetDir * targetSpeed;

            Vector2 velocityError = desiredVelocity - currentVel;
            Vector2 positionError =  this.closestPoint - currentPos2D;
            
            Vector2 velDeriv = (velocityError - lastVelError) / Time.fixedDeltaTime;
            Vector2 posDeriv = (positionError - lastPosError) / Time.fixedDeltaTime;
            
            Vector2 velForce =  velocityError * K_P_VELOCITY + velDeriv*K_D_VELOCITY;
            Vector2 posForce = positionError * K_P_POSITION + posDeriv* K_D_POSITION;
            
            Vector2 total = velForce + posForce;
            
            //Debug.Log("Target Speed: " + targetSpeed);
            //Debug.Log("Current Speed" + currentVel.magnitude);

            h = Mathf.Clamp(total.x / MAX_DRONE_ACCEL, -1f, 1f);
            v = Mathf.Clamp(total.y / MAX_DRONE_ACCEL, -1f, 1f);

            lastVelError = velocityError;
            lastPosError = positionError;
            prevDronePos = droneTransform.position;
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
            Vector2 prev = path[index].position;
            Vector2 next = path[index+1].position;
            float nextDist = Vector2.Distance(pos, next);
            float totalDist = Vector2.Distance(prev, next);
            float prevSpeed = this.targetSpeeds[index];
            float nextSpeed = this.targetSpeeds[index+1];
            // Linear interpolation
            float targetSpeed = nextDist/totalDist*prevSpeed + (1 - nextDist / totalDist)*nextSpeed;
            return targetSpeed;
        }
        
        public List<float> GenerateTargetSpeeds(List<Node> path)
        {
            // Kapania, Subosits, Gerdes "A Sequential Two-Step Algorithm for Fast Generation of Vehicle Racing Trajectories"
            
            float MaxAcceleration = MAX_DRONE_ACCEL;
            
            List<float> speeds = new List<float>();
            speeds.Add(0);
            for (int i = 1; i < path.Count - 1; i++)
            {
                Vector2 prev = path[i - 1].position;
                Vector2 curr = path[i].position;
                Vector2 next = path[i + 1].position;
                
                Vector2 enterDir = (curr - prev).normalized;
                Vector2 leaveDir = (next - curr).normalized;
                float angle = Vector2.Angle(enterDir, leaveDir);
                float dist = Math.Max((Vector2.Distance(curr, prev) + Vector2.Distance(next, curr)) / 2, 0.001f);
                float curvature = angle * Mathf.Deg2Rad / dist;
                curvature = Mathf.Pow(curvature+0.98f, 3f) - 1;
                if (curvature < 0.001f)
                {
                    speeds.Add(MAX_DRONE_SPEED);
                    continue;
                }float maxSpeed = Mathf.Sqrt(MaxAcceleration/curvature);
                maxSpeed = Mathf.Min(MAX_DRONE_SPEED, maxSpeed);
                speeds.Add(maxSpeed);
            }
            speeds[0] = MAX_DRONE_SPEED;
            speeds.Add(MAX_DRONE_SPEED);
            
            // Backwards
            for (int i = path.Count - 2; i >= 0; i--)
            {
                Vector2 curr = path[i].position;
                Vector2 next = path[i + 1].position;
                float dist =  Vector2.Distance(curr, next);
                
                float vNext = speeds[i + 1];

                // Safety margin as someof the accel is located to correcting errors
                float decel = MaxAcceleration;
                
                float vCurr = (float) Math.Sqrt(Math.Pow(vNext, 2) + 2*decel*dist);

                speeds[i] = Math.Min(vCurr, speeds[i]);
            }
            
            for (int i = 1; path.Count > i; i++)
            {
                Vector2 curr = path[i].position;
                Vector2 prev = path[i - 1].position;
                float dist =  Vector2.Distance(curr, prev);
                
                float vPrev = speeds[i - 1];
                
                float accel = MaxAcceleration;
                float vCurr = (float) Math.Sqrt(Math.Pow(vPrev, 2) + 2*accel*dist);

                speeds[i] = Math.Min(vCurr, speeds[i]);
            }
            
            return speeds;
        }
        
        private void UpdateTargetDistance()
        {
            float currentSpeed = Vector3.Distance(this.currDroneState.position, this.prevDronePos) / Time.fixedDeltaTime;
            this.targetDistance = Mathf.Clamp(currentSpeed / 2f, 1f, 2f);
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
            int bestIndex = 0;
            for (int i = 0; i < this.waypoints.Count - 1; i++)
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
                Debug.Log("Goal reached!");
                HasReachedGoal = true;
                h = 0f;
                v = 0f;
                return true;
            }
    
            return false;
        }
        
    }
}