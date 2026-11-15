using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using XCharts.Runtime;

namespace Simulation
{
    /// Wykresy wyników (XCharts). Każda zakładka buduje jeden LineChart od nowa.
    /// Skala logarytmiczna jest robiona ręcznie (rysujemy log10 wartości, a oś podpisujemy 10^x),
    /// bo wtedy linie progów (MarkLine) trafiają dokładnie w swoje miejsce.
    public class ImpactCharts : MonoBehaviour
    {
        public enum Kind
        {
            Velocity,
            Overpressure,
            ShockSpeed,
            Arrival,
            Thermal,
            Seismic,
            Ejecta,
            Casualties
        }

        public static readonly string[] TabNames =
        {
            "Prędkość v(z)",
            "Nadciśnienie",
            "Prędkość fali",
            "Czasy dotarcia",
            "Ciepło",
            "Wstrząsy",
            "Wyrzut",
            "Ofiary"
        };

        [SerializeField] private int samples = 160;

        private static readonly CultureInfo CI = CultureInfo.InvariantCulture;

        private static readonly Color32 Blue = new Color32(79, 195, 247, 255);
        private static readonly Color32 Orange = new Color32(255, 167, 38, 255);
        private static readonly Color32 Purple = new Color32(186, 104, 200, 255);
        private static readonly Color32 Green = new Color32(129, 199, 132, 255);
        private static readonly Color32 Grey = new Color32(176, 190, 197, 255);

        private RectTransform area;
        private LineChart chart;
        private ImpactResults results;
        private bool hasResults;
        private ImpactResults previous;    // poprzedni zestaw parametrów: szara przerywana linia
        private bool hasPrevious;

        public bool HasPrevious => hasPrevious;

        // --- stopniowe rysowanie ---
        private sealed class PendingSerie
        {
            public string name;
            public List<(double x, double y, double key)> points;
            public int next;
            public bool instant;   // linia porównania: od razu cała
        }
        private readonly List<PendingSerie> pending = new List<PendingSerie>();
        private float growStart;
        private bool currentLogY;
        private ImpactSimulation simulation;
        private const float GrowDuration = 1.5f;

        /// Usuwa szarą linię porównania (np. przy pierwszym podglądzie po starcie).
        public void ClearPrevious() => hasPrevious = false;

        /// Zastępuje bieżące wyniki bez zapamiętywania poprzednich.
        public void ReplaceResults(ImpactResults res)
        {
            results = res;
            hasResults = true;
            Show(Current);
        }

        public Kind Current { get; private set; } = Kind.Overpressure;

        public void Setup(RectTransform chartArea)
        {
            area = chartArea;
            simulation = FindFirstObjectByType<ImpactSimulation>();
        }

        public void SetResults(ImpactResults res)
        {
            if (hasResults && !SameInput(results.input, res.input))
            {
                previous = results;
                hasPrevious = true;
            }
            results = res;
            hasResults = true;
            Show(Current);
        }

        public void Show(Kind kind)
        {
            Current = kind;
            if (!hasResults || area == null || !area.gameObject.activeInHierarchy) return;

            if (chart != null) Destroy(chart.gameObject);
            chart = null;
            pending.Clear();
            growStart = Time.unscaledTime;

            switch (kind)
            {
                case Kind.Velocity: BuildVelocity(); break;
                case Kind.Overpressure: BuildOverpressure(); break;
                case Kind.ShockSpeed: BuildShockSpeed(); break;
                case Kind.Arrival: BuildArrival(); break;
                case Kind.Thermal: BuildThermal(); break;
                case Kind.Seismic: BuildSeismic(); break;
                case Kind.Ejecta: BuildEjecta(); break;
                case Kind.Casualties: BuildCasualties(); break;
            }

            if (chart != null)
            {
                FixAxisRanges();
                Reveal();
                chart.RefreshChart();
            }
        }

        private void Update()
        {
            if (chart != null && pending.Count > 0) Reveal();
        }

        /// Czy wykres pokazuje przebieg trwającego uderzenia (wtedy rysujemy w takt czasu symulacji).
        private bool SyncedToSimulation =>
            simulation != null && simulation.HasImpacted && SameInput(simulation.Results.input, results.input);

