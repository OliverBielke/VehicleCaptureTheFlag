using System;
using System.Collections.Generic;
using UnityEngine;
using Quaternion = UnityEngine.Quaternion;
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;
using PacMan.Agent.PathFollowing;

namespace PacMan.Agent.PathFinding
{
    public class CGSmoother
    {
        //private const float ALPHA = 0.00005f;
        private const float ALPHA = 0.00005f; //Changed from 0.00005f for faster runtime
        public const float D_MAX = 1.0f; // Was 2.4f
        //private const float W_COLLISION = 10000f;
        private const float W_COLLISION = 4000f; //Changed to 200 for new mechanics, original is 10000


        //private const float W_CURVATURE = 5f;
        private const float W_CURVATURE = 0.5f; //Changed to 0.5 for new mechanics, original is 5
        //private const float W_SMOOTHNESS = 1f;
        private const float W_SMOOTHNESS = 10f; //Changed to 10 for new mechanics, original is 1
        //public const int DISTANCE_MAP_RESOLUTION = 800;
        public const int DISTANCE_MAP_RESOLUTION = 200; //Changed to 150 for faster runtime
        //private const int MAX_OUTER_ITER = 5;
        private const int MAX_OUTER_ITER = 1; //Changed to 1 for faster runtime
        //private const int MAX_INNER_ITER = 50000;
        private const int MAX_INNER_ITER = 1000; //Changed to 1000 for faster runtime
        private const float MAX_GRADIENT = 100f;
        
        private const float RIGHT_DRIVE = 0.0f; //Pushes path to right side
        /* Shared cache to speed up computations */
        private static bool _cacheReady = false;
        private static Vector2[][] _sharedDistMap;
        private static Vector3 _sharedDistStart;
        private static float _sharedStepX;
        private static float _sharedStepZ;
        private static int _cachedMapInstanceId = -1;
        private static float _cachedCarHeight = -1f;
        private readonly float maxSpeed;
        private readonly float maxAcceleration;

        public CGSmoother(float carHeight, Collider map, float maxSpeed, float maxAcceleration)
        {
            this.carHeight = carHeight;
            this.maxSpeed = maxSpeed;
            this.maxAcceleration = maxAcceleration;

            EnsureDistanceMapCached(map, carHeight);

            this.distMap = _sharedDistMap;
            this.distStart = _sharedDistStart;
            this.stepX = _sharedStepX;
            this.stepZ = _sharedStepZ;
        }

        private float carHeight { get; set; }
        public Vector2[][] distMap { get; set; }
        public Vector3 distStart { get; set; }
        public float stepX { get; set; }
        public float stepZ { get; set; }

        private void CreateDistanceMap(Collider map)
        {
            Vector3 mapOrigin = map.bounds.center;
            Vector3 mapExtents = map.bounds.extents;
            this.distStart = mapOrigin - mapExtents;
            this.stepX = mapExtents.x * 2 / (DISTANCE_MAP_RESOLUTION - 1);
            this.stepZ = mapExtents.z * 2 / (DISTANCE_MAP_RESOLUTION - 1);
            int obstacles = LayerMask.GetMask("Obstacle");

            this.distMap = new Vector2[DISTANCE_MAP_RESOLUTION][];
            for (int i = 0; i < DISTANCE_MAP_RESOLUTION; i++)
            {
                distMap[i] = new Vector2[DISTANCE_MAP_RESOLUTION];
            }

            for (int i = 0; i < DISTANCE_MAP_RESOLUTION; i++)
            {
                for (int j = 0; j < DISTANCE_MAP_RESOLUTION; j++)
                {
                    Vector3 pos = this.distStart + new Vector3(i * stepX, this.carHeight, j * stepZ);
                    distMap[i][j] = GetClosestObject(pos, obstacles);
                }
            }

            //Debug.Log("Distance Map Done, Resolution = " + DISTANCE_MAP_RESOLUTION);
        }

        private Vector2 GetClosestObject(Vector3 pos, int obstacles)
        {
            float searchDist = 50f;
            float closestDistance = 50f;
            Vector2 closestPos = Vector2.positiveInfinity;
            Collider[] colliders = Physics.OverlapSphere(pos, searchDist, obstacles);

            foreach (Collider collider in colliders)
            {
                Vector3 obsPos;
                if (collider is MeshCollider) // ClosestPoint() Does not work, might need more work later
                {
                    Vector3 estimatedClosest = collider.bounds.center;
                    Vector3 estClosestFlat = new Vector3(estimatedClosest.x, carHeight, estimatedClosest.z);
                    Vector3 dir = (estClosestFlat - pos).normalized;
                    // Shoots a 2D line at the estimated position to increase likelihood of actually hitting the real position
                    if (Physics.BoxCast(pos, new Vector3(0.5f, 0.5f, 0.5f), dir,out RaycastHit hit, Quaternion.LookRotation(dir), 50f))
                    {
                        if (hit.collider.gameObject.name == "CarA1(Clone)" || hit.collider.gameObject.name == "startOverhead")
                        {
                            continue;
                        }
                        obsPos = hit.point;
                    }
                    else
                    {
                        obsPos = estimatedClosest;
                    }

                }
                else {
                    obsPos = collider.ClosestPoint(pos);
                }

                float distance = Vector3.Distance(obsPos, pos);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestPos = V3ToV2(obsPos);
                }
            }

