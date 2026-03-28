using System;
using System.Collections.Generic;
using UnityEngine;
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;
using PacMan.Agent.PathFinding; //Our pathfinding namespace

namespace PacMan.Agent.PathFollowing
{
    public class CarControlling
    {
        private const float K_P_STEERING = 0.6f;
        private const float K_D_STEERING = 0.3f;
        private const float K_P_SPEED = 1f;
        private const float K_D_SPEED = 0f;
        
        
        public int priority;

        public const float FRICTION = 1f;
        public const float CURV_CONST = 1f;
        public const float G = 9.81f;

        public bool isReversing = false;
        public float reverseTimer = 0f;
        
        public bool HasReachedGoal = false;
        public float stoppingDistance = 3f; // Adjust based on the size of your car/goal

        public CarControlling(List<Node> waypoints, Vector3 goal, Transform carState)
        {
            this.steering = 0f;
            this.acceleration = 0.5f;
            this.footbrake = 1f;
            this.handbrake = 0f;
            this.waypoints = waypoints;
            this.targetDistance = 1;
            this.lastSteeringError = 0f;
            this.goal = goal;
            this.lastSpeedError = 0f;
            this.prevCarPos = carState.position;
            this.prevCarState = carState;
            this.targetSpeeds = GenerateTargetSpeeds(waypoints);
        }

        public float steering { get; set; }
        public float acceleration { get; set; }
        public List<Node> waypoints { get; set; }
        public float targetDistance { get; set; }
        public float footbrake { get; set; }
        public float handbrake { get; set; }
        public Vector2 closestPoint { get; set; }
        public Vector2 targetPoint { get; set; }
        public Vector3 prevCarPos { get; set; }
        public Transform prevCarState { get; set; }
        public float lastSteeringError { get; set; }
        public Vector3 goal { get; set; }
        public int bestStartIndex { get; set; }
        public float lastSpeedError { get; set; }
        public Transform currCarState { get; set; }
        public List<float> targetSpeeds { get; set; }

        public void StanleyCalculateMove(Transform carTransform)
        {
            this.currCarState = carTransform;
            
            // Check if we reached the end before doing any calculations
            if (CheckGoalReached()) return;
            
            StanleyCalculateSteer();
            // Still use PD for acceleration
            CalculateAcceleration();
            this.prevCarPos = carTransform.position;
        }
        
        public void PerformReverse(Transform carTransform)
        {
            reverseTimer -= Time.fixedDeltaTime;

            if (reverseTimer <= 0)
            {
                isReversing = false; // reversing has finished
                return;
            }

            Vector3 targetPosition = new Vector3(this.targetPoint.x, carTransform.position.y, this.targetPoint.y);
            Vector3 dirToTarget = (targetPosition - carTransform.position).normalized;

            // Calculate angle to target, positive = target is to the right
            var angleToTarget = Vector3.SignedAngle(carTransform.forward, dirToTarget, Vector3.up);

            // Invert steering
            steering = angleToTarget > 0 ? -1f : 1f;
            
            acceleration = 0f; 
            footbrake = -1f;
            handbrake = 0f;
        }

        private void StanleyCalculateSteer()
        {
            float distToFront = 4f;
            Vector3 frontAxlePos = this.currCarState.position + this.currCarState.forward*distToFront;
            (Vector2 closestPoint, int index) = GetClosestPointOnPath(frontAxlePos);

            index = Math.Clamp(index, 0, this.waypoints.Count - 2);
            Vector2 direction = (this.waypoints[index + 1].position - this.waypoints[index].position).normalized;

            float error = Vector3.SignedAngle(
                this.currCarState.forward,
                new Vector3(direction.x, 0, direction.y),
                new Vector3(0, 1, 0));
            
            Vector2 relativeTarget = this.currCarState.InverseTransformPoint(new Vector3(closestPoint.x, this.currCarState.position.y, closestPoint.y));
            float crossTrackError = relativeTarget.x;
            float carSpeed = Mathf.Max((this.currCarState.position-this.prevCarPos).magnitude/Time.fixedDeltaTime, 1f);
            float controlGain = 5f;
            float steeringContr = Mathf.Atan(crossTrackError * controlGain / carSpeed)*Mathf.Rad2Deg;
            
            float stanleySteer = error + steeringContr;
            
            float currentRotationDeriv = Mathf.DeltaAngle(this.currCarState.eulerAngles.y, this.prevCarState.eulerAngles.y)/Time.fixedDeltaTime;

            float damping = 0.5f;
            steering = stanleySteer + damping*currentRotationDeriv;
        }

        public void PDCalculateMove(Transform carTransform)
        {
            currCarState = carTransform;
            
            // Check if we reached the end before doing any calculations
            if (CheckGoalReached()) return;
            
            if (isReversing) //if we should reverse instead
            {
                PerformReverse(carTransform);
                prevCarPos = carTransform.position;
                prevCarState = carTransform;
                return;
            }
            // Effectively two seperate PD Controllers
            UpdateTargetDistance();
            PDCalculateSteer();
            CalculateAcceleration();
            prevCarPos = carTransform.position;
            prevCarState = carTransform;
        }