        /// Dokłada punkty, które "już się wydarzyły". Klucz punktu to czas symulacji, w którym zjawisko
        /// dociera do danej odległości; w podglądzie krzywa rośnie od lewej do prawej przez 1,5 s.
        private void Reveal()
        {
            bool synced = SyncedToSimulation;
            double t = synced ? simulation.TimeSinceImpact : 0.0;
            float frac = Mathf.Clamp01((Time.unscaledTime - growStart) / GrowDuration);
            bool added = false;

            foreach (var ps in pending)
            {
                int target = ps.instant ? ps.points.Count
                    : synced ? CountUpTo(ps, t)
                    : Mathf.CeilToInt(frac * ps.points.Count);
                while (ps.next < target)
                {
                    var pt = ps.points[ps.next++];
                    chart.AddData(ps.name, pt.x, pt.y);
                    added = true;
                }
            }
            if (added) chart.RefreshChart();
        }

        private static int CountUpTo(PendingSerie ps, double t)
        {
            int n = ps.next;
            while (n < ps.points.Count && ps.points[n].key <= t) n++;
            return n;
        }

        /// Stały zakres osi liczony z pełnych danych, żeby osie nie skakały przy dorysowywaniu.
        private void FixAxisRanges()
        {
            double xMin = double.MaxValue, xMax = double.MinValue, yMin = double.MaxValue, yMax = double.MinValue;
            foreach (var ps in pending)
                foreach (var pt in ps.points)
                {
                    if (double.IsNaN(pt.y) || double.IsInfinity(pt.y)) continue;
                    xMin = Math.Min(xMin, pt.x); xMax = Math.Max(xMax, pt.x);
                    yMin = Math.Min(yMin, pt.y); yMax = Math.Max(yMax, pt.y);
                }
            if (xMax <= xMin || yMax < yMin) return;

            var x = chart.EnsureChartComponent<XAxis>();
            x.minMaxType = Axis.AxisMinMaxType.Custom;
            x.min = xMin >= 0 && xMin < 0.35 * xMax ? 0 : NiceFloor(xMin);
            x.max = NiceCeil(xMax);

            var y = chart.EnsureChartComponent<YAxis>();
            y.minMaxType = Axis.AxisMinMaxType.Custom;
            if (currentLogY)
            {
                y.min = Math.Floor(yMin);
                y.max = Math.Max(Math.Ceiling(yMax), y.min + 1);
            }
            else
            {
                y.max = NiceCeil(yMax);
                y.min = yMin >= 0 && yMin < 0.35 * yMax ? 0 : NiceFloor(yMin);
                if (y.max <= y.min) y.max = y.min + 1;
            }
        }

        private static double NiceCeil(double v)
        {
            if (v <= 0) return 0;
            double e = Math.Pow(10, Math.Floor(Math.Log10(v)));
            double f = v / e;
            double n = f <= 1 ? 1 : f <= 2 ? 2 : f <= 2.5 ? 2.5 : f <= 5 ? 5 : 10;
            return n * e;
        }

        private static double NiceFloor(double v)
        {
            if (v <= 0) return -NiceCeil(-v);
            double e = Math.Pow(10, Math.Floor(Math.Log10(v)));
            double f = v / e;
            double n = f >= 5 ? 5 : f >= 2 ? 2 : 1;
            return n * e;
        }

        // --------------------------------------------------------------- wykresy
        // Każdy wykres rysuje bieżące parametry kolorem, a poprzednie (jeśli były inne) szarą przerywaną linią.

        private static bool SameInput(ImpactParameters a, ImpactParameters b) =>
            a.diameter == b.diameter && a.impactorDensity == b.impactorDensity && a.velocity == b.velocity &&
            a.angleDeg == b.angleDeg && a.targetDensity == b.targetDensity;

        private string PrevName(string what) =>
            string.Format(CI, "{0}: poprzednio ({1:F0} m, {2:F0} km/s)", what, previous.input.diameter, previous.input.velocity / 1000.0);

        private static readonly Color32 PrevColor = new Color32(150, 160, 170, 255);