            return closestPos;
        }

        private Vector2 GetDistMapEntry(Vector2 v)
        {
            var indexX = Mathf.RoundToInt(Mathf.Clamp((v.x - distStart.x) / stepX, 1f, DISTANCE_MAP_RESOLUTION-1));
            var indexZ = Mathf.RoundToInt(Mathf.Clamp((v.y - distStart.z) / stepZ, 1f, DISTANCE_MAP_RESOLUTION-1));
            return distMap[indexX][indexZ];
        }

        private static List<Node> GetResampledPath(List<Node> path, float spacing)
        {
            List<Node> newPath = new();
            newPath.Add(new Node(path[0].position.x, path[0].position.y));
            var currSpace = spacing;
            for (int i = 0; i < path.Count - 1; i++)
            {
                Vector2 curr = path[i].position;
                Vector2 next = path[i + 1].position;
                while (true)
                {
                    float dist = Vector2.Distance(curr, next);
                    if (dist < currSpace)
                    {
                        currSpace -= dist;
                        break;
                    }
                    Vector2 dir = (next - curr).normalized;
                    Vector2 newPos = curr + dir * currSpace;
                    Node newNode = new Node(newPos.x, newPos.y);
                    newPath.Add(newNode);
                    
                    currSpace = spacing;
                    curr = newNode.position;
                }
            }
            
            newPath.Add(new Node(path[path.Count - 1].position.x, path[path.Count - 1].position.y));
            
            for (int i = 1; i < newPath.Count; i++)
            {
                newPath[i].parent = newPath[i - 1];
            }
            
            return newPath;
        }

        public List<Node> GetSmoothedPath(List<Node> path)
        {
            var maxIter = MAX_OUTER_ITER;
            var iter = 0;
            List<float> targetSpeed;
            while (maxIter > iter)
            {
                var spacing = 1; // Changed to 5 for faster runtime, original was 2
                path = GetResampledPath(path, spacing);
                targetSpeed = DroneControlling.GenerateTargetSpeeds(path, maxSpeed, maxAcceleration);
                path = RunGradientDescent(path, targetSpeed);
                iter++;
            }

            return path;
        }

        private List<Node> RunGradientDescent(List<Node> path, List<float> targetSpeed)
        {
            // Gradient Descent
            var maxIter = MAX_INNER_ITER;
            var convergenceThreshold = 0.001f;
            for (int i = 0; i < maxIter; i++)
            {
                Vector2[] gradients = new Vector2[path.Count];
                for (int j = 1; j < path.Count - 1; j++)
                {
                    Vector2[] jGradient = CalculateGradient(path, j, targetSpeed[j]);
                    for (int k = 0; k < 3; k++)
                    {
                        gradients[k + j - 1] += jGradient[k];
                    }
                }

                float totalGradientMagnitude = 0;
                for (int j = 1; j < path.Count - 1; j++)
                {
                    totalGradientMagnitude += gradients[j].magnitude;

                     if (gradients[j].magnitude > MAX_GRADIENT)
                     {
                         gradients[j] = gradients[j].normalized*MAX_GRADIENT;
                     }
                    path[j].position -= ALPHA * (gradients[j]);
                }
                
                if (i % 1000 == 0)
                {
                    //Debug.Log("Iteration " + i);
                    //Debug.Log("Magnitude " + totalGradientMagnitude);
                }

                if (totalGradientMagnitude < convergenceThreshold)
                {
                    //Debug.Log("Convergence");
                    //Debug.Log("Iterations: " + i);
                    return path;
                }
            }

            Debug.Log("No Convergence");
            return path;
        }

        private Vector2[] CalculateGradient(List<Node> path, int index, float targetSpeed)
        {
            // Dolgov Paper
            Vector2 x0 = path[index - 1].position;
            Vector2 x1 = path[index].position;
            Vector2 x2 = path[index + 1].position;
            
            Vector2 forwardDir = (x2 - x0).normalized;
            Vector2 rightDir = new Vector2(forwardDir.y, -forwardDir.x); //Forward is (x,y), so right is (y, -x)

            Vector2[] gradients = new Vector2[3];

            // Collision
            Vector2 o = GetNearestObstacle(x1);
            if ((!float.IsPositiveInfinity(o.x) && Vector2.Distance(o, x1) < D_MAX))
            {
                if (Vector2.Distance(o, x1) < 0.001f)
                {
                    o += new Vector2(0.01f, 0.01f);
                }

                gradients[1] += W_COLLISION * (2 * ((x1 - o).magnitude - D_MAX) * (x1 - o) / ((x1 - o).magnitude));
            }
            
            // Much simpler, also no singularity so more stable :)
            Vector2 laplace = x0 - 2 * x1 + x2;
            Vector2 dir = laplace.normalized;
            //float adjustment = ((float)Math.Pow(laplace.magnitude*5, 5) + 4f * laplace.magnitude);
            float adjustment = laplace.magnitude; // Changed to linear for faster runtime, original is above
            
            // Adjust curvature weight based on target speed, dont need curvature if we are slow
            float wCurvature = W_CURVATURE -2.5f + Mathf.Sqrt(targetSpeed)/2;
            
            gradients[1] += wCurvature * -4 * dir * adjustment;
            gradients[1] += RIGHT_DRIVE * -rightDir;
            gradients[0] += wCurvature * 2 * dir* adjustment;
            gradients[2] += wCurvature * 2 * dir* adjustment;
            


            // Smoothness (simple derivation, sign flipped due to negative x1)
            gradients[1] += W_SMOOTHNESS * (2 * (x1 - x0) + 2 * (x1 - x2));

            return gradients;
        }

