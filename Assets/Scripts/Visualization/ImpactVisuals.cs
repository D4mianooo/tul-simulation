using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Simulation
{
    /// Wizualizacja 3D skutków uderzenia, zsynchronizowana z zegarem symulacji:
    /// - wybuch (prefab, np. Cartoon FX) przeskalowany do promienia kuli ognia R_fb,
    /// - pierścienie stref zniszczeń, które pojawiają się dokładnie wtedy, gdy fala dociera (t_bw),
    /// - rozchodzący się front fali uderzeniowej i szybszy front fali sejsmicznej (5 km/s),
    /// - drgania kamery w chwili dotarcia wstrząsu do kamery, z siłą zależną od M_eff.
    [DefaultExecutionOrder(-100)]
    public class ImpactVisuals : MonoBehaviour
    {
        [SerializeField] private ImpactSimulation simulation;

        [Header("Wybuch")]
        [Tooltip("Prefab efektu wybuchu, np. z Cartoon FX Remaster Free (CFXR ... Explosion). " +
                 "Gdy pusty, używany jest prosty wbudowany efekt kuli ognia.")]
        [SerializeField] private GameObject explosionPrefab;
        [Tooltip("Przybliżony promień efektu przy skali 1 (w jednostkach Unity).")]
        [SerializeField] private float prefabRadiusAtScale1 = 2f;
        [SerializeField] private float explosionScaleMultiplier = 1f;
        [SerializeField] private Vector2 explosionScaleClamp = new Vector2(1f, 60f);
        [SerializeField] private float explosionLifetime = 12f;
        [SerializeField] private bool flashLight = true;

        [Header("Strefy i fronty fal")]
        [SerializeField] private bool showDamageRings = true;
        [SerializeField] private bool showRingLabels = true;
        [SerializeField] private bool showThermalRing = true;
        [SerializeField] private bool showShockFront = true;
        [SerializeField] private bool showSeismicFront = true;
        [Tooltip("Pierścienie większe niż ten promień (w jednostkach Unity) są pomijane.")]
        [SerializeField] private float maxRingRadiusUnits = 3000f;
        [SerializeField] private int ringSegments = 160;
        [SerializeField] private float groundOffset = 0.15f;

        [Header("Kamera")]
        [SerializeField] private bool cameraShake = true;
        [SerializeField] private float maxShakeAmplitude = 1.2f;
        [SerializeField] private float shakeDuration = 2.5f;

        private static readonly CultureInfo CI = CultureInfo.InvariantCulture;

        private Transform fxRoot;
        private Material lineMaterial;
        private BlastFrontTable blastTable;
        private ImpactResults results;
        private Vector3 groundZero;

        private struct TimedRing
        {
            public double appearTime;
            public GameObject go;
        }
        private readonly List<TimedRing> rings = new List<TimedRing>();

        private LineRenderer shockFront;
        private LineRenderer seismicFront;
        private double frontsMaxRadiusM;

        private GameObject fallbackFireball;
        private Light flash;
        private float fireballRadiusUnits;

        private Camera cam;
        private double shakeStartTime = -1.0;
        private float shakeAmplitude;
        private bool shakeStarted;
        private Vector3 appliedShake;

        private void Awake()
        {
            if (simulation == null) simulation = FindFirstObjectByType<ImpactSimulation>();
            lineMaterial = CreateLineMaterial();
            EnsureGroundMaterial();
            CameraController.EnsureOnMainCamera();   // sterowanie kamerą (PPM + WSAD), jeśli scena go nie ma
        }

        /// Teren bez materiału Unity rysuje na różowo. Dajemy mu wtedy matowy, ciemnozielony materiał URP.
        public static void EnsureGroundMaterial()
        {
            var terrain = FindFirstObjectByType<TerrainGenerator>();
            if (terrain == null) return;
            var r = terrain.GetComponent<MeshRenderer>();
            if (r == null || r.sharedMaterial != null) return;
            r.sharedMaterial = CreateGroundMaterial();
        }

        public static Material CreateGroundMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var m = new Material(shader) { name = "Ground" };
            var c = new Color(0.30f, 0.36f, 0.27f);
            m.SetColor("_BaseColor", c);
            m.color = c;
            m.SetFloat("_Smoothness", 0.05f);
            return m;
        }

        private void OnEnable()
        {
            if (simulation != null) simulation.Impacted += OnImpacted;
        }

        private void OnDisable()
        {
            if (simulation != null) simulation.Impacted -= OnImpacted;
            RemoveShake();
        }

        private void Start()
        {
            if (simulation != null && simulation.HasImpacted)
                OnImpacted(simulation.GroundZero, simulation.Results);
        }

        // ----------------------------------------------------------------- start

        private void OnImpacted(Vector3 point, ImpactResults res)
        {
            results = res;
            groundZero = point;
            Clear();

            fxRoot = new GameObject("ImpactFX").transform;
            fxRoot.position = point;

            double maxRingM = ImpactChartData.RadiusForOverpressure(res, BlastDamage.Levels[0].thresholdPa);
            frontsMaxRadiusM = System.Math.Min(maxRingM * 1.15, simulation.MetersPerUnit * maxRingRadiusUnits);
            blastTable = new BlastFrontTable(res, frontsMaxRadiusM);

            SpawnExplosion();
            if (showDamageRings) BuildDamageRings();
            if (showShockFront) shockFront = CreateRing("Shock front", new Color(0.85f, 0.95f, 1f, 0.9f), 1f);
            if (showSeismicFront) seismicFront = CreateRing("Seismic front", new Color(0.73f, 0.41f, 0.78f, 0.7f), 0.5f);

            PrepareShake();
            ExtendCameraRange();
        }

        /// Najdalsze strefy (np. wybite szyby) leżą setki km od krateru; kamera musi je jeszcze rysować.
        private void ExtendCameraRange()
        {
            var c = Camera.main;
            if (c == null) return;
            float need = simulation.MetersToUnits(frontsMaxRadiusM) * 2.5f;
            if (c.farClipPlane < need) c.farClipPlane = Mathf.Min(need, 50000f);
        }

        private void SpawnExplosion()
        {
            fireballRadiusUnits = results.hasFireball
                ? simulation.MetersToUnits(results.fireballRadiusM)
                : simulation.MetersToUnits(System.Math.Max(results.finalDiameterM, 200.0));

            if (explosionPrefab != null)
            {
                var fx = Instantiate(explosionPrefab, groundZero, Quaternion.identity, fxRoot);
                float scale = Mathf.Clamp(fireballRadiusUnits / Mathf.Max(0.01f, prefabRadiusAtScale1) * explosionScaleMultiplier,
                    explosionScaleClamp.x, explosionScaleClamp.y);
                fx.transform.localScale = Vector3.one * scale;

                // Żeby skala obiektu działała na wszystkie cząsteczki (także w podsystemach).
                foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = ps.main;
                    main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                }
                Destroy(fx, explosionLifetime);
            }
            else
            {
                fallbackFireball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                fallbackFireball.name = "Fireball (fallback)";
                Destroy(fallbackFireball.GetComponent<Collider>());
                fallbackFireball.transform.SetParent(fxRoot, false);
                var r = fallbackFireball.GetComponent<MeshRenderer>();
                r.sharedMaterial = new Material(lineMaterial) { color = new Color(1f, 0.75f, 0.3f, 0.9f) };
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                fallbackFireball.transform.localScale = Vector3.zero;
            }

            if (flashLight)
            {
                var lightGo = new GameObject("Impact flash");
                lightGo.transform.SetParent(fxRoot, false);
                lightGo.transform.localPosition = Vector3.up * Mathf.Max(1f, fireballRadiusUnits);
                flash = lightGo.AddComponent<Light>();
                flash.type = LightType.Point;
                flash.color = new Color(1f, 0.8f, 0.55f);
                flash.range = Mathf.Max(10f, fireballRadiusUnits * 8f);
                flash.intensity = 0f;
            }
        }

        private void BuildDamageRings()
        {
            var levels = BlastDamage.Levels;

            // Krawędź krateru: widoczna od razu.
            AddTimedRing("Krawędź krateru", results.CraterRimRadiusM, 0.0, new Color(0.85f, 0.85f, 0.85f, 1f),
                "Krater " + ImpactChartData.FormatDistance(results.finalDiameterM));

            // Strefa oparzeń 3 stopnia: pojawia się w chwili maksimum promieniowania.
            if (showThermalRing && results.hasFireball)
            {
                double phi3 = ImpactMath.IgnitionThreshold(ThermalDamage.ThirdDegreeAt1MtMJ, results.energyJ);
                double r3 = ImpactChartData.RadiusForThermal(results, phi3);
                AddTimedRing("Oparzenia 3 st.", r3, results.timeOfMaxRadiationS, new Color(1f, 0.55f, 0.1f, 0.9f),
                    "Oparzenia 3° " + ImpactChartData.FormatDistance(r3));
            }

            // Progi fali uderzeniowej: każdy pierścień pojawia się, gdy front fali do niego dotrze.
            for (int i = 0; i < levels.Length; i++)
            {
                if (i + 1 < levels.Length && System.Math.Abs(levels[i + 1].thresholdPa - levels[i].thresholdPa) < 1.0)
                    continue; // ten sam próg co następny poziom: rysujemy raz
                double r = ImpactChartData.RadiusForOverpressure(results, levels[i].thresholdPa);
                if (r <= results.CraterRimRadiusM) continue;

                string label = string.Format(CI, "{0} {1:0.#} kPa", ImpactChartData.BlastLevelNamesPl[i], levels[i].thresholdPa / 1000.0);
                AddTimedRing(label, r, blastTable.TimeAt(r), ImpactCharts.DamageColor(i, levels.Length), label);
            }
        }

        private void AddTimedRing(string name, double radiusM, double appearTime, Color color, string label)
        {
            float radiusUnits = simulation.MetersToUnits(radiusM);
            if (radiusUnits <= 0.01f || radiusUnits > maxRingRadiusUnits) return;

            var lr = CreateRing(name, color, Mathf.Max(0.2f, radiusUnits * 0.008f));
            SetRingRadius(lr, radiusUnits);

            if (showRingLabels && !string.IsNullOrEmpty(label))
                CreateLabel(lr.transform, label, color, radiusUnits);

            lr.gameObject.SetActive(appearTime <= 0.0);
            rings.Add(new TimedRing { appearTime = appearTime, go = lr.gameObject });
        }

        // ----------------------------------------------------------------- klatka

        private void Update()
        {
            RemoveShake();
            if (simulation == null || !simulation.HasImpacted || fxRoot == null) return;

            double t = simulation.TimeSinceImpact;

            foreach (var ring in rings)
                if (ring.go != null && !ring.go.activeSelf && t >= ring.appearTime)
                    ring.go.SetActive(true);

            UpdateFronts(t);
            UpdateFireball((float)t);
        }

        private void LateUpdate()
        {
            if (simulation == null || !simulation.HasImpacted || !cameraShake) return;
            ApplyShake(simulation.TimeSinceImpact);
        }

        private void UpdateFronts(double t)
        {
            if (shockFront != null)
            {
                double r = blastTable.RadiusAt(t);
                bool visible = r > results.CraterRimRadiusM && r < frontsMaxRadiusM * 0.999;
                shockFront.gameObject.SetActive(visible);
                if (visible)
                {
                    float rUnits = simulation.MetersToUnits(r);
                    SetRingRadius(shockFront, rUnits);
                    shockFront.widthMultiplier = Mathf.Max(0.2f, rUnits * 0.012f);
                }
            }

            if (seismicFront != null)
            {
                double r = ImpactMath.SeismicWaveSpeedKmS * 1000.0 * t;
                bool visible = r > results.CraterRimRadiusM && r < frontsMaxRadiusM;
                seismicFront.gameObject.SetActive(visible);
                if (visible)
                {
                    float rUnits = simulation.MetersToUnits(r);
                    SetRingRadius(seismicFront, rUnits);
                    seismicFront.widthMultiplier = Mathf.Max(0.15f, rUnits * 0.006f);
                }
            }
        }

        /// Kula ognia (zapasowa) i błysk: rośnie do R_fb, potem gaśnie przez czas naświetlania t_irr.
        private void UpdateFireball(float t)
        {
            float growTime = 0.6f;
            float fadeTime = Mathf.Clamp((float)results.irradiationDurationS, 2f, 10f);
            float grow = Mathf.Clamp01(t / growTime);
            float fade = 1f - Mathf.Clamp01((t - growTime) / fadeTime);

            if (fallbackFireball != null)
            {
                float d = fireballRadiusUnits * 2f * Mathf.Lerp(0.2f, 1f, grow);
                fallbackFireball.transform.localScale = Vector3.one * d;
                fallbackFireball.transform.localPosition = Vector3.up * d * 0.35f;
                var mat = fallbackFireball.GetComponent<MeshRenderer>().sharedMaterial;
                mat.color = Color.Lerp(new Color(0.3f, 0.1f, 0.05f, 0f), new Color(1f, 0.8f, 0.35f, 0.9f), fade);
                if (fade <= 0f) fallbackFireball.SetActive(false);
            }

            if (flash != null)
                flash.intensity = 40f * grow * fade * fade;
        }

        // ----------------------------------------------------------------- drgania kamery

        private void PrepareShake()
        {
            shakeStarted = false;
            shakeStartTime = -1.0;
            cam = Camera.main;
            if (!cameraShake || cam == null) return;

            double camDistM = simulation.DistanceToGroundZeroM(cam.transform.position);
            double rKm = System.Math.Max(camDistM, results.CraterRimRadiusM) / 1000.0;
            double mEff = ImpactMath.EffectiveMagnitude(results.magnitude, rKm);
            shakeStartTime = ImpactMath.SeismicArrivalTime(rKm);
            // M_eff ~ 3 ledwo odczuwalne, ~9 maksymalne drgania.
            shakeAmplitude = maxShakeAmplitude * Mathf.Clamp01(((float)mEff - 3f) / 6f);
        }

        private void ApplyShake(double t)
        {
            if (cam == null || shakeStartTime < 0.0 || shakeAmplitude <= 0f) return;

            double local = t - shakeStartTime;
            if (local < 0.0 || local > shakeDuration) return;
            shakeStarted = true;

            float decay = 1f - (float)(local / shakeDuration);
            float time = Time.time * 25f;
            Vector3 offset = new Vector3(
                Mathf.PerlinNoise(time, 0.1f) - 0.5f,
                Mathf.PerlinNoise(0.3f, time) - 0.5f,
                Mathf.PerlinNoise(time, 0.7f) - 0.5f) * (2f * shakeAmplitude * decay * decay);

            cam.transform.position += offset;
            appliedShake = offset;
        }

        /// Cofamy przesunięcie z poprzedniej klatki, zanim CameraController zrobi swoje.
        private void RemoveShake()
        {
            if (cam != null && appliedShake != Vector3.zero)
                cam.transform.position -= appliedShake;
            appliedShake = Vector3.zero;
        }

        // ----------------------------------------------------------------- narzędzia

        private LineRenderer CreateRing(string name, Color color, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(fxRoot, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.loop = true;
            lr.positionCount = ringSegments;
            lr.sharedMaterial = lineMaterial;
            lr.startColor = color;
            lr.endColor = color;
            lr.widthMultiplier = width;
            lr.numCornerVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.alignment = LineAlignment.TransformZ;
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // linia leży płasko na ziemi
            return lr;
        }

        private void SetRingRadius(LineRenderer lr, float radiusUnits)
        {
            for (int i = 0; i < ringSegments; i++)
            {
                float a = i * Mathf.PI * 2f / ringSegments;
                float x = groundZero.x + Mathf.Cos(a) * radiusUnits;
                float z = groundZero.z + Mathf.Sin(a) * radiusUnits;
                lr.SetPosition(i, new Vector3(x, GroundHeight(x, z), z));
            }
        }

        private float GroundHeight(float x, float z)
        {
            var origin = new Vector3(x, groundZero.y + 5000f, z);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 20000f, ~0, QueryTriggerInteraction.Ignore))
                return hit.point.y + groundOffset;
            return groundZero.y + groundOffset;
        }

        private void CreateLabel(Transform parent, string text, Color color, float radiusUnits)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            float a = Mathf.PI * 0.25f;
            float x = groundZero.x + Mathf.Cos(a) * radiusUnits;
            float z = groundZero.z + Mathf.Sin(a) * radiusUnits;
            float size = Mathf.Clamp(radiusUnits * 0.04f, 0.4f, 60f);
            go.transform.position = new Vector3(x, GroundHeight(x, z) + size * 0.6f, z);

            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.fontSize = 64;
            tm.characterSize = size * 0.1f;
            tm.anchor = TextAnchor.LowerCenter;
            tm.color = color;
            go.GetComponent<MeshRenderer>().sharedMaterial = tm.font.material;
            go.AddComponent<FaceCamera>();
        }

        private static Material CreateLineMaterial()
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            return new Material(shader) { name = "ImpactLine" };
        }

        private void Clear()
        {
            RemoveShake();
            rings.Clear();
            shockFront = null;
            seismicFront = null;
            fallbackFireball = null;
            flash = null;
            if (fxRoot != null) Destroy(fxRoot.gameObject);
            fxRoot = null;
        }
    }
}