        /// Klucz punktu = czas dotarcia fali uderzeniowej do tej odległości (x w km).
        private Func<double, double, double> BlastKey(double[] distancesM)
        {
            var table = new BlastFrontTable(results, distancesM[distancesM.Length - 1] * 1.01 + 10.0);
            return (xKm, y) => table.TimeAt(xKm * 1000.0);
        }

        private static double SeismicKey(double xKm, double y) => ImpactMath.SeismicArrivalTime(xKm);
        private static double ValueIsTime(double xKm, double y) => y;

        private void BuildVelocity()
        {
            var p = results.input;
            double zMaxKm = Math.Max(100.0, results.breakupAltitudeM / 1000.0 * 1.3);
            if (hasPrevious) zMaxKm = Math.Max(zMaxKm, previous.breakupAltitudeM / 1000.0 * 1.3);

            chart = NewChart("Prędkość impaktora przy przejściu przez atmosferę",
                string.Format(CI, "v0 = {0:F2} km/s  →  v_f = {1:F2} km/s (przy ziemi)",
                    p.velocity / 1000.0, results.surfaceVelocity / 1000.0),
                "wysokość z [km]", "v [km/s]", false);

            int s = AddLine("v(z)", Blue, VelocityPoints(results, zMaxKm));
            if (hasPrevious) AddLine(PrevName("v(z)"), PrevColor, VelocityPoints(previous, zMaxKm), true);
            YAxisOf(chart).minMaxType = Axis.AxisMinMaxType.MinMaxAuto;

            if (results.breaksUpInAtmosphere && results.breakupAltitudeM / 1000.0 < zMaxKm)
            {
                AddMarkLines(s, new List<Mark>
                {
                    Mark.Vertical(results.breakupAltitudeM / 1000.0,
                        string.Format(CI, "rozpad z_br = {0:F1} km", results.breakupAltitudeM / 1000.0), Orange)
                });
            }
        }

        private List<(double, double)> VelocityPoints(ImpactResults r, double zMaxKm)
        {
            var pts = new List<(double, double)>();
            for (int i = 0; i < samples; i++)
            {
                double zKm = zMaxKm * i / (samples - 1);
                pts.Add((zKm, ImpactChartData.VelocityAtAltitude(r.input, zKm * 1000.0) / 1000.0));
            }
            return pts;
        }

        private void BuildOverpressure()
        {
            chart = NewChart("Szczytowe nadciśnienie fali uderzeniowej p(r)",
                "skala logarytmiczna, przerywane linie: progi zniszczeń budynków",
                "odległość od punktu zerowego [km]", "p [kPa]", true);

            var d = DistanceSamples();
            int s = AddLine("p(r)", Blue, Series(d, results, (r, x) => Log(ImpactMath.Overpressure(x, r.energyKt) / 1000.0)),
                false, BlastKey(d));
            if (hasPrevious)
                AddLine(PrevName("p(r)"), PrevColor, Series(d, previous, (r, x) => Log(ImpactMath.Overpressure(x, r.energyKt) / 1000.0)), true);

            // Konstrukcja stalowa i panele stalowe mają ten sam próg 20,7 kPa: jedna linia.
            var marks = new List<Mark>();
            var levels = BlastDamage.Levels;
            for (int i = 0; i < levels.Length; i++)
            {
                if (i > 0 && Math.Abs(levels[i].thresholdPa - levels[i - 1].thresholdPa) < 1.0) continue;
                string name = ImpactChartData.BlastLevelNamesPl[i];
                if (i + 1 < levels.Length && Math.Abs(levels[i + 1].thresholdPa - levels[i].thresholdPa) < 1.0)
                    name = "Konstrukcje stalowe";
                marks.Add(Mark.Horizontal(Log(levels[i].thresholdPa / 1000.0),
                    string.Format(CI, "{0} ({1:0.#} kPa)", name, levels[i].thresholdPa / 1000.0),
                    DamageColor(i, levels.Length)));
            }
            AddMarkLines(s, marks);
        }

