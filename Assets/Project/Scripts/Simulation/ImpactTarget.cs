using System;
using System.Collections.Generic;
using UnityEngine;

namespace Simulation {
    /// <summary>
    /// Tracks arrival and damage state of impact effects for a specific scene object.
    /// </summary>
    public class ImpactTarget : MonoBehaviour {
        /// <summary>
        /// Active instances of <see cref="ImpactTarget"/> in the scene.
        /// </summary>
        public static readonly List<ImpactTarget> All = new List<ImpactTarget>();

        [SerializeField]
        private string _label = "Building";

        /// <summary>
        /// Gets the display label for this target.
        /// </summary>
        public string Label => _label;

        /// <summary>
        /// Gets the evaluated impact effects at this target's distance.
        /// </summary>
        public EffectsAtDistance Effects { get; private set; }

        /// <summary>
        /// Gets a value indicating whether thermal radiation has arrived.
        /// </summary>
        public bool ThermalArrived { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the seismic wave has arrived.
        /// </summary>
        public bool SeismicArrived { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the blast wave has arrived.
        /// </summary>
        public bool BlastArrived { get; private set; }

        /// <summary>
        /// Gets the evaluated burn degree after thermal arrival.
        /// </summary>
        public BurnDegree Burns { get; private set; }

        /// <summary>
        /// Gets the list of materials ignited by thermal radiation.
        /// </summary>
        public List<string> IgnitedMaterials { get; private set; } = new List<string>();

        /// <summary>
        /// Gets the evaluated blast damage level index, or -1 if undamaged.
        /// </summary>
        public int BlastDamageLevel { get; private set; } = -1;

        /// <summary>
        /// Gets a value indicating whether this target lies inside the crater.
        /// </summary>
        public bool BuriedByEjecta => Effects.insideCrater;

        /// <summary>
        /// Occurs when any effect arrives and updates this target's state.
        /// </summary>
        public event Action<ImpactTarget> StateChanged;

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        internal void Prepare(ImpactSimulation sim) {
            ThermalArrived = SeismicArrived = BlastArrived = false;
            Burns = BurnDegree.None;
            IgnitedMaterials.Clear();
            BlastDamageLevel = -1;

            Effects = EffectsAtDistance.Evaluate(sim.Results, sim.DistanceToGroundZeroM(transform.position));

            if (sim.LogEnabled)
                Debug.Log($"[{_label}] {Effects}", this);
        }

        internal void Tick(ImpactSimulation sim, double time) {
            var res = sim.Results;
            var e = Effects;

            if (!ThermalArrived && res.hasFireball && time >= e.thermalArrivalS) {
                ThermalArrived = true;
                Burns = ThermalDamage.EvaluateBurns(e.thermalExposure, res.energyJ);
                IgnitedMaterials = ThermalDamage.IgnitedMaterials(e.thermalExposure, res.energyJ);
                Notify(sim, $"thermal: burns {Burns}, ignited [{string.Join(", ", IgnitedMaterials)}]");
            }

            if (!SeismicArrived && time >= e.seismicArrivalS) {
                SeismicArrived = true;
                Notify(sim, $"seismic: M_eff {e.effectiveMagnitude:F2}");
            }

            if (!BlastArrived && time >= e.blastArrivalS) {
                BlastArrived = true;
                BlastDamageLevel = BlastDamage.Evaluate(e.overpressurePa);
                string state = BlastDamageLevel >= 0
                    ? $"{BlastDamage.Levels[BlastDamageLevel].name} ({BlastDamage.Levels[BlastDamageLevel].description})"
                    : "no damage";
                Notify(sim, $"blast: {e.overpressurePa / 1000.0:F1} kPa, wind {e.windVelocity:F0} m/s -> {state}");
            }
        }

        private void Notify(ImpactSimulation sim, string message) {
            if (sim.LogEnabled)
                Debug.Log($"[{_label}] t={sim.TimeSinceImpact:F2}s {message}", this);
            StateChanged?.Invoke(this);
        }
    }
}
