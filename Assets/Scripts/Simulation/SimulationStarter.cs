using UnityEngine;

namespace Simulation
{
    /// Jedna scena: po klikniêciu Start SimulationUIController wywo³uje SimulationRequested
    public class SimulationStarter : MonoBehaviour
    {
        [SerializeField] private SimulationUIController ui;
        [SerializeField] private ImpactSimulation simulation;

        private void Awake()
        {
            if (ui == null) ui = GetComponent<SimulationUIController>();
            if (simulation == null) simulation = FindFirstObjectByType<ImpactSimulation>();
        }

        private void OnEnable()
        {
            if (ui != null) ui.SimulationRequested += Run;
        }

        private void OnDisable()
        {
            if (ui != null) ui.SimulationRequested -= Run;
        }

        private void Run(ImpactParameters parameters)
        {
            if (simulation == null)
            {
                Debug.LogError("[SimulationStarter] No ImpactSimulation in the scene.");
                return;
            }

            simulation.Recompute(parameters);

            Vector3 point = ui.platformTransform != null ? ui.platformTransform.position : Vector3.zero;
            simulation.TriggerImpact(point);
        }
    }
}
