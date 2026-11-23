using System;
using System.Collections.Generic;
using System.Globalization;
using Simulation.Terrain;
using Simulation.Visualization;
using UnityEngine;

namespace Simulation.City {
    /// <summary>
    /// Aggregated building and casualty statistics for the loaded city layer.
    /// </summary>
    public class CityStats {
        public string Place = "";
        public int TotalBuildings;
        public double Population;
        public bool Impacted;
        public double Deaths;
        public double FinalDeaths;
        public readonly double[] DeathsByEffect = new double[CasualtyModel.EffectCount];
        public readonly int[] Buildings = new int[CityLayer.StateCount];
        public double[] BinDistanceKm = Array.Empty<double>();
        public double[] BinPopulation = Array.Empty<double>();
        public double[] BinDeaths = Array.Empty<double>();
    }

    /// <summary>
    /// Loads OpenStreetMap building footprints, renders instanced city meshes, and tracks casualties over time.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class CityLayer : MonoBehaviour {
        public const int StateCount = 7;

        private static readonly CultureInfo _ci = CultureInfo.InvariantCulture;

        /// <summary>
        /// Structural and thermal damage states used for instanced building rendering.
        /// </summary>
        public enum BuildingState {
            Intact = 0,
            Light = 1,
            Moderate = 2,
            Heavy = 3,
            Destroyed = 4,
            Burning = 5,
            Crater = 6
        }

        private struct Ev {
            public double Time;
            public int Building;
            public byte Type;
        }

        [SerializeField]
        private ImpactSimulation _simulation;

        [Tooltip("Plik z importera OSM. Puste = Resources/City/buildings.")]
        [SerializeField]
        private TextAsset _buildingData;

        [Tooltip("Punkt odpowiadający środkowi mapy. Puste = środek terenu (TerrainGenerator).")]
        [SerializeField]
        private Transform _mapCenter;

        [SerializeField]
        private float _heightExaggeration = 2f;

        [Tooltip("Jeśli > 0, liczba mieszkańców jest przeskalowana do tej wartości (np. dane GUS dla obszaru).")]
        [SerializeField]
        private double _targetPopulation = 0;

        [SerializeField]
        private bool _castShadows = true;

        [SerializeField]
        private int _distanceBins = 30;

        private int _count;
        private float[] _xM;
        private float[] _zM;
        private float[] _lenM;
        private float[] _widM;
        private float[] _angleDeg;
        private float[] _heightM;
        private float[] _residents;
        private byte[] _kind;
        private Vector3[] _worldPos;
        private bool _placed;

        private byte[] _state;
        private bool[] _burning;
        private double[] _survival;
        private byte[] _blastState;
        private double[] _vThermal;
        private double[] _vSeismic;
        private double[] _vBlast;
        private bool[] _ignites;

        private Ev[] _events = Array.Empty<Ev>();
        private int _nextEvent;

        [Tooltip("Mesh używany do renderowania budynków (np. Cube).")]
        [SerializeField]
        private Mesh _cityMesh;
        [Tooltip("Materiały budynków dla kolejnych stanów (Intact, Light, Moderate, Heavy, Destroyed, Burning, Crater).")]
        [SerializeField]
        private Material[] _materials = new Material[StateCount];
        private readonly List<Matrix4x4>[] _groups = new List<Matrix4x4>[StateCount];
        private bool _dirty = true;
        private Bounds _bounds;

        /// <summary>Gets the current city statistics.</summary>
        public CityStats Stats { get; } = new CityStats();

        /// <summary>Gets a value indicating whether building data was loaded.</summary>
        public bool HasData => _count > 0;

        /// <summary>Occurs when building states or casualty counts change.</summary>
        public event Action StatsChanged;

        private void OnValidate() {
            if (_materials == null || _materials.Length != StateCount) {
                Array.Resize(ref _materials, StateCount);
            }
        }