        private void BuildShockSpeed()
        {
            chart = NewChart("Prędkość frontu fali U i wiatru za frontem u",
                "U(p) = c0·√(1 + 6p/7Pa),   u(p) = 5p·c0 / (7Pa·√(1 + 6p/7Pa))",
                "odległość od punktu zerowego [km]", "prędkość [m/s]", false);

            var d = DistanceSamples();
            var blastKey = BlastKey(d);
            int s = AddLine("U – front fali", Blue,
                Series(d, results, (r, x) => ImpactMath.ShockFrontVelocity(ImpactMath.Overpressure(x, r.energyKt))), false, blastKey);
            AddLine("u – wiatr", Orange,
                Series(d, results, (r, x) => ImpactMath.WindVelocity(ImpactMath.Overpressure(x, r.energyKt))), false, blastKey);
            if (hasPrevious)
                AddLine(PrevName("u"), PrevColor,
                    Series(d, previous, (r, x) => ImpactMath.WindVelocity(ImpactMath.Overpressure(x, r.energyKt))), true);

            AddMarkLines(s, new List<Mark>
            {
                Mark.Horizontal(ImpactMath.SoundSpeed, "c0 = 330 m/s (prędkość dźwięku)", Grey)
            });
        }

        private void BuildArrival()
        {
            chart = NewChart("Czas dotarcia zjawisk do odległości r",
                "najpierw promieniowanie cieplne, potem wstrząs sejsmiczny, na końcu fala uderzeniowa",
                "odległość od punktu zerowego [km]", "czas [s]", false);

            var d = DistanceSamples();
            double rMax = d[d.Length - 1] * 1.01;
            var table = new BlastFrontTable(results, rMax);
            AddLine("fala uderzeniowa t_bw", Blue, Series(d, results, (r, x) => table.TimeAt(x)), false, ValueIsTime);
            AddLine("wstrząs t_s", Purple, Series(d, results, (r, x) => ImpactMath.SeismicArrivalTime(x / 1000.0)), false, ValueIsTime);
            if (results.hasFireball)
                AddLine("maks. promieniowania t_max", Orange, Series(d, results, (r, x) => r.timeOfMaxRadiationS), false, ValueIsTime);
            if (hasPrevious)
            {
                var prevTable = new BlastFrontTable(previous, rMax);
                AddLine(PrevName("t_bw"), PrevColor, Series(d, previous, (r, x) => prevTable.TimeAt(x)), true);
            }
        }

        private void BuildThermal()
        {
            if (!results.hasFireball)
            {
                chart = NewChart("Ekspozycja cieplna φ(r)",
                    "brak kuli ognia: prędkość przy ziemi v_f ≤ 15 km/s",
                    "odległość od punktu zerowego [km]", "φ [MJ/m²]", false);
                return;
            }

            chart = NewChart("Ekspozycja cieplna φ(r)",
                string.Format(CI, "skala logarytmiczna, progi zapłonu przeskalowane: φ_ig(E) = φ_ig(1 Mt)·E^(1/6), E = {0:F1} Mt",
                    results.energyMt),
                "odległość od punktu zerowego [km]", "φ [MJ/m²]", true);

            var d = DistanceSamples();
            double tMax = results.timeOfMaxRadiationS;
            int s = AddLine("φ(r)", Orange, Series(d, results, (r, x) => Log(ImpactMath.ThermalExposure(r.energyJ, x) / 1e6)),
                false, (xKm, y) => tMax);
            if (hasPrevious && previous.hasFireball)
                AddLine(PrevName("φ(r)"), PrevColor, Series(d, previous, (r, x) => Log(ImpactMath.ThermalExposure(r.energyJ, x) / 1e6)), true);

            var thresholds = new List<(string name, double at1Mt, Color32 color)>
            {
                ("Ubranie", 1.0, new Color32(239, 154, 154, 255)),
                ("Sklejka", 0.67, new Color32(255, 204, 128, 255)),
                ("Oparzenia 3°", ThermalDamage.ThirdDegreeAt1MtMJ, new Color32(229, 57, 53, 255)),
                ("Trawa", 0.38, new Color32(197, 225, 165, 255)),
                ("Gazeta", 0.33, new Color32(224, 224, 224, 255)),
                ("Drzewa liściaste / oparzenia 2°", 0.25, new Color32(251, 140, 0, 255)),
                ("Oparzenia 1°", ThermalDamage.FirstDegreeAt1MtMJ, new Color32(255, 241, 118, 255)),
            };

            var marks = new List<Mark>();
            foreach (var t in thresholds)
            {
                double mj = ImpactMath.IgnitionThreshold(t.at1Mt, results.energyJ) / 1e6;
                marks.Add(Mark.Horizontal(Log(mj), string.Format(CI, "{0} ({1:0.##} MJ/m²)", t.name, mj), t.color));
            }
            AddMarkLines(s, marks);
        }

