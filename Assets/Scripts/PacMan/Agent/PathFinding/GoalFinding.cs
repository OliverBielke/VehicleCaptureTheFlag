using UnityEngine;
using System.Collections.Generic;
using PacMan.Local;

namespace PacMan.Agent.PathFinding
{
    public class GoalFinding
    {
        private PacManAgentManager _agent;
        private Transform _initialDroneState;
        
        public GoalFinding(PacManAgentManager agent)
        {
            _agent = agent;
            _initialDroneState = _agent.transform;
        }
        
        /// <summary>
        /// Get position of the closest food you can eat. 
        /// </summary>
        /// <param name="activeFoodPositions">List of all the foods on the ground. </param>
        /// <param name="debug">Draws the gaol if true. </param>
        /// <returns>Position of the closest food you can eat.</returns>
        public Vector3 GetClosestEatableFood(List<GameObject> activeFoodPositions, bool debug = false)
        {
            _initialDroneState = _agent.transform;
            var agentPos = _initialDroneState.position;
            var closestPos =  Vector3.zero;
            var closestDistance = float.MaxValue;
            foreach (var foodPosition in activeFoodPositions)
            {
                //Check if food is on oppenents side
                var foodPos = foodPosition.transform.position;
                var startPos = _agent.globalStartPosition;
                var isEatableFood = (startPos.x * foodPos.x < 0); //Food is eatable if on opposite side
                
                if (!isEatableFood) //If not eatable
                {
                    continue;
                }
                
                //Check distance to food
                var curDistance = Vector3.Distance(foodPos, agentPos);

                if (closestDistance <= curDistance) //If not the closest food
                {
                    continue;
                }
                //Assign current food as closest
                closestDistance = curDistance;
                closestPos = foodPos;
            }
                
            if (debug)
            {
                var size = 0.5f;
                Debug.DrawLine(closestPos - Vector3.up * size, closestPos + Vector3.up * size, Color.red, 20f);
                Debug.DrawLine(closestPos - Vector3.left * size, closestPos + Vector3.left * size, Color.red, 20f);
                Debug.DrawLine(closestPos - Vector3.forward * size, closestPos + Vector3.forward * size, Color.red, 20f);
            }
            
            return closestPos;
        }
    }
}