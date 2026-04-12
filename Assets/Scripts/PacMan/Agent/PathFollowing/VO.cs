using UnityEngine;
using System.Linq;
using System.Collections.Generic;
using PacMan.Agent.Debugging;

namespace PacMan.Agent.PathFollowing
{
    /// <summary>
    /// The state of a vehicle for the VO algorithm.
    /// </summary>
    public struct VehicleState
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public Vector2 Forward;
        public float Radius;
    }
    
    public class VO
    {
        // --- Agent Capabilities ---
        private readonly float _maxAcceleration;
        private readonly float _vehicleRadius;
        
        // --- Safety Parameters ---
        private const float TimeHorizon = 8.0f;           // Dynamic obstacles
        private const float WallTimeHorizon = 0.2f;       // Static obstacles (CBF-style)
        private const int AccelSamples = 100;               // How many points to check on our acceleration grid
        private const float AgentRadiusPadding =  0.01f;
        private const float OpponentRadiusPadding = 0.5f;
        private const float StaticObstacleRadiusPadding = 0.01f;
        
        public VO(Transform vehicleTransform, float maxAcceleration)
        {
            _maxAcceleration = maxAcceleration;
            _vehicleRadius = vehicleTransform.GetComponent<Collider>().bounds.extents.z + AgentRadiusPadding;
        }

        public (float, float) GetSafeAcceleration(Transform myTransform, Vector3 currentVelocity, 
            float intendedH, float intendedV, List<GameObject> lowRiskDrones, List<GameObject> highRiskDrones, 
            Collider[] staticObstacles)
        {
            var currentVelocity2 = new Vector2(currentVelocity.x, currentVelocity.z);

            if (DebugManager.Instance != null && DebugManager.Instance.vO)
            {
                // Visualize Agent's Current State & Intent
                VisualizeAgent(myTransform, currentVelocity2, intendedH, intendedV);
            }
            
            // Clamp intent to the physical capabilities of this specific drone model
            var intendedAccel2D = GetRealAcceleration(intendedH, intendedV);
            
            //Get candidate accelerations
            var candidates = GetCandidateAccelerations(intendedAccel2D);
            
            // Sort the candidates
            candidates =  SortCandidates(candidates, intendedAccel2D);
            
            //Get the obstacles
            var obstacleList = new List<VehicleState>();

            foreach (var drone in highRiskDrones)
            {
                // Safety check: Skip ourselves if we accidentally ended up in the _otherDrone array!
                if (drone.transform == myTransform) continue;
                obstacleList.Add(ConvertDroneToState(drone, highRisk: true));
                if (DebugManager.Instance != null && DebugManager.Instance.vO)
                {
                    DrawDebugCircle(drone.transform.position, obstacleList[^1].Radius, Color.red);
                    
                }
            }
            foreach (var drone in lowRiskDrones)
            {
                // Safety check: Skip ourselves if we accidentally ended up in the _otherDrone array!
                if (drone.transform == myTransform) continue;
                obstacleList.Add(ConvertDroneToState(drone, highRisk: false));
                if (DebugManager.Instance != null && DebugManager.Instance.vO)
                {
                    DrawDebugCircle(drone.transform.position, obstacleList[^1].Radius, Color.red);
                    
                }
            }
            
            var wallPlanes = new List<(Vector2 Normal, float Distance, Vector2 ClosestPoint)>();
            foreach (var obstacle in staticObstacles)
            {
                var closest3D = obstacle.ClosestPoint(myTransform.position);
                var dir3D = myTransform.position - closest3D;
                var dist3D = dir3D.magnitude;

                // If we are literally inside the wall, skip to avoid math errors (Unity will physically push us out anyway)
                if (dist3D < 0.001f) continue;

                // The normal points AWAY from the wall, towards the drone
                var normal2D = new Vector2(dir3D.x, dir3D.z).normalized;

                // Calculate effective distance from the edge of the drone to the padded wall
                var totalPadding = _vehicleRadius + StaticObstacleRadiusPadding;
                var effDist = Mathf.Max(0f, dist3D - totalPadding);

                wallPlanes.Add((normal2D, effDist, new Vector2(closest3D.x, closest3D.z)));
                
                
                if (DebugManager.Instance != null && DebugManager.Instance.vO)
                {
                    // --- NEW: Extended Wall Visualization ---
                    // 1. Find the 3D point where the padded wall boundary sits
                    var paddedWallPoint3D =
                        closest3D + new Vector3(normal2D.x, 0, normal2D.y) * StaticObstacleRadiusPadding;

                    // 2. Calculate the tangent (perpendicular to the normal) to draw the flat surface of the plane
                    var wallTangent = new Vector3(-normal2D.y, 0, normal2D.x);

                    // 3. Draw an orange line representing the padded boundary (extends 2 units in both directions)
                    Debug.DrawLine(paddedWallPoint3D - wallTangent * 2f, paddedWallPoint3D + wallTangent * 2f,
                        new Color(1f, 0.5f, 0f));

                    // 4. (Optional) Draw a short grey ray showing the normal direction pointing away from the wall
                    Debug.DrawRay(paddedWallPoint3D, new Vector3(normal2D.x, 0, normal2D.y), Color.grey);

                }
            }

            var bestAccel = Vector2.zero;
            var maxColTime = 0f;
            
            float h, v;
            
            var mostThreateningObstacleIndex = -1; // Track the threat for the chosen candidate
            Vector2 mostThreateningWallPos = Vector2.zero;
            
            // Evaluate each candidate to find the best one
            foreach (var candidate in candidates)
            {
                var newVel = currentVelocity2 + candidate * Time.fixedDeltaTime;
                
                // Evaluate VO
                var (colTime, obsIndex) = LowestTimeToCollision(newVel, new Vector2(myTransform.position.x, myTransform.position.z), obstacleList);
                Vector2 currentCandidateWallPos = Vector2.zero;
                
                // Evaluate Static Geometry (Unity Physics)
                var newVel3D = new Vector3(newVel.x, 0, newVel.y);
                if (newVel3D.sqrMagnitude > 0.0001f)
                {
                    // Evaluate Static Geometry (Planes)
                    foreach (var plane in wallPlanes)
                    {
                        // Calculate how fast this velocity is moving directly INTO the wall plane
                        // (plane.Normal points towards the drone, so -plane.Normal points into the wall)
                        var velTowardsWall = Vector2.Dot(newVel, -plane.Normal);
            
                        // Only care if we are actually moving towards the wall
                        if (velTowardsWall > 0.0001f)
                        {
                            float wallColTime;
        
                            if (plane.Distance <= 0.001f)
                            {
                                // We've breached the padding! Instead of colTime = 0 (which causes the 
                                // drone to give up and coast), we map the impact severity to a tiny positive time.
                                // Smaller velocity into the wall = larger fake time = better candidate score.
                                // This forces the algorithm to pick the candidate that brakes the hardest.
                                wallColTime = 0.001f / velTowardsWall;
                            }
                            else
                            {
                                wallColTime = plane.Distance / velTowardsWall;
                            }
                
                            // NEW: CBF-style check. Only treat the wall as a threat if impact is imminent.
                            if (wallColTime <= WallTimeHorizon)
                            {
                                // If the wall collision is happening sooner than a dynamic collision
                                if (colTime > TimeHorizon || wallColTime < colTime)
                                {
                                    colTime = wallColTime;
                                    obsIndex = -2; // Special index to denote a static wall threat
                                    currentCandidateWallPos = plane.ClosestPoint;
                                }
                            }
                        }
                    }
                }
                
                if (colTime > TimeHorizon)
                {
                    // If perfectly safe, optionally draw line to the furthest tracked threat (if any exist)
                    if (DebugManager.Instance != null && DebugManager.Instance.vO && obsIndex != -1 && obsIndex != -2)
                    {
                        var threat = obstacleList[obsIndex];
                        var threatPos3D = new Vector3(threat.Position.x, myTransform.position.y, threat.Position.y);
                        
                        Debug.DrawLine(myTransform.position, threatPos3D, Color.red);
                    }
                    (h, v) = GetAccelerationOutput(candidate);

                    return (h, v);
                }

                if (colTime <= maxColTime)//If worse than best time
                {
                    continue;
                }
                
                maxColTime = colTime;
                bestAccel = candidate;
                mostThreateningObstacleIndex = obsIndex;
                
                if (obsIndex == -2) 
                {
                    mostThreateningWallPos = currentCandidateWallPos; 
                }
            }
            
            // Draw the debug line for the best fallback candidate we are forced to use
            if (DebugManager.Instance != null && DebugManager.Instance.vO)
            {
                if (mostThreateningObstacleIndex != -1)
                {
                    if (mostThreateningObstacleIndex == -2)
                    {
                        // Safe drawing for static walls
                        Debug.DrawLine(myTransform.position,
                            new Vector3(mostThreateningWallPos.x, myTransform.position.y, mostThreateningWallPos.y),
                            Color.red);
                    }
                    else
                    {
                        // Safe drawing for dynamic agents
                        var threatPos = obstacleList[mostThreateningObstacleIndex].Position;
                        Debug.DrawLine(myTransform.position, new Vector3(threatPos.x, myTransform.position.y, threatPos.y),
                            Color.red);
                    }
                }
                // Visualize the finalized, chosen acceleration in magenta
                Debug.DrawRay(myTransform.position + currentVelocity, new Vector3(bestAccel.x, 0, bestAccel.y), Color.magenta);
            }
            
            (h, v) = GetAccelerationOutput(bestAccel);
            
            return (h, v);
        }
        
        
        private VehicleState ConvertDroneToState(GameObject drone, bool highRisk)
        {
            Vector3 droneVel;
            if (drone.GetComponent<Rigidbody>() != null)
            {
                droneVel = drone.GetComponent<Rigidbody>().linearVelocity;
            }
            else
            {
                droneVel = Vector3.zero;
            }
            
            var dronePos = new Vector2(drone.transform.position.x, drone.transform.position.z);
            
            var radiusPadding = highRisk ? OpponentRadiusPadding : AgentRadiusPadding;
            
            var radius = drone.GetComponent<Collider>().bounds.extents.z;
            return new VehicleState
            {
                Position = dronePos,
                Velocity = new Vector2(droneVel.x, droneVel.z), 
                Forward = new Vector2(droneVel.x, droneVel.z).normalized, 
                Radius = radius + radiusPadding
            };
        }
        
        
        /// <summary>
        /// Sort candidates based on how close they are to the intended acceleration. 
        /// </summary>
        /// <param name="candidates">The list to be sorted. </param>
        /// <param name="intendedAccel2D">Intended acceleration. </param>
        /// <returns>The sorted list. </returns>
        private Vector2[] SortCandidates(Vector2[] candidates, Vector2 intendedAccel2D)
        {
            return candidates
                .OrderBy(c => (c - intendedAccel2D).sqrMagnitude)
                .ToArray();
        }
        

        private (float, int) LowestTimeToCollision(Vector2 velocity, Vector2 position,
            List<VehicleState> obstacles)
        {
            int closestObstacleIndex = -1; // -1 means no collision found
            var t = float.MaxValue;
            for (int i = 0; i < obstacles.Count; i++)
            {
                var tNew = TimeToCollision(velocity, position, obstacles[i]);
                if (tNew >= 0f && tNew < t)
                {
                    t = tNew;
                    closestObstacleIndex = i;
                }
            }
            
            return (t,  closestObstacleIndex);
        }
        
        
        /// <summary>
        /// Returns to collision with an object. Negative if no collision. 
        /// </summary>
        /// <param name="velocity">Velocity of the drone. </param>
        /// <param name="position">Position of the drone. </param>
        /// <param name="obstacle">Obstacle we want to avoid. </param>
        /// <returns>Time to collision, negative if no collision. </returns>
        private float TimeToCollision(Vector2 velocity, Vector2 position, VehicleState obstacle)
        {
            var relativePosition = position - obstacle.Position;
            var relativeVelocity = velocity - obstacle.Velocity;
            
            var combinedRadius = _vehicleRadius + obstacle.Radius;
            
            // Calculate Time to Collision (TTC)
            // Starting from the equation ||V*t + P|| = R, we derive a quadratic formula to solve for t (time until collision).
            // Quadratic equation: a*t^2 + b*t + c = 0
            var a = relativeVelocity.sqrMagnitude;
            var b = 2f * Vector2.Dot(relativeVelocity, relativePosition);
            var c = relativePosition.sqrMagnitude - combinedRadius * combinedRadius;

            // --- Handle agents that are already intersecting ---
            if (c < 0f)
            {
                // b represents the direction of relative velocity compared to relative position.
                // If b <= 0, the agents are moving towards each other (or perfectly parallel).
                // If b > 0, they are moving apart.
                if (b <= 0f) 
                {
                    // Penalize moving deeper, but break ties by rewarding the strongest braking!
                    // b represents how fast they are moving together. -b makes it positive.
                    return 0.001f / Mathf.Max(0.0001f, -b);
                }
                // Moving apart! Treat this as a perfectly an escape route.
                // Calculate the time it will take to EXIT the circle (t2)
                var pEsc = b / a;
                var qEsc = c / a;
                var tExit = (-pEsc / 2f) + Mathf.Sqrt((pEsc * pEsc / 4f) - qEsc);
                
                // Trick the evaluation loop: smaller exit time -> larger "safe" time score.
                // We cap it just below TimeHorizon so it doesn't early-exit the candidate search,
                // forcing the algorithm to evaluate all options and pick the FASTEST escape route.
                return Mathf.Min(0.5f / tExit, TimeHorizon - 0.01f);
            }
            // --------------------------------------------------------
            
            // If a is near zero, relative velocity is zero (we are matching speeds perfectly)
            if (a < 0.0001f) return -1f;

            // Convert to p-q form: t^2 + (b/a)*t + (c/a) = 0
            var p = b / a;
            var q = c / a;

            var inSqrt = (p * p / 4f) - q;

            if (inSqrt < 0)
            {
                // No real roots means no collision
                return -1f;
            }

            var sqrtTerm = Mathf.Sqrt(inSqrt);

            // Get the collision times
            var t1 = (-p / 2f) - sqrtTerm;
            var t2 = (-p / 2f) + sqrtTerm;

            // We want the smallest positive time
            var t = -1f;
            if (t1 >= 0 && (t2 < 0 || t1 < t2)) t = t1;
            else if (t2 >= 0) t = t2;
            
            return t;
        }
        
        
        /// <summary>
        /// Get the candidate accelerations to evaluate. Candidate 0 is always the exact intended acceleration, and the rest are distributed in a circle around it.
        /// </summary>
        /// <param name="intendedAccel2D">Intended acceleration. </param>
        /// <returns>Candidate accelerations. </returns>
        private Vector2[] GetCandidateAccelerations(Vector2 intendedAccel2D)
        {
            // Generate our candidate accelerations
            var candidates = new Vector2[AccelSamples];
            
            // We ALWAYS include the exact intended acceleration as candidate 0
            candidates[0] = intendedAccel2D;
            
            // Distribute the remaining samples in a circle around the drone
            // (These are our escape options if the intended path is blocked)
            for (var i = 1; i < AccelSamples; i++)
            {
                var angle = (i - 1) * (Mathf.PI * 2f) / (AccelSamples - 1);
                candidates[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * _maxAcceleration;
            }
            
            return candidates;
        }
        
        
        /// <summary>
        /// Convert the accel input in the same way the Move function does, to ensure we're evaluating the same physical acceleration that the drone will actually experience.
        /// </summary>
        /// <param name="h">Horizontal acceleration. </param>
        /// <param name="v">Vertical acceleration. </param>
        /// <returns>The physical acceleration. </returns>
        private Vector2 GetRealAcceleration(float h, float v)
        {
            var acceleration = (Vector3.right * h + Vector3.forward * v) * _maxAcceleration;
            if (acceleration.magnitude > _maxAcceleration)
            {
                acceleration = acceleration.normalized * _maxAcceleration;
            }
            
            return new Vector2(acceleration.x, acceleration.z);
        }


        /// <summary>
        /// Convert the physical acceleration to the one used in Move. 
        /// </summary>
        /// <param name="outputAccel2D">Our best physical acceleration. </param>
        /// <returns>The output h and v. </returns>
        private (float, float) GetAccelerationOutput(Vector2 outputAccel2D)
        {
            var acceleration = outputAccel2D /  _maxAcceleration;
            return (acceleration.x, acceleration.y);
        }
        
        
        private void VisualizeAgent(Transform myTransform, Vector3 currentVelocity, 
            float intendedH, float intendedV)
        {
            // Green line: Current Velocity
            Debug.DrawRay(myTransform.position, currentVelocity, Color.green);
            
            // Blue line: Intended Acceleration (scaled for visibility, applied at the tip of velocity)
            var intendedAccelVector = new Vector3(intendedH, 0, intendedV);
            Debug.DrawRay(myTransform.position + currentVelocity, intendedAccelVector, Color.blue);
            
            // Draw this vehicle radius
            DrawDebugCircle(myTransform.position, _vehicleRadius, Color.cyan);
        }
        
        
        private void DrawDebugCircle(Vector3 center, float radius, Color color)
        {
            int segments = 24;
            float angle = 0f;
            float step = Mathf.PI * 2f / segments;
            
            // Start point 
            Vector3 prevPoint = center + new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
            
            for (int i = 1; i <= segments; i++)
            {
                angle += step;
                Vector3 nextPoint = center + new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
                Debug.DrawLine(prevPoint, nextPoint, color);
                prevPoint = nextPoint;
            }
        }
    }
}