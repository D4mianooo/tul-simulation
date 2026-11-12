using UnityEngine;
using UnityEngine.SceneManagement;

namespace Simulation
{
    public class MenuSceneLoader : MonoBehaviour
    {
        [SerializeField] private SimulationUIController ui;
        [SerializeField] private string simulationSceneName = "SimulationScene";

        private void Awake()
        {
            if (ui == null) ui = GetComponent<SimulationUIController>();
        }

        private void OnEnable()
        {
            if (ui != null) ui.SimulationRequested += HandleSimulationRequested;
        }

        private void OnDisable()
        {
            if (ui != null) ui.SimulationRequested -= HandleSimulationRequested;
        }

        private void HandleSimulationRequested(ImpactParameters parameters)
        {
            if (!Application.CanStreamedLevelBeLoaded(simulationSceneName))
            {
                Debug.LogError($"Scene '{simulationSceneName}' is not in the build scene list " +
                               "(File > Build Profiles > Scene List).");
                return;
            }

            SceneManager.LoadScene(simulationSceneName);
        }
    }
}
