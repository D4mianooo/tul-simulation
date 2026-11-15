using System;
using System.Collections.Generic;
using UnityEngine;

namespace Simulation
{
    /// Obiekt w odleg³oœci r od punktu zerowego (budynek, drzewo, cz³owiek...).
    /// Po czasie dojœcia danego zjawiska porównuje wartoœæ z progami i zapisuje stan.
    public class ImpactTarget : MonoBehaviour
    {
        public static readonly List<ImpactTarget> All = new List<ImpactTarget>();

        [SerializeField] private string label = "Building";

        public string Label => label;
        public EffectsAtDistance Effects { get; private set; }

        public bool ThermalArrived { get; private set; }
        public bool SeismicArrived { get; private set; }
        public bool BlastArrived { get; private set; }

        public BurnDegree Burns { get; private set; }
        public List<string> IgnitedMaterials { get; private set; } = new List<string>();
        public int BlastDamageLevel { get; private set; } = -1;   // indeks w BlastDamage.Levels
        public bool BuriedByEjecta => Effects.insideCrater;

        /// Wywo³ywane przy ka¿dej zmianie stanu (do podpiêcia efektów wizualnych).
        public event Action<ImpactTarget> StateChanged;

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        internal void Prepare(ImpactSimulation sim)
        {
            ThermalArrived = SeismicArrived = BlastArrived = false;
            Burns = BurnDegree.None;
            IgnitedMaterials.Clear();
            BlastDamageLevel = -1;

            Effects = EffectsAtDistance.Evaluate(sim.Results, sim.DistanceToGroundZeroM(transform.position));

            if (sim.LogEnabled)
                Debug.Log($"[{label}] {Effects}", this);
        }

        internal void Tick(ImpactSimulation sim, double time)
        {
            var res = sim.Results;
            var e = Effects;

            if (!ThermalArrived && res.hasFireball && time >= e.thermalArrivalS)
            {
                ThermalArrived = true;
                Burns = ThermalDamage.EvaluateBurns(e.thermalExposure, res.energyJ);
                IgnitedMaterials = ThermalDamage.IgnitedMaterials(e.thermalExposure, res.energyJ);
                Notify(sim, $"thermal: burns {Burns}, ignited [{string.Join(", ", IgnitedMaterials)}]");
            }

            if (!SeismicArrived && time >= e.seismicArrivalS)
            {
                SeismicArrived = true;
                Notify(sim, $"seismic: M_eff {e.effectiveMagnitude:F2}");
            }

            if (!BlastArrived && time >= e.blastArrivalS)
            {
                BlastArrived = true;
                BlastDamageLevel = BlastDamage.Evaluate(e.overpressurePa);
                string state = BlastDamageLevel >= 0
                    ? $"{BlastDamage.Levels[BlastDamageLevel].name} ({BlastDamage.Levels[BlastDamageLevel].description})"
                    : "no damage";
                Notify(sim, $"blast: {e.overpressurePa / 1000.0:F1} kPa, wind {e.windVelocity:F0} m/s -> {state}");
            }
        }

        private void Notify(ImpactSimulation sim, string message)
        {
            if (sim.LogEnabled)
                Debug.Log($"[{label}] t={sim.TimeSinceImpact:F2}s {message}", this);
            StateChanged?.Invoke(this);
        }
    }
}