        private Vector2 CalculateOrthogonal(Vector2 v1, Vector2 v2)
        {
            if (Vector2.Dot(v1, v2) < 0.0001)
            {
                // Avoid division by zero
                return v1;
            }

            // Orthogonal = v1 - projection(v1, v2)
            Vector2 projection = (Vector2.Dot(v1, v2) / (Vector2.Dot(v2, v2))) * v2;
            return v1 - projection;
        }

        private Vector2 GetNearestObstacle(Vector2 pos)
        {
            return GetDistMapEntry(pos);
        }

        private Vector2 V3ToV2(Vector3 v3)
        {
            return new Vector2(v3.x, v3.z);
        }
        private static void EnsureDistanceMapCached(Collider map, float carHeight)
        {
            int mapId = map.GetInstanceID();

            if (_cacheReady &&
                _cachedMapInstanceId == mapId &&
                Mathf.Abs(_cachedCarHeight - carHeight) < 0.001f)
            {
                return;
            }

            BuildDistanceMap(map, carHeight);

            _cachedMapInstanceId = mapId;
            _cachedCarHeight = carHeight;
            _cacheReady = true;
        }
        private static void BuildDistanceMap(Collider map, float carHeight)
        {
            Vector3 mapOrigin = map.bounds.center;
            Vector3 mapExtents = map.bounds.extents;
            _sharedDistStart = mapOrigin - mapExtents;
            _sharedStepX = mapExtents.x * 2 / (DISTANCE_MAP_RESOLUTION - 1);
            _sharedStepZ = mapExtents.z * 2 / (DISTANCE_MAP_RESOLUTION - 1);

            int obstacles = LayerMask.GetMask("Obstacle");

            _sharedDistMap = new Vector2[DISTANCE_MAP_RESOLUTION][];
            for (int i = 0; i < DISTANCE_MAP_RESOLUTION; i++)
                _sharedDistMap[i] = new Vector2[DISTANCE_MAP_RESOLUTION];

            for (int i = 0; i < DISTANCE_MAP_RESOLUTION; i++)
            {
                for (int j = 0; j < DISTANCE_MAP_RESOLUTION; j++)
                {
                    Vector3 pos = _sharedDistStart + new Vector3(i * _sharedStepX, carHeight, j * _sharedStepZ);
                    _sharedDistMap[i][j] = GetClosestObjectStatic(pos, obstacles, carHeight);
                }
            }

            Debug.Log("Distance map built once and cached.");
        }
        private static Vector2 GetClosestObjectStatic(Vector3 pos, int obstacles, float carHeight)
        {
            float searchDist = 50f;
            float closestDistance = 50f;
            Vector2 closestPos = Vector2.positiveInfinity;
            Collider[] colliders = Physics.OverlapSphere(pos, searchDist, obstacles);

            foreach (Collider collider in colliders)
            {
                Vector3 obsPos;

                if (collider is MeshCollider)
                {
                    Vector3 estimatedClosest = collider.bounds.center;
                    Vector3 estClosestFlat = new Vector3(estimatedClosest.x, carHeight, estimatedClosest.z);
                    Vector3 dir = (estClosestFlat - pos).normalized;

                    if (Physics.BoxCast(pos, new Vector3(0.5f, 0.5f, 0.5f), dir, out RaycastHit hit, Quaternion.LookRotation(dir), 50f))
                    {
                        if (hit.collider.gameObject.name == "CarA1(Clone)" || hit.collider.gameObject.name == "startOverhead")
                            continue;

                        obsPos = hit.point;
                    }
                    else
                    {
                        obsPos = estimatedClosest;
                    }
                }
                else
                {
                    obsPos = collider.ClosestPoint(pos);
                }

                float distance = Vector3.Distance(obsPos, pos);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestPos = new Vector2(obsPos.x, obsPos.z);
                }
            }

            return closestPos;
        }
    }
    
    public class Node
    {
        public Node(float x, float y)
        {
            this.position = new Vector2(x, y);
            this.parent = null;
        }

        public Vector2 position { get; set; }
        public Node parent { get; set; }
    }
}