        private void Awake() {
            if (_simulation == null) _simulation = FindAnyObjectByType<ImpactSimulation>();
            if (_mapCenter == null) {
                var terrain = FindAnyObjectByType<TerrainGenerator>();
                if (terrain != null) _mapCenter = terrain.transform;
            }
            if (_buildingData == null) _buildingData = Resources.Load<TextAsset>("City/buildings");

            for (int i = 0; i < StateCount; i++) _groups[i] = new List<Matrix4x4>();
            Load();
        }

        private void OnEnable() {
            if (_simulation != null) _simulation.Impacted += OnImpacted;
        }

        private void Update() {
            if (_count == 0) return;
            if (!_placed) PlaceBuildings();

            if (_simulation && _simulation.HasImpacted && _nextEvent < _events.Length) {
                double t = _simulation.TimeSinceImpact;
                bool changed = false;
                while (_nextEvent < _events.Length && _events[_nextEvent].Time <= t) {
                    Apply(_events[_nextEvent]);
                    _nextEvent++;
                    changed = true;
                }
                if (changed) {
                    _dirty = true;
                    StatsChanged?.Invoke();
                }
            }

            if (_dirty) RebuildGroups();
            Draw();
        }

        private void OnDisable() {
            if (_simulation != null) _simulation.Impacted -= OnImpacted;
        }

        private void Load() {
            _count = 0;
            if (_buildingData == null) return;

            var lines = _buildingData.text.Split('\n');
            var tmp = new List<string[]>(lines.Length);
            foreach (var raw in lines) {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                if (line[0] == '#') {
                    int p = line.IndexOf("place=", StringComparison.Ordinal);
                    if (p >= 0) Stats.Place = line.Substring(p + 6).Split(';')[0];
                    continue;
                }
                if (line.StartsWith("x_m", StringComparison.Ordinal)) continue;
                var parts = line.Split(',');
                if (parts.Length >= 9) tmp.Add(parts);
            }

            _count = tmp.Count;
            _xM = new float[_count];
            _zM = new float[_count];
            _lenM = new float[_count];
            _widM = new float[_count];
            _angleDeg = new float[_count];
            _heightM = new float[_count];
            _residents = new float[_count];
            _kind = new byte[_count];
            _worldPos = new Vector3[_count];

            double total = 0;
            for (int i = 0; i < _count; i++) {
                var p = tmp[i];
                _xM[i] = F(p[0]);
                _zM[i] = F(p[1]);
                _lenM[i] = F(p[3]);
                _widM[i] = F(p[4]);
                _angleDeg[i] = F(p[5]);
                _heightM[i] = F(p[6]);
                _residents[i] = F(p[7]);
                _kind[i] = byte.TryParse(p[8], out byte k) ? k : (byte)0;
                total += _residents[i];
            }

            if (_targetPopulation > 0 && total > 0) {
                float scale = (float)(_targetPopulation / total);
                for (int i = 0; i < _count; i++) _residents[i] *= scale;
                total = _targetPopulation;
            }

            Stats.TotalBuildings = _count;
            Stats.Population = total;

            _state = new byte[_count];
            _burning = new bool[_count];
            _survival = new double[_count];
            for (int i = 0; i < _count; i++) _survival[i] = 1.0;
            Stats.Buildings[(int)BuildingState.Intact] = _count;
        }

        private static float F(string s) =>
            float.TryParse(s, NumberStyles.Float, _ci, out float v) ? v : 0f;

        private void PlaceBuildings() {
            if (_simulation == null || _count == 0) return;
            float mpu = _simulation.MetersPerUnit;
            Vector3 c = _mapCenter ? _mapCenter.position : Vector3.zero;

            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = -min;
            for (int i = 0; i < _count; i++) {
                float x = c.x + _xM[i] / mpu;
                float z = c.z + _zM[i] / mpu;
                float y = c.y;
                if (Physics.Raycast(new Vector3(x, c.y + 5000f, z), Vector3.down, out RaycastHit hit, 20000f, ~0,QueryTriggerInteraction.Ignore)) y = hit.point.y;
                _worldPos[i] = new Vector3(x, y, z);
                min = Vector3.Min(min, _worldPos[i]);
                max = Vector3.Max(max, _worldPos[i]);
            }
            _bounds = new Bounds((min + max) * 0.5f, (max - min) + Vector3.one * 50f);
            _placed = true;
            _dirty = true;
        }