        private void BuildSeismic()
        {
            chart = NewChart("Magnituda efektywna wstrząsu M_eff(r)",
                string.Format(CI, "M = {0:F2} w punkcie zerowym; trzy zakresy wzoru: < 60 km, 60–700 km, > 700 km",
                    results.magnitude),
                "odległość od punktu zerowego [km]", "M_eff (Richter)", false);

            double minKm = ImpactChartData.ChartMinDistanceM(results) / 1000.0;
            double maxKm = Math.Max(1000.0, ImpactChartData.ChartMaxDistanceM(results) / 1000.0);
            var d = new double[samples];
            for (int i = 0; i < samples; i++) d[i] = (minKm + (maxKm - minKm) * i / (samples - 1)) * 1000.0;

            int s = AddLine("M_eff(r)", Purple, Series(d, results, (r, x) => ImpactMath.EffectiveMagnitude(r.magnitude, x / 1000.0)),
                false, SeismicKey);
            if (hasPrevious)
                AddLine(PrevName("M_eff"), PrevColor, Series(d, previous, (r, x) => ImpactMath.EffectiveMagnitude(r.magnitude, x / 1000.0)), true);
            YAxisOf(chart).minMaxType = Axis.AxisMinMaxType.MinMaxAuto;

            AddMarkLines(s, new List<Mark>
            {
                Mark.Vertical(60.0, "60 km", Grey),
                Mark.Vertical(700.0, "700 km", Grey),
            });
        }

        private void BuildEjecta()
        {
            chart = NewChart("Grubość warstwy wyrzutu τ(r)",
                "τ(r) = D_tc⁴ / (112 r³), liczone od krawędzi krateru; skala logarytmiczna",
                "odległość od środka krateru [km]", "τ [m]", true);

            var d = DistanceSamples();
            int s = AddLine("τ(r)", Green, Series(d, results, EjectaLog), false, BlastKey(d));
            if (hasPrevious) AddLine(PrevName("τ(r)"), PrevColor, Series(d, previous, EjectaLog), true);

            AddMarkLines(s, new List<Mark>
            {
                Mark.Horizontal(Log(1.0), "1 m", Grey),
                Mark.Horizontal(Log(0.01), "1 cm", Grey),
                Mark.Horizontal(Log(0.001), "1 mm", Grey),
            });
        }

        private static double EjectaLog(ImpactResults r, double x)
        {
            if (x < r.CraterRimRadiusM) return double.NaN;
            double tau = ImpactMath.EjectaThickness(r.transientDiameterM, x);
            return tau > 0.0 ? Log(tau) : double.NaN;
        }

        /// Punkty (odległość w km, wartość) dla danej funkcji; pomija odległości wewnątrz krateru.
        private static List<(double, double)> Series(double[] distancesM, ImpactResults r, Func<ImpactResults, double, double> f)
        {
            var pts = new List<(double, double)>(distancesM.Length);
            foreach (double x in distancesM)
            {
                if (x < r.CraterRimRadiusM) continue;
                pts.Add((x / 1000.0, f(r, x)));
            }
            return pts;
        }

