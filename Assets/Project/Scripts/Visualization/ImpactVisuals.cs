using System.Collections.Generic;
using System.Globalization;
using Simulation;
using Simulation.Core;
using Simulation.Terrain;
using UnityEngine;

namespace Simulation.Visualization {

    [DefaultExecutionOrder(-100)]
    public class ImpactVisuals : MonoBehaviour {
        [SerializeField]
        private ImpactSimulation _simulation;

        [Header("Wybuch")]
        [Tooltip("Prefab efektu wybuchu, np. z Cartoon FX Remaster Free (CFXR ... Explosion). " +
                 "Gdy pusty, używany jest prosty wbudowany efekt kuli ognia.")]
        [SerializeField]
        private GameObject _explosionPrefab;
        [Tooltip("Przybliżony promień efektu przy skali 1 (w jednostkach Unity).")]
        [SerializeField]
        private float _prefabRadiusAtScale1 = 2f;
        [SerializeField]
        private float _explosionScaleMultiplier = 1f;
        [SerializeField]
        private Vector2 _explosionScaleClamp = new Vector2(1f, 60f);
        [SerializeField]
        private float _explosionLifetime = 12f;
        [SerializeField]
        private bool _flashLight = true;

        [Header("Strefy i fronty fal")]
        [SerializeField]
        private bool _showDamageRings = true;
        [SerializeField]
        private bool _showRingLabels = true;
        [SerializeField]
        private bool _showThermalRing = true;
        [SerializeField]
        private bool _showShockFront = true;
        [SerializeField]
        private bool _showSeismicFront = true;
        [Tooltip("Pierścienie większe niż ten promień (w jednostkach Unity) są pomijane.")]
        [SerializeField]
        private float _maxRingRadiusUnits = 3000f;
        [SerializeField]
        private int _ringSegments = 160;
        [SerializeField]
        private float _groundOffset = 0.15f;

        [Header("Kamera")]
        [SerializeField]
        private bool _cameraShake = true;
        [SerializeField]
        private float _maxShakeAmplitude = 1.2f;
        [SerializeField]
        private float _shakeDuration = 2.5f;

        private static readonly CultureInfo CI = CultureInfo.InvariantCulture;

        private Transform _fxRoot;
        private Material _lineMaterial;
        private Material _labelMaterial;
        private Font _labelFont;
        private BlastFrontTable _blastTable;
        private ImpactResults _results;
        private Vector3 _groundZero;

        private struct TimedRing {
            public double appearTime;
            public GameObject go;
        }
        private readonly List<TimedRing> _rings = new List<TimedRing>();

        private LineRenderer _shockFront;
        private LineRenderer _seismicFront;
        private double _frontsMaxRadiusM;

        private GameObject _fallbackFireball;
        private Light _flash;
        private float _fireballRadiusUnits;

        private Camera _cam;
        private double _shakeStartTime = -1.0;
        private float _shakeAmplitude;
        private bool _shakeStarted;
        private Vector3 _appliedShake;

        private void Awake() {
            if (_simulation == null) _simulation = FindFirstObjectByType<ImpactSimulation>();
            _lineMaterial = CreateLineMaterial();
            EnsureGroundMaterial();
            CameraController.EnsureOnMainCamera();
        }

        public static void EnsureGroundMaterial() {
            var terrain = FindFirstObjectByType<TerrainGenerator>();
            if (terrain == null) return;
            var r = terrain.GetComponent<MeshRenderer>();
            if (r == null || r.sharedMaterial != null) return;
            r.sharedMaterial = CreateGroundMaterial();
        }

        public static Material CreateGroundMaterial() {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var m = new Material(shader) { name = "Ground" };
            var c = new Color(0.30f, 0.36f, 0.27f);
            m.SetColor("_BaseColor", c);
            m.color = c;
            m.SetFloat("_Smoothness", 0.05f);
            return m;
        }

        private void OnEnable() {
            if (_simulation != null) _simulation.Impacted += OnImpacted;
        }

        private void OnDisable() {
            if (_simulation != null) _simulation.Impacted -= OnImpacted;
            RemoveShake();
        }

        private void OnDestroy() {
            if (_lineMaterial != null) Destroy(_lineMaterial);
            if (_labelMaterial != null) Destroy(_labelMaterial);
        }

        private void Start() {
            if (_simulation != null && _simulation.HasImpacted)
                OnImpacted(_simulation.GroundZero, _simulation.Results);
        }

