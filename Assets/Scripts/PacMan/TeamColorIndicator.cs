using System.Linq;
using PacMan.Local;
using Scripts.Map;
using UnityEngine;

namespace PacMan
{
    public class TeamColorIndicator : MonoBehaviour
    {
        public string redMaterialResource = "Materials/RedMaterial";
        public string blueMaterialResource = "Materials/BlueMaterial";
        public string lightRedMaterialResource = "Materials/LightRedMaterial";
        public string lightBlueMaterialResource = "Materials/LightBlueMaterial";
        public int materialIndex = -1;
        public GameObject taggedObject;

        private Material _lightRedMaterial;
        private Material _lightBlueMaterial;
        private Material _redMaterial;
        private Material _blueMaterial;

        private Renderer _rendererComponent;
        private PacManAgentManager _pacManAgentManager;

        private bool applied;

        public void Start()
        {
            _pacManAgentManager = taggedObject.GetComponent<PacManAgentManager>();
            _redMaterial = FileUtils.LoadMaterialFromFile(redMaterialResource);
            _blueMaterial = FileUtils.LoadMaterialFromFile(blueMaterialResource);
            _lightRedMaterial = FileUtils.LoadMaterialFromFile(lightRedMaterialResource);
            _lightBlueMaterial = FileUtils.LoadMaterialFromFile(lightBlueMaterialResource);
            _rendererComponent = gameObject.GetComponent<Renderer>();

            SetMaterial();
        }

        public void SetMaterial()
        {
            var checkTeam = TeamAssignmentUtil.CheckTeam(taggedObject != null ? taggedObject : gameObject);

            var materialToSet = checkTeam == Team.Blue ? _blueMaterial : _redMaterial;
            if (_pacManAgentManager != null && _pacManAgentManager.IsScared() && _pacManAgentManager.IsGhost())
            {
                materialToSet = checkTeam == Team.Blue ? _lightBlueMaterial : _lightRedMaterial;
            }

            if (materialIndex >= 0)
            {
                var materials = _rendererComponent.sharedMaterials;
                materials[materialIndex] = materialToSet;
                _rendererComponent.SetSharedMaterials(materials.ToList());
            }
            else
            {
                _rendererComponent.sharedMaterial = materialToSet;
            }
        }

        public void FixedUpdate()
        {
            SetMaterial();
        }
    }
}