        private void UpdateTargetDistance()
        {
            float currentSpeed = Vector3.Distance(this.currCarState.position, this.prevCarPos) / Time.fixedDeltaTime;
            targetDistance = Mathf.Clamp(currentSpeed / 10, 5, 20);
        }

        private void CalculateAcceleration()
        {
            float currentSpeed = Vector3.Distance(this.currCarState.position, prevCarPos) / Time.fixedDeltaTime;
            float targetSpeed = GetTargetSpeed(this.closestPoint, this.bestStartIndex, this.waypoints);

            targetSpeed *= 1.05f; // Want to avoid constantly accelerating and braking

            
            //BELOW IS A HARDCODED SOLUTION, REMOVE IT LATER!
            float speedMultiplier = 1.0f - (this.priority * 0.05f); //Hardcoded solution just to clear map intersection
            speedMultiplier = Mathf.Clamp(speedMultiplier, 0.3f, 1.0f);
            targetSpeed *= speedMultiplier;
            targetSpeed += 1;
            
            float error = (targetSpeed - currentSpeed);
            float acceleration = K_P_SPEED * error + K_D_SPEED * (error - this.lastSpeedError) / Time.fixedDeltaTime;
            
            if (acceleration < 0)
            {
                this.footbrake = 1f;
                this.acceleration = 0f;
            }
            else
            {
                this.acceleration = 1f;
                this.footbrake = 0f;
            }
            
            //Debug.Log("Target Speed: " + targetSpeed);
            //Debug.Log("Current Speed: " + currentSpeed);
            //Debug.Log("Diff: " + (currentSpeed - targetSpeed));
            
            this.lastSpeedError = error;
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

        public static float GetFinalSpeed(List<Node> path)
        {
            // Gets speed of the last node of a path
            List<float> speeds = GenerateTargetSpeeds(path);
            return speeds[speeds.Count - 1];
        }

        public static List<float> GenerateTargetSpeeds(List<Node> path)
        {
            // Kapania, Subosits, Gerdes "A Sequential Two-Step Algorithm for Fast Generation of Vehicle Racing Trajectories"
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
                float dist = Math.Max(Vector2.Distance(curr, prev) + Vector2.Distance(next, curr) / 2, 0.001f);
                float curvature = angle * Mathf.Deg2Rad / dist;
                curvature = Mathf.Max(Mathf.Pow(curvature+0.95f, 2f) - 1, curvature);
                if (curvature < 0.001f)
                {
                    speeds.Add(1000);
                    continue;
                }
                
                float maxSpeed = CURV_CONST*Mathf.Sqrt(FRICTION * G / curvature);
                speeds.Add(maxSpeed);
            }
            speeds[0] = 50;
            speeds.Add(1000);
            
            // Backwards
            for (int i = path.Count - 2; i >= 0; i--)
            {
                Vector2 curr = path[i].position;
                Vector2 next = path[i + 1].position;
                float dist =  Vector2.Distance(curr, next);
                
                float vNext = speeds[i + 1];

                // Values derived from AccelerationRecorder, Linear relationship
                float a = 0.103786f;
                float b = 0f;
                float decel = a * vNext + b;
                
                float vCurr = (float) Math.Sqrt(Math.Pow(vNext, 2) + 2*decel*dist);

                speeds[i] = Math.Min(vCurr, speeds[i]);
            }
            for (int i = 1; path.Count > i; i++)
            {
                Vector2 curr = path[i].position;
                Vector2 prev = path[i - 1].position;
                float dist =  Vector2.Distance(curr, prev);
                
                float vPrev = speeds[i - 1];
                
                float accel;
                if (vPrev <= 5.5)
                {
                    accel = 2.298f * Mathf.Pow(vPrev, 0.6582f); // Where do these numbers come from?
                }
                else
                 {
                    float a = -0.1041002f; //And these?
                    float b = 6.78387f;
                    accel = a * vPrev + b;
                }
                float vCurr = (float) Math.Sqrt(Math.Pow(vPrev, 2) + 2*accel*dist);

                speeds[i] = Math.Min(vCurr, speeds[i]);
            }
            
            return speeds;
        }

        private void PDCalculateSteer()
        {
            Vector2 currentPosition2D = new Vector2(this.currCarState.position.x, this.currCarState.position.z);
            Vector2 targetPoint = GetTargetPoint(this.currCarState.position);
            Vector2 direction = Vector2.Normalize(targetPoint - currentPosition2D);
            float error = Vector3.SignedAngle(
                this.currCarState.forward,
                new Vector3(direction.x, 0, direction.y),
                new Vector3(0, 1, 0));

            float derivative = (error - lastSteeringError) / Time.fixedDeltaTime;
            this.lastSteeringError = error;
            this.steering = error * K_P_STEERING + derivative * K_D_STEERING;
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
            Vector2 currentPos2D = new Vector2(this.currCarState.position.x, this.currCarState.position.z);
            Vector2 goal2D = new Vector2(this.goal.x, this.goal.z);

            if (Vector2.Distance(currentPos2D, goal2D) <= stoppingDistance)
            {
                Debug.Log("Goal reached!");
                this.HasReachedGoal = true;
                this.acceleration = 0f;
                this.footbrake = 1f;
                this.handbrake = 1f; // Apply handbrake for a hard stop
                this.steering = 0f;  // Straighten the wheels
                return true;
            }
    
            return false;
        }
        
    }
}