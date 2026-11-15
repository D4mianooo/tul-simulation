using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Simulation
{
    /// Statystyki strat w mieście (aktualizowane na bieżąco w trakcie symulacji).
    public class CityStats
    {
        public string Place = "";
        public int TotalBuildings;
        public double Population;

        public bool Impacted;
        public double Deaths;          // dotąd (zjawiska, które już dotarły)
        public double FinalDeaths;     // prognoza końcowa (wszystkie zjawiska)
        public readonly double[] DeathsByEffect = new double[CasualtyModel.EffectCount];  // podział prognozy końcowej
        public readonly int[] Buildings = new int[CityLayer.StateCount];                   // wg stanu

        public double[] BinDistanceKm = Array.Empty<double>();
        public double[] BinPopulation = Array.Empty<double>();
        public double[] BinDeaths = Array.Empty<double>();
    }

    /// Warstwa miasta: budynki z OpenStreetMap (Assets/Resources/City/buildings.csv) postawione na terenie,
    /// kolorowane według zniszczeń, z liczeniem ofiar wg modelu Rumpfa i in. (2017).
    /// Każdy budynek dostaje trzy zdarzenia w czasie: ciepło (t_max), wstrząs (t_s) i falę uderzeniową (t_bw),
    /// więc liczniki ofiar i kolory zmieniają się dokładnie wtedy, gdy zjawisko dociera do budynku.
    [DefaultExecutionOrder(-50)]
    public class CityLayer : MonoBehaviour
    {
        public enum BuildingState
        {
            Intact = 0,
            Light = 1,
            Moderate = 2,
            Heavy = 3,
            Destroyed = 4,
            Burning = 5,
            Crater = 6
        }

        public const int StateCount = 7;

        public static readonly string[] StateNamesPl =
        {
            "nienaruszone", "lekko uszkodzone", "umiarkowanie uszkodzone",
            "ciężko uszkodzone", "zniszczone", "płonące", "w kraterze"
        };

        public static readonly Color[] StateColors =
        {
            new Color(0.80f, 0.80f, 0.82f),
            new Color(0.96f, 0.88f, 0.45f),
            new Color(1.00f, 0.60f, 0.20f),
            new Color(0.85f, 0.22f, 0.12f),
            new Color(0.22f, 0.17f, 0.15f),
            new Color(1.00f, 0.35f, 0.05f),
            new Color(0f, 0f, 0f),
        };

        [SerializeField] private ImpactSimulation simulation;
        [Tooltip("Plik z importera OSM. Puste = Resources/City/buildings.")]
        [SerializeField] private TextAsset buildingData;
        [Tooltip("Punkt odpowiadający środkowi mapy. Puste = środek terenu (TerrainGenerator).")]
        [SerializeField] private Transform mapCenter;
        [SerializeField] private float heightExaggeration = 2f;
        [Tooltip("Jeśli > 0, liczba mieszkańców jest przeskalowana do tej wartości (np. dane GUS dla obszaru).")]
        [SerializeField] private double targetPopulation = 0;
        [SerializeField] private bool castShadows = true;
        [SerializeField] private int distanceBins = 30;

        public CityStats Stats { get; } = new CityStats();
        public bool HasData => count > 0;
        public event Action StatsChanged;

        private static readonly CultureInfo CI = CultureInfo.InvariantCulture;

        // dane budynków
        private int count;
        private float[] xM, zM, lenM, widM, angleDeg, heightM, residents;
        private byte[] kind;
        private Vector3[] worldPos;
        private bool placed;

        // stan po uderzeniu
        private byte[] state;
        private bool[] burning;
        private double[] survival;
        private byte[] blastState;
        private double[] vThermal, vSeismic, vBlast;
        private bool[] ignites;

        private struct Ev
        {
            public double time;
            public int building;
            public byte type;   // 0 ciepło, 1 wstrząs, 2 fala (nadciśnienie + wiatr + wyrzut)
        }
        private Ev[] events = Array.Empty<Ev>();
        private int nextEvent;

        // rysowanie
        private Mesh cube;
        private readonly Material[] materials = new Material[StateCount];
        private readonly List<Matrix4x4>[] groups = new List<Matrix4x4>[StateCount];
        private bool dirty = true;
        private Bounds bounds;

        private void Awake()
        {
            if (simulation == null) simulation = FindFirstObjectByType<ImpactSimulation>();
            if (mapCenter == null)
            {
                var terrain = FindFirstObjectByType<TerrainGenerator>();
                if (terrain != null) mapCenter = terrain.transform;
            }
            if (buildingData == null) buildingData = Resources.Load<TextAsset>("City/buildings");

            for (int i = 0; i < StateCount; i++) groups[i] = new List<Matrix4x4>();
            Load();
            CreateRenderResources();
        }

        private void OnEnable()
        {
            if (simulation != null) simulation.Impacted += OnImpacted;
        }

        private void OnDisable()
        {
            if (simulation != null) simulation.Impacted -= OnImpacted;
        }

        // ------------------------------------------------------------------ wczytanie

        private void Load()
        {
            count = 0;
            if (buildingData == null) return;

            var lines = buildingData.text.Split('\n');
            var tmp = new List<string[]>(lines.Length);
            foreach (var raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                if (line[0] == '#')
                {
                    int p = line.IndexOf("place=", StringComparison.Ordinal);
                    if (p >= 0) Stats.Place = line.Substring(p + 6).Split(';')[0];
                    continue;
                }
                if (line.StartsWith("x_m", StringComparison.Ordinal)) continue;
                var parts = line.Split(',');
                if (parts.Length >= 9) tmp.Add(parts);
            }

            count = tmp.Count;
            xM = new float[count]; zM = new float[count]; lenM = new float[count]; widM = new float[count];
            angleDeg = new float[count]; heightM = new float[count]; residents = new float[count];
            kind = new byte[count]; worldPos = new Vector3[count];

            double total = 0;
            for (int i = 0; i < count; i++)
            {
                var p = tmp[i];
                xM[i] = F(p[0]); zM[i] = F(p[1]);
                lenM[i] = F(p[3]); widM[i] = F(p[4]); angleDeg[i] = F(p[5]); heightM[i] = F(p[6]);
                residents[i] = F(p[7]);
                kind[i] = byte.TryParse(p[8], out byte k) ? k : (byte)0;
                total += residents[i];
            }

            if (targetPopulation > 0 && total > 0)
            {
                float scale = (float)(targetPopulation / total);
                for (int i = 0; i < count; i++) residents[i] *= scale;
                total = targetPopulation;
            }

            Stats.TotalBuildings = count;
            Stats.Population = total;

            state = new byte[count];
            burning = new bool[count];
            survival = new double[count];
            for (int i = 0; i < count; i++) survival[i] = 1.0;
            Stats.Buildings[(int)BuildingState.Intact] = count;
        }

        private static float F(string s) =>
            float.TryParse(s, NumberStyles.Float, CI, out float v) ? v : 0f;

        /// Pozycje w świecie: liczone dopiero w pierwszej klatce, gdy teren już ma collider.
        private void PlaceBuildings()
        {
            if (simulation == null || count == 0) return;
            float mpu = simulation.MetersPerUnit;
            Vector3 c = mapCenter != null ? mapCenter.position : Vector3.zero;

            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = -min;
            for (int i = 0; i < count; i++)
            {
                float x = c.x + xM[i] / mpu, z = c.z + zM[i] / mpu;
                float y = c.y;
                if (Physics.Raycast(new Vector3(x, c.y + 5000f, z), Vector3.down, out RaycastHit hit, 20000f, ~0,
                        QueryTriggerInteraction.Ignore))
                    y = hit.point.y;
                worldPos[i] = new Vector3(x, y, z);
                min = Vector3.Min(min, worldPos[i]);
                max = Vector3.Max(max, worldPos[i]);
            }
            bounds = new Bounds((min + max) * 0.5f, (max - min) + Vector3.one * 50f);
            placed = true;
            dirty = true;
        }

        // ------------------------------------------------------------------ uderzenie

        private void OnImpacted(Vector3 gz, ImpactResults res)
        {
            if (count == 0) return;
            if (!placed) PlaceBuildings();

            double rim = res.CraterRimRadiusM;
            double maxDist = 0;
            var dist = new double[count];
            for (int i = 0; i < count; i++)
            {
                dist[i] = simulation.DistanceToGroundZeroM(worldPos[i]);
                maxDist = Math.Max(maxDist, dist[i]);
            }

            var table = new BlastFrontTable(res, Math.Max(maxDist, rim) * 1.01 + 10.0);
            double ignitionPlywood = ImpactMath.IgnitionThreshold(ThermalDamage.Materials[1].thresholdAt1MtMJ, res.energyJ);

            vThermal = new double[count];
            vSeismic = new double[count];
            vBlast = new double[count];
            blastState = new byte[count];
            ignites = new bool[count];

            var stats = Stats;
            stats.Impacted = true;
            stats.Deaths = 0;
            stats.FinalDeaths = 0;
            Array.Clear(stats.DeathsByEffect, 0, stats.DeathsByEffect.Length);
            Array.Clear(stats.Buildings, 0, stats.Buildings.Length);

            int bins = Mathf.Max(5, distanceBins);
            double binWidth = Math.Max(maxDist, 1.0) / bins;
            stats.BinDistanceKm = new double[bins];
            stats.BinPopulation = new double[bins];
            stats.BinDeaths = new double[bins];
            for (int b = 0; b < bins; b++) stats.BinDistanceKm[b] = (b + 0.5) * binWidth / 1000.0;

            var evs = new List<Ev>(count * 3);

            for (int i = 0; i < count; i++)
            {
                double d = dist[i];
                double R = residents[i];
                int bin = Math.Min(bins - 1, (int)(d / binWidth));
                stats.BinPopulation[bin] += R;
                survival[i] = 1.0;
                burning[i] = false;

                if (d <= rim)
                {
                    // Krater: brak szans przeżycia (Rumpf 2017), budynek znika od razu.
                    state[i] = (byte)BuildingState.Crater;
                    survival[i] = 0.0;
                    stats.Deaths += R;
                    stats.FinalDeaths += R;
                    stats.DeathsByEffect[(int)CasualtyModel.Effect.Crater] += R;
                    stats.BinDeaths[bin] += R;
                    stats.Buildings[(int)BuildingState.Crater]++;
                    continue;
                }

                state[i] = (byte)BuildingState.Intact;
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

                vThermal[i] = vT;
                vSeismic[i] = vS;
                vBlast[i] = vB;
                blastState[i] = (byte)StateFromOverpressure(p);
                ignites[i] = res.hasFireball && phi >= ignitionPlywood;

                // Prognoza końcowa i podział na zjawiska (proporcjonalnie do V_i).
                double vAll = 1.0 - (1.0 - vT) * (1.0 - vS) * (1.0 - vB);
                double dead = R * vAll;
                stats.FinalDeaths += dead;
                stats.BinDeaths[bin] += dead;
                double sum = vT + vS + vP + vW + vE;
                if (sum > 0)
                {
                    stats.DeathsByEffect[(int)CasualtyModel.Effect.Thermal] += dead * vT / sum;
                    stats.DeathsByEffect[(int)CasualtyModel.Effect.Seismic] += dead * vS / sum;
                    stats.DeathsByEffect[(int)CasualtyModel.Effect.Overpressure] += dead * vP / sum;
                    stats.DeathsByEffect[(int)CasualtyModel.Effect.Wind] += dead * vW / sum;
                    stats.DeathsByEffect[(int)CasualtyModel.Effect.Ejecta] += dead * vE / sum;
                }

                if (res.hasFireball) evs.Add(new Ev { time = res.timeOfMaxRadiationS, building = i, type = 0 });
                evs.Add(new Ev { time = ImpactMath.SeismicArrivalTime(d / 1000.0), building = i, type = 1 });
                evs.Add(new Ev { time = table.TimeAt(d), building = i, type = 2 });
            }

            evs.Sort((a, b) => a.time.CompareTo(b.time));
            events = evs.ToArray();
            nextEvent = 0;
            dirty = true;
            StatsChanged?.Invoke();
        }

        private static BuildingState StateFromOverpressure(double pPa)
        {
            double kPa = pPa / 1000.0;
            if (kPa >= 48.3) return BuildingState.Destroyed;   // mur ceglany i mocniej
            if (kPa >= 13.8) return BuildingState.Heavy;       // mur niezbrojony, konstrukcje stalowe
            if (kPa >= 4.8) return BuildingState.Moderate;     // dom drewniany, poszycie
            if (kPa >= 1.0) return BuildingState.Light;        // szyby
            return BuildingState.Intact;
        }

        // ------------------------------------------------------------------ klatka

        private void Update()
        {
            if (count == 0) return;
            if (!placed) PlaceBuildings();

            if (simulation != null && simulation.HasImpacted && nextEvent < events.Length)
            {
                double t = simulation.TimeSinceImpact;
                bool changed = false;
                while (nextEvent < events.Length && events[nextEvent].time <= t)
                {
                    Apply(events[nextEvent]);
                    nextEvent++;
                    changed = true;
                }
                if (changed)
                {
                    dirty = true;
                    StatsChanged?.Invoke();
                }
            }

            if (dirty) RebuildGroups();
            Draw();
        }

        private void Apply(Ev e)
        {
            int i = e.building;
            double v = e.type == 0 ? vThermal[i] : e.type == 1 ? vSeismic[i] : vBlast[i];
            double before = survival[i];
            survival[i] = before * (1.0 - v);
            Stats.Deaths += residents[i] * (before - survival[i]);

            int oldVisual = VisualState(i);
            if (e.type == 0 && ignites[i]) burning[i] = true;
            if (e.type == 2) state[i] = blastState[i];
            int newVisual = VisualState(i);

            if (oldVisual != newVisual)
            {
                Stats.Buildings[oldVisual]--;
                Stats.Buildings[newVisual]++;
            }
        }

        private int VisualState(int i)
        {
            if (state[i] == (byte)BuildingState.Crater) return (int)BuildingState.Crater;
            if (state[i] == (byte)BuildingState.Destroyed) return (int)BuildingState.Destroyed;
            if (burning[i]) return (int)BuildingState.Burning;
            return state[i];
        }

        // ------------------------------------------------------------------ rysowanie

        private void CreateRenderResources()
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube = tmp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmp);

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            for (int s = 0; s < StateCount; s++)
            {
                var m = new Material(shader) { name = "Building " + StateNamesPl[s], enableInstancing = true };
                m.SetColor("_BaseColor", StateColors[s]);
                m.color = StateColors[s];
                m.SetFloat("_Smoothness", 0.15f);
                if (s == (int)BuildingState.Burning)
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", StateColors[s] * 2.5f);
                }
                materials[s] = m;
            }
        }

        private void RebuildGroups()
        {
            dirty = false;
            if (!placed) return;
            for (int s = 0; s < StateCount; s++) groups[s].Clear();

            float mpu = simulation.MetersPerUnit;
            for (int i = 0; i < count; i++)
            {
                int s = VisualState(i);
                if (s == (int)BuildingState.Crater) continue;

                float h = Mathf.Max(0.02f, heightM[i] / mpu * heightExaggeration);
                if (s == (int)BuildingState.Destroyed) h *= 0.15f;    // gruzowisko
                var scale = new Vector3(Mathf.Max(0.02f, lenM[i] / mpu), h, Mathf.Max(0.02f, widM[i] / mpu));
                var pos = worldPos[i] + Vector3.up * (h * 0.5f);
                groups[s].Add(Matrix4x4.TRS(pos, Quaternion.Euler(0f, -angleDeg[i], 0f), scale));
            }
        }

        private void Draw()
        {
            if (!placed || cube == null) return;
            for (int s = 0; s < StateCount; s++)
            {
                var list = groups[s];
                if (list.Count == 0) continue;

                var rp = new RenderParams(materials[s])
                {
                    worldBounds = bounds,
                    shadowCastingMode = castShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off,
                    receiveShadows = true,
                };
                for (int start = 0; start < list.Count; start += 1023)
                {
                    int n = Mathf.Min(1023, list.Count - start);
                    Graphics.RenderMeshInstanced(rp, cube, 0, list, n, start);
                }
            }
        }

        private void OnDestroy()
        {
            foreach (var m in materials)
                if (m != null) Destroy(m);
        }
    }
}
