using UnityEngine;
using System.Collections.Generic;
using PacMan.Agent.PathFollowing;


namespace PacMan.Agent.PathFinding
{
    public class GoalChoosing
    {
        private readonly List<GameObject> _goals;
        private readonly List<GameObject> _teamVehicles;
        private readonly Transform _initialState;
        
        public GoalChoosing(List<GameObject> goals, List<GameObject> teamVehicles, Transform initialState)
        {
            _goals = goals;
            _teamVehicles = teamVehicles;
            _initialState = initialState;
        }

        /// <summary>
        /// Get this vehicle's goal. 
        /// </summary>
        /// <returns>The goal position. </returns>
        public Vector3 GetGoalPosition()
        {
            //If only one goal
            if (_goals.Count == 1) return _goals[0].transform.position;
            
            var goalPosition = _initialState.position;
            var bestDist = float.MaxValue;
            
            foreach (var goal in _goals)
            {
                var dist = (goal.transform.position - _initialState.position).sqrMagnitude;
                
                if (dist >= bestDist)//If not closest goal
                {
                    continue;
                }
                
                bestDist = dist;
                goalPosition = goal.transform.position;
            }
            
            return goalPosition;
        }
    }
}