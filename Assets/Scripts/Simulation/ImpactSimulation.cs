using System;
using System.Globalization;
using UnityEngine;

namespace Simulation
{
    /// Warstwa logiki symulacji: liczy wyniki z parametrów z menu, prowadzi czas
    public class ImpactSimulation : MonoBehaviour
    {
        public static ImpactSimulation Instance { get; private set; }

        [Header("Input")]
        [Tooltip("Bierz parametry z ekranu startowego (SimulationSettings.Current). " +
                 "Wy³¹cz, ¿eby testowaæ scenê bez menu.")]
        [SerializeField] private bool useMenuSettings = true;
        [SerializeField]
        private ImpactParameters testParameters =
            new ImpactParameters(130f, 3000f, 19000f, 45f, 2500f); // przypadek testowy ze sprawozdania

        [Header("Scene")]
        [Tooltip("Ile metrów rzeczywistych przypada na 1 jednostkê Unity.")]
        [SerializeField] private float metersPerUnit = 100f;
        [SerializeField] private Transform groundZeroMarker;
        [SerializeField] private bool impactOnStart = true;

        [Header("Time")]
        [Tooltip("1 = czas rzeczywisty. Fala uderzeniowa idzie ~kilkadziesi¹t sekund na kilka km.")]
        [SerializeField] private float timeScale = 1f;

        [Header("Debug")]
        [SerializeField] private bool logReport = true;
        [SerializeField] private float[] probeDistancesM = { 1000f, 2000f, 5000f, 10000f, 20000f, 50000f, 100000f };

        public ImpactParameters Parameters { get; private set; }
        public ImpactResults Results { get; private set; }
        public bool HasImpacted { get; private set; }
        public double TimeSinceImpact { get; private set; }
        public Vector3 GroundZero { get; private set; }
        public float MetersPerUnit => metersPerUnit;
        public bool LogEnabled => logReport;

        public event Action<ImpactResults> ResultsComputed;
        public event Action<Vector3, ImpactResults> Impacted;
        public event Action<double> Ticked;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            Recompute(useMenuSettings ? SimulationSettings.Current : testParameters);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            if (impactOnStart)
                TriggerImpact(groundZeroMarker != null ? groundZeroMarker.position : transform.position);
        }

        private void Update()
        {
            if (!HasImpacted) return;

            TimeSinceImpact += Time.deltaTime * timeScale;
            foreach (var target in ImpactTarget.All)
                target.Tick(this, TimeSinceImpact);

            Ticked?.Invoke(TimeSinceImpact);
        }

        /// Kroki 1-3: m, E, z_br, v_f, krater.
        public void Recompute(ImpactParameters parameters)
        {
            Parameters = parameters;
            Results = ImpactResults.Compute(parameters);

            if (logReport)
                LogReport();

            ResultsComputed?.Invoke(Results);
        }

        /// Uderzenie w danym punkcie œwiata — start zegara, kroki 4-5 dla wszystkich celów.
        public void TriggerImpact(Vector3 worldPoint)
        {
            GroundZero = worldPoint;
            HasImpacted = true;
            TimeSinceImpact = 0.0;

            foreach (var target in ImpactTarget.All)
                target.Prepare(this);

            Impacted?.Invoke(worldPoint, Results);
        }

        public void ResetSimulation()
        {
            HasImpacted = false;
            TimeSinceImpact = 0.0;
        }

        public double DistanceToGroundZeroM(Vector3 worldPosition)
        {
            Vector2 a = new Vector2(worldPosition.x, worldPosition.z);
            Vector2 b = new Vector2(GroundZero.x, GroundZero.z);
            return Vector2.Distance(a, b) * metersPerUnit;
        }

        public float MetersToUnits(double meters) => (float)(meters / metersPerUnit);

        private void LogReport()
        {
            var res = Results;
            Debug.Log(res.ToString());

            var sb = new System.Text.StringBuilder("=== EFFECTS VS DISTANCE ===\n");
            foreach (float r in probeDistancesM)
            {
                var e = EffectsAtDistance.Evaluate(res, r);
                int lvl = BlastDamage.Evaluate(e.overpressurePa);
                string blast = lvl >= 0 ? BlastDamage.Levels[lvl].name : "none";
                var burns = ThermalDamage.EvaluateBurns(e.thermalExposure, res.energyJ);
                sb.AppendLine(e + $" | blast: {blast}, burns: {burns}");
            }
            Debug.Log(sb.ToString());
        }

        [ContextMenu("Log reference test case (Impact: Earth!)")]
        private void LogReferenceCase()
        {
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