        private void OnImpacted(Vector3 point, ImpactResults res) {
            _results = res;
            _groundZero = point;
            Clear();

            _fxRoot = new GameObject("ImpactFX").transform;
            _fxRoot.position = point;

            double maxRingM = ImpactChartData.RadiusForOverpressure(res, BlastDamage.Levels[0].thresholdPa);
            _frontsMaxRadiusM = System.Math.Min(maxRingM * 1.15, _simulation.MetersPerUnit * _maxRingRadiusUnits);
            _blastTable = new BlastFrontTable(res, _frontsMaxRadiusM);

            SpawnExplosion();
            if (_showDamageRings) BuildDamageRings();
            if (_showShockFront) _shockFront = CreateRing("Shock front", new Color(0.85f, 0.95f, 1f, 0.9f), 1f);
            if (_showSeismicFront) _seismicFront = CreateRing("Seismic front", new Color(0.73f, 0.41f, 0.78f, 0.7f), 0.5f);

            PrepareShake();
            ExtendCameraRange();
        }

        private void ExtendCameraRange() {
            var c = Camera.main;
            if (c == null) return;
            float need = _simulation.MetersToUnits(_frontsMaxRadiusM) * 2.5f;
            if (c.farClipPlane < need) c.farClipPlane = Mathf.Min(need, 50000f);
        }

        private void SpawnExplosion() {
            _fireballRadiusUnits = _results.hasFireball
                ? _simulation.MetersToUnits(_results.fireballRadiusM)
                : _simulation.MetersToUnits(System.Math.Max(_results.finalDiameterM, 200.0));

            if (_explosionPrefab != null) {
                var fx = Instantiate(_explosionPrefab, _groundZero, Quaternion.identity, _fxRoot);
                float scale = Mathf.Clamp(_fireballRadiusUnits / Mathf.Max(0.01f, _prefabRadiusAtScale1) * _explosionScaleMultiplier,
                    _explosionScaleClamp.x, _explosionScaleClamp.y);
                fx.transform.localScale = Vector3.one * scale;

                foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true)) {
                    var main = ps.main;
                    main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                }
                Destroy(fx, _explosionLifetime);
            } else {
                _fallbackFireball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _fallbackFireball.name = "Fireball (fallback)";
                Destroy(_fallbackFireball.GetComponent<Collider>());
                _fallbackFireball.transform.SetParent(_fxRoot, false);
                var r = _fallbackFireball.GetComponent<MeshRenderer>();
                r.sharedMaterial = new Material(_lineMaterial) { color = new Color(1f, 0.75f, 0.3f, 0.9f) };
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _fallbackFireball.transform.localScale = Vector3.zero;
            }

