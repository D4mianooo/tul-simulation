using System;
using System.Globalization;
using Simulation.Core;
using UnityEngine;

namespace Simulation {
    /// <summary>
    /// Central controller for computing impact physics, advancing simulation time, and notifying listeners.
    /// </summary>
    public class ImpactSimulation : Singleton<ImpactSimulation> {
        [Header("Input")]
        [Tooltip("Bierz parametry z ekranu startowego (SimulationSettings.Current). " +
                 "Wyłącz, żeby testować scenę bez menu.")]
        [SerializeField]
        private bool _useMenuSettings = true;

        [SerializeField]
        private ImpactParameters _testParameters =
            new ImpactParameters(130f, 3000f, 19000f, 45f, 2500f);

        [Header("Scene")]
        [Tooltip("Ile metrów rzeczywistych przypada na 1 jednostkę Unity.")]
        [SerializeField]
        private float _metersPerUnit = 100f;

        [SerializeField]
        private Transform _groundZeroMarker;

        [SerializeField]
        private bool _impactOnStart = true;

        [Header("Time")]
        [Tooltip("1 = czas rzeczywisty. Fala uderzeniowa idzie ~kilkadziesiąt sekund na kilka km.")]
        [SerializeField]
        private float _timeScale = 1f;

        [Header("Debug")]
        [SerializeField]
        private bool _logReport = true;

        [SerializeField]
        private float[] _probeDistancesM = { 1000f, 2000f, 5000f, 10000f, 20000f, 50000f, 100000f };

        /// <summary>
        /// Gets the parameters used for the current simulation.
        /// </summary>
        public ImpactParameters Parameters { get; private set; }

        /// <summary>
        /// Gets the computed results for the current simulation.
        /// </summary>
        public ImpactResults Results { get; private set; }

        /// <summary>
        /// Gets a value indicating whether an impact has been triggered.
        /// </summary>
        public bool HasImpacted { get; private set; }

        /// <summary>
        /// Gets the elapsed simulation time since impact in seconds.
        /// </summary>
        public double TimeSinceImpact { get; private set; }

        /// <summary>
        /// Gets the world-space ground zero position of the current impact.
        /// </summary>
        public Vector3 GroundZero { get; private set; }

        /// <summary>
        /// Gets the scale factor converting Unity world units to meters.
        /// </summary>
        public float MetersPerUnit => _metersPerUnit;

        /// <summary>
        /// Gets or sets whether diagnostic impact reports are logged to the Console.
        /// </summary>
        public bool LogEnabled { get => _logReport; set => _logReport = value; }

        /// <summary>
        /// Gets or sets the simulation time multiplier.
        /// </summary>
        public float TimeScale { get => _timeScale; set => _timeScale = Mathf.Max(0f, value); }

        /// <summary>
        /// Occurs after <see cref="Recompute"/> finishes calculating <see cref="Results"/>.
        /// </summary>
        public event Action<ImpactResults> ResultsComputed;

        /// <summary>
        /// Occurs when <see cref="TriggerImpact"/> initiates an impact at a world point.
        /// </summary>
        public event Action<Vector3, ImpactResults> Impacted;

        /// <summary>
        /// Occurs each frame while an impact is active with the current <see cref="TimeSinceImpact"/>.
        /// </summary>
        public event Action<double> Ticked;

        protected override void Awake() {
            base.Awake();
            if (Instance != this) {
                return;
            }

            Recompute(_useMenuSettings ? SimulationSettings.Current : _testParameters);
        }

        private void Start() {
            if (_impactOnStart)
                TriggerImpact(_groundZeroMarker != null ? _groundZeroMarker.position : transform.position);
        }

        private void Update() {
            if (!HasImpacted) return;

            TimeSinceImpact += Time.deltaTime * _timeScale;
            foreach (var target in ImpactTarget.All)
                target.Tick(this, TimeSinceImpact);

            Ticked?.Invoke(TimeSinceImpact);
        }

        /// <summary>
        /// Recomputes impact results for the given <paramref name="parameters"/>.
        /// </summary>
        /// <param name="parameters">Input impactor and target parameters.</param>
        public void Recompute(ImpactParameters parameters) {
            Parameters = parameters;
            Results = ImpactResults.Compute(parameters);

            if (_logReport)
                LogReport();

            ResultsComputed?.Invoke(Results);
        }

        /// <summary>
        /// Triggers an impact at <paramref name="worldPoint"/> and resets the simulation clock.
        /// </summary>
        /// <param name="worldPoint">World-space ground zero position.</param>
        public void TriggerImpact(Vector3 worldPoint) {
            GroundZero = worldPoint;
            HasImpacted = true;
            TimeSinceImpact = 0.0;

            foreach (var target in ImpactTarget.All)
                target.Prepare(this);

            Impacted?.Invoke(worldPoint, Results);
        }

        /// <summary>
        /// Resets the active impact state and elapsed time.
        /// </summary>
        public void ResetSimulation() {
            HasImpacted = false;
            TimeSinceImpact = 0.0;
        }

        /// <summary>
        /// Computes the horizontal distance in meters from <paramref name="worldPosition"/> to <see cref="GroundZero"/>.
        /// </summary>
        /// <param name="worldPosition">Target position in world space.</param>
        /// <returns>Distance to ground zero in meters.</returns>
        public double DistanceToGroundZeroM(Vector3 worldPosition) {
            Vector2 a = new Vector2(worldPosition.x, worldPosition.z);
            Vector2 b = new Vector2(GroundZero.x, GroundZero.z);
            return Vector2.Distance(a, b) * _metersPerUnit;
        }

        /// <summary>
        /// Converts a distance in meters to Unity world units.
        /// </summary>
        /// <param name="meters">Distance in meters.</param>
        /// <returns>Equivalent distance in Unity units.</returns>
        public float MetersToUnits(double meters) => (float)(meters / _metersPerUnit);

        private void LogReport() {
            var res = Results;
            Debug.Log(res.ToString());

            var sb = new System.Text.StringBuilder("=== EFFECTS VS DISTANCE ===\n");
            foreach (float r in _probeDistancesM) {
                var e = EffectsAtDistance.Evaluate(res, r);
                int lvl = BlastDamage.Evaluate(e.overpressurePa);
                string blast = lvl >= 0 ? BlastDamage.Levels[lvl].name : "none";
                var burns = ThermalDamage.EvaluateBurns(e.thermalExposure, res.energyJ);
                sb.AppendLine(e + $" | blast: {blast}, burns: {burns}");
            }
            Debug.Log(sb.ToString());
        }

        [ContextMenu("Log reference test case (Impact: Earth!)")]
        private void LogReferenceCase() {
            var ci = CultureInfo.InvariantCulture;
            var res = ImpactResults.Compute(new ImpactParameters(130f, 3000f, 19000f, 45f, 2500f));
            Debug.Log(res.ToString());
            Debug.Log(string.Format(ci,
                "Impact: Earth! reference -> z_br 55800 m | D_tc 1860 m | d_tc 657 m | D_fr 2320 m | simple\n" +
                "Ours                     -> z_br {0:F0} m | D_tc {1:F0} m | d_tc {2:F0} m | D_fr {3:F0} m | {4}",
                res.breakupAltitudeM, res.transientDiameterM, res.transientDepthM, res.finalDiameterM,
                res.isComplexCrater ? "complex" : "simple"));
        }
    }
}
