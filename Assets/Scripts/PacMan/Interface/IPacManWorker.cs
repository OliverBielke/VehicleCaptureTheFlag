using UnityEngine;

namespace PacMan.Interface
{
    public interface IPacManWorkerInterface
    {
        public GameObject CreateEdible(GameObject parent, GameObject prefab, Vector3 position);
        public GameObject CreateAgent(GameObject prefab, GameObject parent, Vector3 position, string team_tag);
        public void RemoveObject(GameObject toRemove);
        void ResetAgent(GameObject agent);
    }
}