        private void OnImpacted(Vector3 gz, ImpactResults res) {
            if (_count == 0) return;
            if (!_placed) PlaceBuildings();

            double rim = res.CraterRimRadiusM;
            double maxDist = 0;
            var dist = new double[_count];
            for (int i = 0; i < _count; i++) {
                dist[i] = _simulation.DistanceToGroundZeroM(_worldPos[i]);
                maxDist = Math.Max(maxDist, dist[i]);
            }

            var table = new BlastFrontTable(res, Math.Max(maxDist, rim) * 1.01 + 10.0);
            double ignitionPlywood = ImpactMath.IgnitionThreshold(ThermalDamage.Materials[1].thresholdAt1MtMJ, res.energyJ);

            _vThermal = new double[_count];
            _vSeismic = new double[_count];
            _vBlast = new double[_count];
            _blastState = new byte[_count];
            _ignites = new bool[_count];

            var stats = Stats;
            stats.Impacted = true;
            stats.Deaths = 0;
            stats.FinalDeaths = 0;
            Array.Clear(stats.DeathsByEffect, 0, stats.DeathsByEffect.Length);
            Array.Clear(stats.Buildings, 0, stats.Buildings.Length);

            int bins = Mathf.Max(5, _distanceBins);
            double binWidth = Math.Max(maxDist, 1.0) / bins;
            stats.BinDistanceKm = new double[bins];
            stats.BinPopulation = new double[bins];
            stats.BinDeaths = new double[bins];
            for (int b = 0; b < bins; b++) stats.BinDistanceKm[b] = (b + 0.5) * binWidth / 1000.0;

            var evs = new List<Ev>(_count * 3);

            for (int i = 0; i < _count; i++) {
                double d = dist[i];
                double r = _residents[i];
                int bin = Math.Min(bins - 1, (int)(d / binWidth));
                stats.BinPopulation[bin] += r;
                _survival[i] = 1.0;
                _burning[i] = false;

                if (d <= rim) {
                    _state[i] = (byte)BuildingState.Crater;
                    _survival[i] = 0.0;
                    stats.Deaths += r;
                    stats.FinalDeaths += r;
                    stats.DeathsByEffect[(int)CasualtyModel.Effect.Crater] += r;
                    stats.BinDeaths[bin] += r;
                    stats.Buildings[(int)BuildingState.Crater]++;
                    continue;
                }

                _state[i] = (byte)BuildingState.Intact;
                stats.Buildings[(int)BuildingState.Intact]++;

                double p = ImpactMath.Overpressure(d, res.energyKt);
                double u = ImpactMath.WindVelocity(p);
                double phi = res.hasFireball ? ImpactMath.ThermalExposure(res.energyJ, d) : 0.0;
                double mEff = ImpactMath.EffectiveMagnitude(res.magnitude, d / 1000.0);
                double tau = ImpactMath.EjectaThickness(res.transientDiameterM, d);

                double vT = CasualtyModel.Thermal(phi);
                double vS = CasualtyModel.Seismic(mEff);
                double vP = CasualtyModel.Overpressure(p);
                double vW = CasualtyModel.Wind(u);
                double vE = CasualtyModel.Ejecta(tau);
                double vB = 1.0 - (1.0 - vP) * (1.0 - vW) * (1.0 - vE);

                _vThermal[i] = vT;
                _vSeismic[i] = vS;
                _vBlast[i] = vB;
                _blastState[i] = (byte)StateFromOverpressure(p);
                _ignites[i] = res.hasFireball && phi >= ignitionPlywood;

                double vAll = 1.0 - (1.0 - vT) * (1.0 - vS) * (1.0 - vB);
                double dead = r * vAll;
                stats.FinalDeaths += dead;
                stats.BinDeaths[bin] += dead;
                double sum = vT + vS + vP + vW + vE;
                if (sum > 0) {
                    stats.DeathsByEffect[(int)CasualtyModel.Effect.Thermal] += dead * vT / sum;
                    stats.DeathsByEffect[(int)CasualtyModel.Effect.Seismic] += dead * vS / sum;
                    stats.DeathsByEffect[(int)CasualtyModel.Effect.Overpressure] += dead * vP / sum;
                    stats.DeathsByEffect[(int)CasualtyModel.Effect.Wind] += dead * vW / sum;
                    stats.DeathsByEffect[(int)CasualtyModel.Effect.Ejecta] += dead * vE / sum;
                }

                if (res.hasFireball) evs.Add(new Ev { Time = res.timeOfMaxRadiationS, Building = i, Type = 0 });
                evs.Add(new Ev { Time = ImpactMath.SeismicArrivalTime(d / 1000.0), Building = i, Type = 1 });
                evs.Add(new Ev { Time = table.TimeAt(d), Building = i, Type = 2 });
            }

            evs.Sort((a, b) => a.Time.CompareTo(b.Time));
            _events = evs.ToArray();
            _nextEvent = 0;
            _dirty = true;
            StatsChanged?.Invoke();
        }