            if (_flashLight) {
                var lightGo = new GameObject("ImpactFlash");
                lightGo.transform.SetParent(_fxRoot, false);
                lightGo.transform.localPosition = Vector3.up * Mathf.Max(1f, _fireballRadiusUnits);
                _flash = lightGo.AddComponent<Light>();
                _flash.type = LightType.Point;
                _flash.color = new Color(1f, 0.8f, 0.55f);
                _flash.range = Mathf.Max(10f, _fireballRadiusUnits * 8f);
                _flash.intensity = 0f;
            }
        }

        private void BuildDamageRings() {
            var levels = BlastDamage.Levels;

            AddTimedRing("Krawędź krateru", _results.CraterRimRadiusM, 0.0, new Color(0.85f, 0.85f, 0.85f, 1f),
                "Krater " + ImpactChartData.FormatDistance(_results.finalDiameterM));

            if (_showThermalRing && _results.hasFireball) {
                double phi3 = ImpactMath.IgnitionThreshold(ThermalDamage.ThirdDegreeAt1MtMJ, _results.energyJ);
                double r3 = ImpactChartData.RadiusForThermal(_results, phi3);
                AddTimedRing("Oparzenia 3 st.", r3, _results.timeOfMaxRadiationS, new Color(1f, 0.55f, 0.1f, 0.9f),
                    "Oparzenia 3° " + ImpactChartData.FormatDistance(r3));
            }

            for (int i = 0; i < levels.Length; i++) {
                if (i + 1 < levels.Length && System.Math.Abs(levels[i + 1].thresholdPa - levels[i].thresholdPa) < 1.0)
                    continue;
                double r = ImpactChartData.RadiusForOverpressure(_results, levels[i].thresholdPa);
                if (r <= _results.CraterRimRadiusM) continue;

                string label = string.Format(CI, "{0} {1:0.#} kPa", ImpactChartData.BlastLevelNamesPl[i], levels[i].thresholdPa / 1000.0);
                AddTimedRing(label, r, _blastTable.TimeAt(r), ImpactCharts.DamageColor(i, levels.Length), label);
            }
        }

        private void AddTimedRing(string name, double radiusM, double appearTime, Color color, string label) {
            float radiusUnits = _simulation.MetersToUnits(radiusM);
            if (radiusUnits <= 0.01f || radiusUnits > _maxRingRadiusUnits) return;

            var lr = CreateRing(name, color, Mathf.Max(0.2f, radiusUnits * 0.008f));
            SetRingRadius(lr, radiusUnits);

            if (_showRingLabels && !string.IsNullOrEmpty(label))
                CreateLabel(lr.transform, label, color, radiusUnits);

            lr.gameObject.SetActive(appearTime <= 0.0);
            _rings.Add(new TimedRing { appearTime = appearTime, go = lr.gameObject });
        }

        private void Update() {
            RemoveShake();
            if (_simulation == null || !_simulation.HasImpacted || _fxRoot == null) return;

            double t = _simulation.TimeSinceImpact;

            foreach (var ring in _rings)
                if (ring.go != null && !ring.go.activeSelf && t >= ring.appearTime)
                    ring.go.SetActive(true);

            UpdateFronts(t);
            UpdateFireball((float)t);
        }

        private void LateUpdate() {
            if (_simulation == null || !_simulation.HasImpacted || !_cameraShake) return;
            ApplyShake(_simulation.TimeSinceImpact);
        }

        private void UpdateFronts(double t) {
            if (_shockFront != null) {
                double r = _blastTable.RadiusAt(t);
                bool visible = r > _results.CraterRimRadiusM && r < _frontsMaxRadiusM * 0.999;
                _shockFront.gameObject.SetActive(visible);
                if (visible) {
                    float rUnits = _simulation.MetersToUnits(r);
                    SetRingRadius(_shockFront, rUnits);
                    _shockFront.widthMultiplier = Mathf.Max(0.2f, rUnits * 0.012f);
                }
            }

            if (_seismicFront != null) {
                double r = ImpactMath.SeismicWaveSpeedKmS * 1000.0 * t;
                bool visible = r > _results.CraterRimRadiusM && r < _frontsMaxRadiusM;
                _seismicFront.gameObject.SetActive(visible);
                if (visible) {
                    float rUnits = _simulation.MetersToUnits(r);
                    SetRingRadius(_seismicFront, rUnits);
                    _seismicFront.widthMultiplier = Mathf.Max(0.15f, rUnits * 0.006f);
                }
            }
        }

        private void UpdateFireball(float t) {
            float growTime = 0.6f;
            float fadeTime = Mathf.Clamp((float)_results.irradiationDurationS, 2f, 10f);
            float grow = Mathf.Clamp01(t / growTime);
            float fade = 1f - Mathf.Clamp01((t - growTime) / fadeTime);

            if (_fallbackFireball != null) {
                float d = _fireballRadiusUnits * 2f * Mathf.Lerp(0.2f, 1f, grow);
                _fallbackFireball.transform.localScale = Vector3.one * d;
                _fallbackFireball.transform.localPosition = Vector3.up * d * 0.35f;
                var mat = _fallbackFireball.GetComponent<MeshRenderer>().sharedMaterial;
                mat.color = Color.Lerp(new Color(0.3f, 0.1f, 0.05f, 0f), new Color(1f, 0.8f, 0.35f, 0.9f), fade);
                if (fade <= 0f) _fallbackFireball.SetActive(false);
            }

            if (_flash != null)
                _flash.intensity = 40f * grow * fade * fade;
        }

        private void PrepareShake() {
            _shakeStarted = false;
            _shakeStartTime = -1.0;
            _cam = Camera.main;
            if (!_cameraShake || _cam == null) return;

            double camDistM = _simulation.DistanceToGroundZeroM(_cam.transform.position);
            double rKm = System.Math.Max(camDistM, _results.CraterRimRadiusM) / 1000.0;
            double mEff = ImpactMath.EffectiveMagnitude(_results.magnitude, rKm);
            _shakeStartTime = ImpactMath.SeismicArrivalTime(rKm);

            _shakeAmplitude = _maxShakeAmplitude * Mathf.Clamp01(((float)mEff - 3f) / 6f);
        }

        private void ApplyShake(double t) {
            if (_cam == null || _shakeStartTime < 0.0 || _shakeAmplitude <= 0f) return;

            double local = t - _shakeStartTime;
            if (local < 0.0 || local > _shakeDuration) return;
            _shakeStarted = true;

            float decay = 1f - (float)(local / _shakeDuration);
            float time = Time.time * 25f;
            Vector3 offset = new Vector3(
                Mathf.PerlinNoise(time, 0.1f) - 0.5f,
                Mathf.PerlinNoise(0.3f, time) - 0.5f,
                Mathf.PerlinNoise(time, 0.7f) - 0.5f) * (2f * _shakeAmplitude * decay * decay);

            _cam.transform.position += offset;
            _appliedShake = offset;
        }

        private void RemoveShake() {
            if (_cam != null && _appliedShake != Vector3.zero)
                _cam.transform.position -= _appliedShake;
            _appliedShake = Vector3.zero;
        }

        private LineRenderer CreateRing(string name, Color color, float width) {
            var go = new GameObject(name);
            go.transform.SetParent(_fxRoot, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.loop = true;
            lr.positionCount = _ringSegments;
            lr.sharedMaterial = _lineMaterial;
            lr.startColor = color;
            lr.endColor = color;
            lr.widthMultiplier = width;
            lr.numCornerVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.alignment = LineAlignment.TransformZ;
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            return lr;
        }

        private void SetRingRadius(LineRenderer lr, float radiusUnits) {
            for (int i = 0; i < _ringSegments; i++) {
                float a = i * Mathf.PI * 2f / _ringSegments;
                float x = _groundZero.x + Mathf.Cos(a) * radiusUnits;
                float z = _groundZero.z + Mathf.Sin(a) * radiusUnits;
                lr.SetPosition(i, new Vector3(x, GroundHeight(x, z), z));
            }
        }

        private float GroundHeight(float x, float z) {
            var origin = new Vector3(x, _groundZero.y + 5000f, z);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 20000f, ~0, QueryTriggerInteraction.Ignore))
                return hit.point.y + _groundOffset;
            return _groundZero.y + _groundOffset;
        }

        private void CreateLabel(Transform parent, string text, Color color, float radiusUnits) {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            float a = Mathf.PI * 0.25f;
            float x = _groundZero.x + Mathf.Cos(a) * radiusUnits;
            float z = _groundZero.z + Mathf.Sin(a) * radiusUnits;
            float size = Mathf.Clamp(radiusUnits * 0.04f, 0.4f, 60f);
            go.transform.position = new Vector3(x, GroundHeight(x, z) + size * 0.6f, z);

            var tm = go.AddComponent<TextMesh>();
            if (_labelFont == null) _labelFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.font = _labelFont;
            tm.text = text;
            tm.fontSize = 64;
            tm.characterSize = size * 0.1f;
            tm.anchor = TextAnchor.LowerCenter;
            tm.color = color;

            if (_labelMaterial == null) _labelMaterial = CreateLabelMaterial(_labelFont);
            if (_labelMaterial != null && _labelFont != null && _labelFont.material != null) {
                _labelMaterial.mainTexture = _labelFont.material.mainTexture;
                go.GetComponent<MeshRenderer>().sharedMaterial = _labelMaterial;
            } else {
                go.GetComponent<MeshRenderer>().sharedMaterial = tm.font.material;
            }
        }

        private static Material CreateLabelMaterial(Font font) {
            Shader shader = Shader.Find("Simulation/BillboardText");
            if (shader == null) return null;
            var mat = new Material(shader) { name = "BillboardText" };
            if (font != null && font.material != null)
                mat.mainTexture = font.material.mainTexture;
            return mat;
        }

        private static Material CreateLineMaterial() {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            return new Material(shader) { name = "ImpactLine" };
        }

        private void Clear() {
            RemoveShake();
            _rings.Clear();
            _shockFront = null;
            _seismicFront = null;
            _fallbackFireball = null;
            _flash = null;
            if (_fxRoot != null) Destroy(_fxRoot.gameObject);
            _fxRoot = null;
        }
    }
}