        private void BuildCasualties()
        {
            var city = FindFirstObjectByType<CityLayer>();
            if (city == null || !city.HasData || !city.Stats.Impacted)
            {
                chart = NewChart("Ofiary w funkcji odległości",
                    city == null || !city.HasData
                        ? "brak danych o budynkach: Simulation → Import buildings from OpenStreetMap..."
                        : "kliknij Start, aby policzyć straty",
                    "odległość od punktu zerowego [km]", "osoby", false);
                return;
            }

            var st = city.Stats;
            chart = NewChart("Mieszkańcy i ofiary w pierścieniach odległości",
                string.Format(CI, "prognoza końcowa: {0:N0} ofiar z {1:N0} mieszkańców (model Rumpf i in. 2017)",
                    st.FinalDeaths, st.Population),
                "odległość od punktu zerowego [km]", "osoby w pierścieniu", false);

            var pop = new List<(double, double)>();
            var dead = new List<(double, double)>();
            for (int i = 0; i < st.BinDistanceKm.Length; i++)
            {
                pop.Add((st.BinDistanceKm[i], st.BinPopulation[i]));
                dead.Add((st.BinDistanceKm[i], st.BinDeaths[i]));
            }
            double maxM = st.BinDistanceKm.Length > 0 ? st.BinDistanceKm[st.BinDistanceKm.Length - 1] * 1000.0 + 1000.0 : 1000.0;
            var key = BlastKey(new[] { maxM });
            AddLine("mieszkańcy", Blue, pop);
            AddLine("ofiary (prognoza)", new Color32(255, 82, 82, 255), dead, false, key);
        }

        // --------------------------------------------------------------- budowanie

        private double[] DistanceSamples()
        {
            double min = ImpactChartData.ChartMinDistanceM(results);
            double max = ImpactChartData.ChartMaxDistanceM(results);
            if (hasPrevious)
            {
                min = Math.Min(min, ImpactChartData.ChartMinDistanceM(previous));
                max = Math.Max(max, ImpactChartData.ChartMaxDistanceM(previous));
            }
            return ImpactChartData.Distances(min, max, samples);
        }

        private static double Log(double v) => Math.Log10(Math.Max(v, 1e-12));

        private static string Pow10Label(double exponent)
        {
            double v = Math.Pow(10.0, exponent);
            if (v >= 10000.0 || v < 0.001) return v.ToString("0.#E+0", CI);
            if (v >= 10.0) return v.ToString("0", CI);
            if (v >= 1.0) return v.ToString("0.#", CI);
            return v.ToString("0.###", CI);
        }

        /// XCharts szuka motywów w Resources (Assets/XCharts/Resources). Jeśli ich nie ma,
        /// motyw jest pusty i wykres wywraca się w Awake. Wtedy budujemy oba motywy w pamięci.
        private static void EnsureThemes()
        {
            if (XCThemeMgr.GetTheme(ThemeType.Default) == null)
            {
                var t = ScriptableObject.CreateInstance<Theme>();
                Theme.ResetToDefaultTheme(t);
                XCThemeMgr.AddTheme(t);
            }
            if (XCThemeMgr.GetTheme(ThemeType.Dark) == null)
            {
                var t = ScriptableObject.CreateInstance<Theme>();
                Theme.ResetToDarkTheme(t);
                XCThemeMgr.AddTheme(t);
            }
        }

        private LineChart NewChart(string title, string subtitle, string xName, string yName, bool logY)
        {
            EnsureThemes();
            currentLogY = logY;
            var go = new GameObject("Chart", typeof(RectTransform));
            go.transform.SetParent(area, false);

            var c = go.AddComponent<LineChart>();
            c.Init();
            c.UpdateTheme(ThemeType.Dark);

            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var t = c.EnsureChartComponent<Title>();
            t.show = true;
            t.text = title;
            t.subText = subtitle;
            t.location.align = Location.Align.TopLeft;

            var legend = c.EnsureChartComponent<Legend>();
            legend.show = true;
            legend.location.align = Location.Align.BottomCenter;   // pod wykresem: nie nachodzi na tytuł

            var tooltip = c.EnsureChartComponent<Tooltip>();
            // Przy osi log tooltip pokazywałby log10 wartości, więc go wyłączamy.
            tooltip.show = !logY;
            tooltip.trigger = Tooltip.Trigger.Axis;

            var grid = c.EnsureChartComponent<GridCoord>();
            grid.left = 80;
            grid.right = 190;   // miejsce na podpisy linii progów
            grid.top = 85;
            grid.bottom = 95;

            var x = c.EnsureChartComponent<XAxis>();
            x.type = Axis.AxisType.Value;
            x.boundaryGap = false;
            x.minMaxType = Axis.AxisMinMaxType.MinMaxAuto;
            x.axisName.show = true;
            x.axisName.name = xName;
            x.axisLabel.numericFormatter = "0.##";

            var y = c.EnsureChartComponent<YAxis>();
            y.type = Axis.AxisType.Value;
            y.minMaxType = logY ? Axis.AxisMinMaxType.MinMax : Axis.AxisMinMaxType.Default;
            y.axisName.show = true;
            y.axisName.name = yName;
            if (logY)
                y.axisLabel.formatterFunction = (index, value, category, content) => Pow10Label(value);
            else
                y.axisLabel.numericFormatter = "0.##";

            c.RemoveData();
            return c;
        }