        private static BuildingState StateFromOverpressure(double pPa) {
            double kPa = pPa / 1000.0;
            if (kPa >= 48.3) return BuildingState.Destroyed;
            if (kPa >= 13.8) return BuildingState.Heavy;
            if (kPa >= 4.8) return BuildingState.Moderate;
            if (kPa >= 1.0) return BuildingState.Light;
            return BuildingState.Intact;
        }

        private void Apply(Ev e) {
            int i = e.Building;
            double v = e.Type == 0 ? _vThermal[i] : e.Type == 1 ? _vSeismic[i] : _vBlast[i];
            double before = _survival[i];
            _survival[i] = before * (1.0 - v);
            Stats.Deaths += _residents[i] * (before - _survival[i]);

            int oldVisual = VisualState(i);
            if (e.Type == 0 && _ignites[i]) _burning[i] = true;
            if (e.Type == 2) _state[i] = _blastState[i];
            int newVisual = VisualState(i);

            if (oldVisual != newVisual) {
                Stats.Buildings[oldVisual]--;
                Stats.Buildings[newVisual]++;
            }
        }

        private int VisualState(int i) {
            if (_state[i] == (byte)BuildingState.Crater) return (int)BuildingState.Crater;
            if (_state[i] == (byte)BuildingState.Destroyed) return (int)BuildingState.Destroyed;
            if (_burning[i]) return (int)BuildingState.Burning;
            return _state[i];
        }

        private void RebuildGroups() {
            _dirty = false;
            if (!_placed) return;
            for (int s = 0; s < StateCount; s++) _groups[s].Clear();

            float mpu = _simulation.MetersPerUnit;
            for (int i = 0; i < _count; i++) {
                int s = VisualState(i);
                if (s == (int)BuildingState.Crater) continue;

                float h = Mathf.Max(0.02f, _heightM[i] / mpu * _heightExaggeration);
                if (s == (int)BuildingState.Destroyed) h *= 0.15f;
                var scale = new Vector3(Mathf.Max(0.02f, _lenM[i] / mpu), h, Mathf.Max(0.02f, _widM[i] / mpu));
                var pos = _worldPos[i] + Vector3.up * (h * 0.5f);
                _groups[s].Add(Matrix4x4.TRS(pos, Quaternion.Euler(0f, -_angleDeg[i], 0f), scale));
            }
        }

        private void Draw() {
            if (!_placed || !_cityMesh) return;
            for (int s = 0; s < StateCount; s++) {
                if (_materials == null || s >= _materials.Length || !_materials[s]) continue;
                var list = _groups[s];
                if (list.Count == 0) continue;

                var rp = new RenderParams(_materials[s]) {
                    worldBounds = _bounds,
                    shadowCastingMode = _castShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off,
                    receiveShadows = true,
                };
                for (int start = 0; start < list.Count; start += 1023) {
                    int n = Mathf.Min(1023, list.Count - start);
                    Graphics.RenderMeshInstanced(rp, _cityMesh, 0, list, n, start);
                }
            }
        }
    }
}
