using Simulation.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Simulation.Core {
    /// <summary>
    /// Loads the simulation scene when <see cref="SimulationUIController.SimulationRequested"/> fires.
    /// </summary>
    public class MenuSceneLoader : MonoBehaviour {
        [SerializeField]
        private SimulationUIController _ui;

        [SerializeField]
        private string _simulationSceneName = "SimulationScene";

        private void Awake() {
            if (!_ui) _ui = GetComponent<SimulationUIController>();
        }

        private void OnEnable() {
            if (_ui) _ui.SimulationRequested += HandleSimulationRequested;
        }

        private void OnDisable() {
            if (_ui != null) _ui.SimulationRequested -= HandleSimulationRequested;
        }

        private void HandleSimulationRequested(ImpactParameters parameters) {
            if (!Application.CanStreamedLevelBeLoaded(_simulationSceneName)) {
                Debug.LogError($"Scene '{_simulationSceneName}' is not in the build scene list " +
                               "(File > Build Profiles > Scene List).");
                return;
            }

            SceneManager.LoadScene(_simulationSceneName);
        }
    }
}