        private static YAxis YAxisOf(LineChart c) => c.EnsureChartComponent<YAxis>();

        private int AddLine(string name, Color32 color, List<(double x, double y)> points, bool dashed = false,
            Func<double, double, double> key = null)
        {
            var serie = chart.AddSerie<Line>(name);
            serie.symbol.show = false;
            serie.lineStyle.width = dashed ? 1.8f : 2.5f;
            if (dashed) serie.lineStyle.type = LineStyle.Type.Dashed;
            serie.lineStyle.color = color;
            serie.itemStyle.color = color;
            serie.animation.enable = false;

            var ps = new PendingSerie
            {
                name = name,
                points = new List<(double, double, double)>(points.Count),
                instant = dashed,
            };
            foreach (var pt in points)
            {
                if (double.IsNaN(pt.y) || double.IsInfinity(pt.y)) continue;
                ps.points.Add((pt.x, pt.y, key != null ? key(pt.x, pt.y) : double.NegativeInfinity));
            }
            pending.Add(ps);
            return serie.index;
        }

        private struct Mark
        {
            public bool vertical;
            public double value;
            public string label;
            public Color32 color;

            public static Mark Horizontal(double y, string label, Color32 color) =>
                new Mark { vertical = false, value = y, label = label, color = color };

            public static Mark Vertical(double x, string label, Color32 color) =>
                new Mark { vertical = true, value = x, label = label, color = color };
        }

        private void AddMarkLines(int serieIndex, List<Mark> marks)
        {
            var markLine = chart.AddChartComponent<MarkLine>();
            markLine.show = true;
            markLine.serieIndex = serieIndex;
            markLine.data.Clear();

            foreach (var m in marks)
            {
                var d = new MarkLineData();
                d.type = MarkLineType.Custom;
                d.name = m.label;
                // Wartość 0 MarkLine traktuje jak "nie ustawiono", więc przesuwamy ją minimalnie.
                double v = Math.Abs(m.value) < 1e-9 ? 1e-9 : m.value;
                if (m.vertical) d.xValue = v;
                else d.yValue = v;

                d.lineStyle.type = LineStyle.Type.Dashed;
                d.lineStyle.color = m.color;
                d.lineStyle.width = 1.5f;
                d.startSymbol.show = false;
                d.endSymbol.show = false;
                d.label.show = true;
                d.label.formatter = m.label;
                d.label.textStyle.color = m.color;
                markLine.data.Add(d);
            }
            markLine.SetAllDirty();
        }

        /// Kolor progu: od żółtego (słaba fala) do ciemnoczerwonego (całkowite zniszczenie).
        public static Color32 DamageColor(int levelIndex, int levelCount)
        {
            float t = levelCount <= 1 ? 1f : (float)levelIndex / (levelCount - 1);
            Color a = new Color(1f, 0.92f, 0.23f);
            Color b = new Color(1f, 0.45f, 0.1f);
            Color c = new Color(0.72f, 0.05f, 0.05f);
            Color col = t < 0.5f ? Color.Lerp(a, b, t * 2f) : Color.Lerp(b, c, (t - 0.5f) * 2f);
            return col;
        }
    }
}
