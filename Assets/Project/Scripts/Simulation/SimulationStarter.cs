using Simulation.Core;
using Simulation.UI;
using UnityEngine;

namespace Simulation {
    /// <summary>
    /// Starts an impact simulation when <see cref="SimulationUIController.SimulationRequested"/> is raised.
    /// </summary>
    public class SimulationStarter : MonoBehaviour {
        [SerializeField]
        private SimulationUIController _ui;

        [SerializeField]
        private ImpactSimulation _simulation;

        [SerializeField]
        private Target _target;

        private void Awake() {
            if (!_ui) _ui = GetComponent<SimulationUIController>();
            if (!_simulation) _simulation = FindAnyObjectByType<ImpactSimulation>();
            if (!_target) _target = FindFirstObjectByType<Target>(FindObjectsInactive.Include);
        }

        private void OnEnable() {
            if (_ui) _ui.SimulationRequested += Run;
        }

        private void OnDisable() {
            if (_ui) _ui.SimulationRequested -= Run;
        }

        private void Run(ImpactParameters parameters) {
            if (!_simulation) {
                Debug.LogError("[SimulationStarter] No ImpactSimulation in the scene.");
                return;
            }

            _simulation.Recompute(parameters);

            Vector3 point = _ui.PlatformTransform ? _ui.PlatformTransform.position : Vector3.zero;
            if (_target) point = _target.transform.position;
            _simulation.TriggerImpact(point);
        }
    }
}
