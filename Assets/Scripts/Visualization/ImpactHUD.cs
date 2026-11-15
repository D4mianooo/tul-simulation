using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Simulation
{
    /// Panel wyników na ekranie zamiast Debug.Log:
    /// - u góry zegar symulacji i przyciski prędkości czasu,
    /// - po prawej wyniki (kroki 1-3) i dziennik zdarzeń (co dociera do budynków i kiedy),
    /// - na dole zakładki z wykresami (ImpactCharts / XCharts).
    /// Cały interfejs budowany jest z kodu, nie trzeba nic ustawiać w scenie.
    [DefaultExecutionOrder(-200)]
    public class ImpactHUD : MonoBehaviour
    {
        [SerializeField] private ImpactSimulation simulation;
        [Tooltip("Wyłącza wypisywanie raportu do konsoli (ImpactSimulation.logReport).")]
        [SerializeField] private bool muteConsoleLogs = true;
        [Tooltip("Lewa krawędź dolnego panelu wykresów — żeby nie zasłaniał panelu parametrów.")]
        [SerializeField] private float leftMargin = 530f;
        [SerializeField] private int maxLogLines = 12;

        private static readonly CultureInfo CI = CultureInfo.InvariantCulture;
        private static readonly Color PanelColor = new Color(0.08f, 0.10f, 0.14f, 0.92f);
        private static readonly Color ButtonColor = new Color(0.20f, 0.24f, 0.32f, 1f);
        private static readonly Color ButtonActiveColor = new Color(0.95f, 0.55f, 0.15f, 1f);

        private Font font;
        private GameObject root;
        private Text clockText;
        private Text resultsText;
        private Text logText;
        private Text cityText;
        private CityLayer city;
        private float nextCityRefresh;
        private RectTransform chartPanel;
        private RectTransform chartArea;
        private ImpactCharts charts;
        private readonly List<Image> tabImages = new List<Image>();
        private readonly List<Image> speedImages = new List<Image>();
        private readonly float[] speeds = { 1f, 5f, 20f, 100f };
        private CollapsiblePanel resultsPanel, cityPanel, logPanel, chartsPanel;
        private SimulationUIController ui;
        private float previewAt = -1f;
        private bool firstPreview = true;

        private readonly List<string> log = new List<string>();
        private readonly List<(double time, string message)> milestones = new List<(double, string)>();
        private int nextMilestone;
        private readonly Dictionary<ImpactTarget, (bool thermal, bool seismic, bool blast)> targetState =
            new Dictionary<ImpactTarget, (bool, bool, bool)>();
        private readonly List<ImpactTarget> subscribedTargets = new List<ImpactTarget>();

        private void Awake()
        {
            if (simulation == null) simulation = FindFirstObjectByType<ImpactSimulation>();
            if (simulation != null && muteConsoleLogs) simulation.LogEnabled = false;

            city = FindFirstObjectByType<CityLayer>();
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildUI();
            RefreshCityText();
            root.SetActive(false);
        }

        private void OnEnable()
        {
            if (simulation == null) return;
            simulation.ResultsComputed += OnResultsComputed;
            simulation.Impacted += OnImpacted;
            simulation.Ticked += OnTicked;
            if (city != null) city.StatsChanged += OnCityStatsChanged;
        }

        private void OnDisable()
        {
            if (simulation != null)
            {
                simulation.ResultsComputed -= OnResultsComputed;
                simulation.Impacted -= OnImpacted;
                simulation.Ticked -= OnTicked;
            }
            if (city != null) city.StatsChanged -= OnCityStatsChanged;
            UnsubscribeTargets();
        }

        private void Start()
        {
            // Wyniki mogły zostać policzone w Awake symulacji, zanim się zapisaliśmy na zdarzenie.
            // Panel widoczny od razu po wejściu w Play (wyniki i wykresy dla bieżących parametrów).
            root.SetActive(true);
            if (simulation != null)
            {
                OnResultsComputed(simulation.Results);
                if (simulation.HasImpacted) OnImpacted(simulation.GroundZero, simulation.Results);
            }
            clockText.text = "Wybierz parametry i kliknij Start";
            HookParameterPanel();
        }

        // ------------------------------------------------------------ zdarzenia

        private void OnResultsComputed(ImpactResults res)
        {
            resultsText.text = FormatResults(res);
            UpdateResultsTitle(res, false);
            // Wykresy budujemy dopiero przy widocznym panelu (XCharts potrzebuje aktywnego obiektu).
            if (root.activeSelf) { charts.SetResults(res); UpdateChartsTitle(); }
        }

        private void OnImpacted(Vector3 point, ImpactResults res)
        {
            root.SetActive(true);
            OnResultsComputed(res);

            log.Clear();
            AddLog(0.0, string.Format(CI, "<b>Uderzenie</b>: E = {0} Mt TNT, M = {1:F1}, krater {2}",
                FormatNumber(res.energyMt), res.magnitude, ImpactChartData.FormatDistance(res.finalDiameterM)));

            BuildMilestones(res);
            SubscribeTargets();
            casualtyChartFresh = false;
            RefreshCityText();
            UpdateSpeedButtons();
        }

        private void OnTicked(double time)
        {
            clockText.text = string.Format(CI, "t = {0}", ImpactChartData.FormatTime(time));

            while (nextMilestone < milestones.Count && milestones[nextMilestone].time <= time)
            {
                AddLog(milestones[nextMilestone].time, milestones[nextMilestone].message);
                nextMilestone++;
            }
        }

        /// Zdarzenia globalne: kiedy fala mija kolejne progi zniszczeń, kiedy kończy się promieniowanie.
        private void BuildMilestones(ImpactResults res)
        {
            milestones.Clear();
            nextMilestone = 0;

            if (res.hasFireball)
            {
                milestones.Add((res.timeOfMaxRadiationS, string.Format(CI,
                    "<color=#FFA726>Kula ognia</color> R = {0}: maksimum promieniowania",
                    ImpactChartData.FormatDistance(res.fireballRadiusM))));
                milestones.Add((res.irradiationDurationS, "<color=#FFA726>Koniec naświetlania cieplnego</color>"));
            }

            double rMax = ImpactChartData.RadiusForOverpressure(res, BlastDamage.Levels[0].thresholdPa) * 1.05;
            var table = new BlastFrontTable(res, rMax);
            var levels = BlastDamage.Levels;
            for (int i = levels.Length - 1; i >= 0; i--)
            {
                double r = ImpactChartData.RadiusForOverpressure(res, levels[i].thresholdPa);
                if (r <= res.CraterRimRadiusM) continue;
                milestones.Add((table.TimeAt(r), string.Format(CI,
                    "<color=#4FC3F7>Fala</color> na {0}: {1} kPa – {2}",
                    ImpactChartData.FormatDistance(r), (levels[i].thresholdPa / 1000.0).ToString("0.#", CI),
                    ImpactChartData.BlastLevelNamesPl[i].ToLowerInvariant())));
            }

            milestones.Sort((a, b) => a.time.CompareTo(b.time));
        }

        private void SubscribeTargets()
        {
            UnsubscribeTargets();
            foreach (var target in ImpactTarget.All)
            {
                target.StateChanged += OnTargetStateChanged;
                subscribedTargets.Add(target);
                targetState[target] = (false, false, false);
            }
        }

        private void UnsubscribeTargets()
        {
            foreach (var target in subscribedTargets)
                if (target != null) target.StateChanged -= OnTargetStateChanged;
            subscribedTargets.Clear();
            targetState.Clear();
        }

        /// Zamienia zmianę stanu budynku na wpis w dzienniku (to, co wcześniej szło do Debug.Log).
        private void OnTargetStateChanged(ImpactTarget t)
        {
            targetState.TryGetValue(t, out var prev);
            var e = t.Effects;
            string who = string.Format(CI, "<b>{0}</b> ({1})", t.Label, ImpactChartData.FormatDistance(e.distanceM));
            double now = simulation != null ? simulation.TimeSinceImpact : 0.0;

            if (t.ThermalArrived && !prev.thermal)
            {
                string burns = t.Burns == BurnDegree.None ? "brak oparzeń" : "oparzenia " + (int)t.Burns + "°";
                string ignited = t.IgnitedMaterials.Count > 0 ? ", zapłon: " + TranslateMaterials(t.IgnitedMaterials) : "";
                AddLog(now, string.Format(CI, "{0} <color=#FFA726>ciepło</color> {1:0.##} MJ/m²: {2}{3}",
                    who, e.thermalExposure / 1e6, burns, ignited));
            }
            if (t.SeismicArrived && !prev.seismic)
            {
                AddLog(now, string.Format(CI, "{0} <color=#BA68C8>wstrząs</color> M_eff = {1:F1}", who, e.effectiveMagnitude));
            }
            if (t.BlastArrived && !prev.blast)
            {
                string state = t.BlastDamageLevel >= 0
                    ? ImpactChartData.BlastLevelNamesPl[t.BlastDamageLevel].ToLowerInvariant()
                    : "bez zniszczeń";
                AddLog(now, string.Format(CI, "{0} <color=#4FC3F7>fala</color> {1:0.#} kPa, wiatr {2:F0} m/s → {3}",
                    who, e.overpressurePa / 1000.0, e.windVelocity, state));
            }

            targetState[t] = (t.ThermalArrived, t.SeismicArrived, t.BlastArrived);
        }

        private static string TranslateMaterials(List<string> names)
        {
            var result = new List<string>();
            foreach (var n in names)
            {
                int idx = System.Array.FindIndex(ThermalDamage.Materials, m => m.name == n);
                result.Add(idx >= 0 ? ImpactChartData.MaterialNamesPl[idx].ToLowerInvariant() : n);
            }
            return string.Join(", ", result);
        }

        private void AddLog(double time, string message)
        {
            log.Add(string.Format(CI, "<color=#90A4AE>[{0,7}]</color> {1}", ImpactChartData.FormatTime(time), message));
            while (log.Count > maxLogLines) log.RemoveAt(0);
            logText.text = string.Join("\n", log);
            if (logPanel != null) logPanel.SetTitle("Dziennik: " + StripTags(message));
        }

        // ------------------------------------------------------------ podgląd na żywo

        /// Zmiana presetu, suwaka lub zatwierdzenie pola w panelu parametrów od razu przelicza
        /// wyniki i wykresy (bez uderzenia). Uderzenie nadal uruchamia przycisk Start.
        private void HookParameterPanel()
        {
            ui = FindFirstObjectByType<SimulationUIController>();
            if (ui == null) return;
            if (ui.presetDropdown != null) ui.presetDropdown.onValueChanged.AddListener(_ => SchedulePreview());
            if (ui.materialDropdown != null) ui.materialDropdown.onValueChanged.AddListener(_ => SchedulePreview());
            if (ui.sizeUnitDropdown != null) ui.sizeUnitDropdown.onValueChanged.AddListener(_ => SchedulePreview());
            if (ui.targetDropdown != null) ui.targetDropdown.onValueChanged.AddListener(_ => SchedulePreview());
            if (ui.sizeSlider != null) ui.sizeSlider.onValueChanged.AddListener(_ => SchedulePreview());
            if (ui.impactAngleSlider != null) ui.impactAngleSlider.onValueChanged.AddListener(_ => SchedulePreview());
            if (ui.sizeInputField != null) ui.sizeInputField.onEndEdit.AddListener(_ => SchedulePreview());
            if (ui.weightInputField != null) ui.weightInputField.onEndEdit.AddListener(_ => SchedulePreview());
            if (ui.densityInputField != null) ui.densityInputField.onEndEdit.AddListener(_ => SchedulePreview());
            if (ui.impactSpeedInputField != null) ui.impactSpeedInputField.onEndEdit.AddListener(_ => SchedulePreview());
            if (ui.impactAngleInputField != null) ui.impactAngleInputField.onEndEdit.AddListener(_ => SchedulePreview());
            SchedulePreview();
        }

        // Krótka zwłoka: panel parametrów najpierw aktualizuje swoje pola, potem czytamy wartości.
        private void SchedulePreview() => previewAt = Time.unscaledTime + 0.15f;

        private void RunPreview()
        {
            if (ui == null || !ui.TryBuildParameters(out ImpactParameters p, out _)) return;
            var res = ImpactResults.Compute(p);
            resultsText.text = "<color=#FFD54F><b>Podgląd</b> – kliknij Start, aby uruchomić uderzenie</color>\n" + FormatResults(res);
            UpdateResultsTitle(res, true);
            if (firstPreview)
            {
                // Pierwszy podgląd zastępuje wyniki startowe – bez szarej linii porównania.
                firstPreview = false;
                charts.ReplaceResults(res);
            }
            else charts.SetResults(res);
            UpdateChartsTitle();
        }

        private void UpdateResultsTitle(ImpactResults r, bool preview)
        {
            if (resultsPanel == null) return;
            resultsPanel.SetTitle(string.Format(CI, "{0}: {1} Mt, krater {2}{3}",
                preview ? "Podgląd" : "Wyniki", FormatNumber(r.energyMt),
                ImpactChartData.FormatDistance(r.finalDiameterM),
                r.breaksUpInAtmosphere ? ", rozpad w atmosferze" : ""));
        }

        // ------------------------------------------------------------ straty w mieście

        private bool casualtyChartFresh;

        private void OnCityStatsChanged()
        {
            // Zakładka "Ofiary" potrzebuje danych miasta, które liczą się zaraz po uderzeniu.
            if (!casualtyChartFresh && city != null && city.Stats.Impacted && root.activeSelf
                && charts.Current == ImpactCharts.Kind.Casualties)
            {
                casualtyChartFresh = true;
                charts.Show(ImpactCharts.Kind.Casualties);
            }

            // Odświeżanie najwyżej 10 razy na sekundę (fala mija tysiące budynków na klatkę).
            if (Time.unscaledTime < nextCityRefresh) { pendingCityRefresh = true; return; }
            nextCityRefresh = Time.unscaledTime + 0.1f;
            pendingCityRefresh = false;
            RefreshCityText();
        }

        private bool pendingCityRefresh;

        private void LateUpdate()
        {
            LayoutRightColumn();
            if (previewAt > 0f && Time.unscaledTime >= previewAt)
            {
                previewAt = -1f;
                RunPreview();
            }

            // Zaległe odświeżenie, które limit 0,1 s pominął.
            if (pendingCityRefresh && Time.unscaledTime >= nextCityRefresh)
            {
                pendingCityRefresh = false;
                nextCityRefresh = Time.unscaledTime + 0.1f;
                RefreshCityText();
            }
        }

        private void RefreshCityText()
        {
            if (cityText == null) return;
            if (city == null || !city.HasData)
            {
                cityText.text = "<b><color=#E57373>Straty w mieście</color></b>\n" +
                                "Brak danych o budynkach.\nSimulation → Import buildings from OpenStreetMap...,\n" +
                                "potem Simulation → Add Results HUD + Visuals.";
                return;
            }

            var st = city.Stats;
            var sb = new StringBuilder();
            sb.AppendLine(string.Format(CI, "<b><color=#E57373>Straty: {0}</color></b>  <size=12>(model Rumpf i in. 2017)</size>",
                string.IsNullOrEmpty(st.Place) ? "miasto" : st.Place));
            sb.AppendLine(string.Format(CI, "Mieszkańcy na mapie: <b>{0}</b>  ({1} budynków)",
                Thousands(st.Population), Thousands(st.TotalBuildings)));

            if (!st.Impacted)
            {
                sb.Append("Kliknij Start, aby zobaczyć skutki.");
                cityText.text = sb.ToString();
                return;
            }

            double pct = st.Population > 0 ? 100.0 * st.Deaths / st.Population : 0.0;
            if (cityPanel != null)
                cityPanel.SetTitle(string.Format(CI, "Straty: <color=#FF5252>{0}</color> ofiar ({1:F1}%)", Thousands(st.Deaths), pct));
            sb.AppendLine(string.Format(CI, "Ofiary śmiertelne: <b><color=#FF5252>{0}</color></b> ({1:F1}%)",
                Thousands(st.Deaths), pct));
            sb.AppendLine(string.Format(CI, "Prognoza końcowa: {0}", Thousands(st.FinalDeaths)));

            var b = st.Buildings;
            sb.AppendLine(string.Format(CI,
                "Budynki: <color=#6D4C41>■</color> zniszczone {0}, <color=#D9381E>■</color> ciężko {1}, " +
                "<color=#FF9933>■</color> umiark. {2}, <color=#F5E173>■</color> lekko {3}, " +
                "<color=#FF5A0D>■</color> płonące {4}, w kraterze {5}, nienaruszone {6}",
                Thousands(b[4]), Thousands(b[3]), Thousands(b[2]), Thousands(b[1]),
                Thousands(b[5]), Thousands(b[6]), Thousands(b[0])));

            double total = 0;
            foreach (double v in st.DeathsByEffect) total += v;
            if (total > 0)
            {
                var parts = new List<string>();
                for (int i = 0; i < st.DeathsByEffect.Length; i++)
                {
                    double share = 100.0 * st.DeathsByEffect[i] / total;
                    if (share >= 0.5) parts.Add(string.Format(CI, "{0} {1:F0}%", CasualtyModel.EffectNamesPl[i], share));
                }
                sb.Append("Przyczyny (prognoza): " + string.Join(", ", parts));
            }
            cityText.text = sb.ToString();
        }

        private static string StripTags(string s)
        {
            string plain = System.Text.RegularExpressions.Regex.Replace(s, "<.*?>", "");
            return plain.Length > 48 ? plain.Substring(0, 47) + "…" : plain;
        }

        private static string Thousands(double v)
        {
            var nfi = (NumberFormatInfo)CI.NumberFormat.Clone();
            nfi.NumberGroupSeparator = " ";
            return Math.Round(v).ToString("#,0", nfi);
        }

        // ------------------------------------------------------------ tekst wyników

        private static string FormatResults(ImpactResults r)
        {
            var p = r.input;
            var sb = new StringBuilder();
            sb.AppendLine("<b><size=22>Wyniki symulacji</size></b>");
            sb.AppendLine();
            sb.AppendLine("<b><color=#4FC3F7>Impaktor</color></b>");
            sb.AppendLine(string.Format(CI, "L0 = {0:F0} m,  ρ = {1:F0} kg/m³,  v0 = {2:F1} km/s,  θ = {3:F0}°",
                p.diameter, p.impactorDensity, p.velocity / 1000.0, p.angleDeg));
            sb.AppendLine(string.Format(CI, "Masa: {0} kg", FormatNumber(r.massKg)));
            sb.AppendLine(string.Format(CI, "Energia: {0} J = <b>{1} Mt TNT</b>", FormatNumber(r.energyJ), FormatNumber(r.energyMt)));
            sb.AppendLine();
            sb.AppendLine("<b><color=#4FC3F7>Atmosfera</color></b>");
            sb.AppendLine(r.breaksUpInAtmosphere
                ? string.Format(CI, "Rozpad na wysokości: {0:F1} km", r.breakupAltitudeM / 1000.0)
                : "Brak rozpadu (Y_i > ρ0·v²)");
            sb.AppendLine(string.Format(CI, "Prędkość przy ziemi v_f: {0:F2} km/s", r.surfaceVelocity / 1000.0));
            sb.AppendLine();
            sb.AppendLine(string.Format("<b><color=#4FC3F7>Krater</color></b> ({0})", r.isComplexCrater ? "złożony" : "prosty"));
            sb.AppendLine(string.Format(CI, "Przejściowy: D = {0}, d = {1}",
                ImpactChartData.FormatDistance(r.transientDiameterM), ImpactChartData.FormatDistance(r.transientDepthM)));
            sb.AppendLine(string.Format(CI, "Końcowy: D = {0}, d = {1}",
                ImpactChartData.FormatDistance(r.finalDiameterM), ImpactChartData.FormatDistance(r.finalDepthM)));
            sb.AppendLine();
            sb.AppendLine("<b><color=#FFA726>Kula ognia</color></b>");
            sb.AppendLine(r.hasFireball
                ? string.Format(CI, "R = {0}, t_max = {1}, t_irr = {2}",
                    ImpactChartData.FormatDistance(r.fireballRadiusM),
                    ImpactChartData.FormatTime(r.timeOfMaxRadiationS),
                    ImpactChartData.FormatTime(r.irradiationDurationS))
                : "brak (v_f ≤ 15 km/s)");
            sb.AppendLine(string.Format(CI, "<b><color=#BA68C8>Sejsmika</color></b>: M = {0:F2}", r.magnitude));
            sb.AppendLine();
            sb.AppendLine("<b><color=#E57373>Zasięg fali uderzeniowej</color></b>");
            var levels = BlastDamage.Levels;
            for (int i = levels.Length - 1; i >= 0; i--)
            {
                if (i > 0 && System.Math.Abs(levels[i].thresholdPa - levels[i - 1].thresholdPa) < 1.0) continue;
                double rad = ImpactChartData.RadiusForOverpressure(r, levels[i].thresholdPa);
                sb.AppendLine(string.Format(CI, "{0} ({1:0.#} kPa): {2}",
                    ImpactChartData.BlastLevelNamesPl[i], levels[i].thresholdPa / 1000.0,
                    rad <= r.CraterRimRadiusM ? "w kraterze" : ImpactChartData.FormatDistance(rad)));
            }
            return sb.ToString().TrimEnd();
        }

        private static string FormatNumber(double v)
        {
            double a = System.Math.Abs(v);
            if (a != 0.0 && (a >= 1e5 || a < 0.01)) return v.ToString("0.00E+0", CI);
            if (a >= 100) return v.ToString("F0", CI);
            return v.ToString("0.##", CI);
        }

        // ------------------------------------------------------------ budowanie UI

        private void BuildUI()
        {
            EnsureEventSystem();

            root = new GameObject("ImpactHUD Canvas", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            root.AddComponent<GraphicRaycaster>();
            var rootRt = (RectTransform)root.transform;

            // Zegar + prędkość czasu (góra, środek)
            var bar = Panel(rootRt, "TimeBar", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(120f, -20f), new Vector2(620f, 52f));
            clockText = MakeLabel(bar, "t = 0 s", 24, TextAnchor.MiddleLeft, FontStyle.Bold);
            SetRect(clockText.rectTransform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(16, 0), new Vector2(190, 0));

            float x = 200f;
            foreach (float s in speeds)
            {
                float speed = s;
                var b = MakeButton(bar, "x" + speed.ToString("0", CI), () => SetSpeed(speed));
                SetRect((RectTransform)b.transform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(x, -18), new Vector2(x + 62, 18));
                speedImages.Add(b.GetComponent<Image>());
                x += 68f;
            }
            var restart = MakeButton(bar, "Powtórz", Restart);
            SetRect((RectTransform)restart.transform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(x + 6, -18), new Vector2(x + 132, 18));

            // Prawa kolumna: zwijane panele ustawiane jeden pod drugim (LayoutRightColumn).
            resultsPanel = MakeCollapsible(rootRt, "Results", "Wyniki symulacji", 470f, new Vector2(1, 1));
            resultsText = MakeLabel(resultsPanel.Content, "", 15, TextAnchor.UpperLeft, FontStyle.Normal);
            SetRect(resultsText.rectTransform, Vector2.zero, Vector2.one, new Vector2(16, 6), new Vector2(-12, -6));

            cityPanel = MakeCollapsible(rootRt, "CityStats", "Straty w mieście", 225f, new Vector2(1, 1));
            cityText = MakeLabel(cityPanel.Content, "", 15, TextAnchor.UpperLeft, FontStyle.Normal);
            SetRect(cityText.rectTransform, Vector2.zero, Vector2.one, new Vector2(16, 6), new Vector2(-12, -6));

            logPanel = MakeCollapsible(rootRt, "EventLog", "Dziennik zdarzeń", 280f, new Vector2(1, 1));
            logText = MakeLabel(logPanel.Content, "", 15, TextAnchor.LowerLeft, FontStyle.Normal);
            SetRect(logText.rectTransform, Vector2.zero, Vector2.one, new Vector2(16, 8), new Vector2(-12, -6));
            logText.verticalOverflow = VerticalWrapMode.Truncate;

            // Wykresy: dół ekranu, między panelem parametrów a prawą kolumną; rozwijają się w górę.
            chartsPanel = MakeCollapsible(rootRt, "Charts", "Wykresy", 580f, new Vector2(0.5f, 0));
            var chartRt = (RectTransform)chartsPanel.transform;
            chartRt.anchorMin = new Vector2(0, 0);
            chartRt.anchorMax = new Vector2(1, 0);
            chartRt.pivot = new Vector2(0.5f, 0);
            chartRt.offsetMin = new Vector2(leftMargin, 20);
            chartRt.offsetMax = new Vector2(-490, 20 + chartsPanel.CurrentHeight);
            chartPanel = chartRt;

            var tabs = new GameObject("Tabs", typeof(RectTransform));
            var tabsRt = (RectTransform)tabs.transform;
            tabsRt.SetParent(chartsPanel.Content, false);
            SetRect(tabsRt, new Vector2(0, 1), new Vector2(1, 1), new Vector2(8, -40), new Vector2(-8, -4));
            var layout = tabs.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 4;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            for (int i = 0; i < ImpactCharts.TabNames.Length; i++)
            {
                var kind = (ImpactCharts.Kind)i;
                var b = MakeButton(tabsRt, ImpactCharts.TabNames[i], () => ShowChart(kind));
                b.GetComponentInChildren<Text>().fontSize = 15;
                tabImages.Add(b.GetComponent<Image>());
            }

            var areaGo = new GameObject("ChartArea", typeof(RectTransform));
            chartArea = (RectTransform)areaGo.transform;
            chartArea.SetParent(chartsPanel.Content, false);
            SetRect(chartArea, Vector2.zero, Vector2.one, new Vector2(6, 6), new Vector2(-6, -46));

            charts = gameObject.AddComponent<ImpactCharts>();
            charts.Setup(chartArea);
            HighlightTab(charts.Current);
            UpdateChartsTitle();
        }

        private CollapsiblePanel MakeCollapsible(RectTransform parent, string name, string title, float contentHeight, Vector2 pivot)
        {
            var rt = Panel(parent, name, new Vector2(1, 1), new Vector2(1, 1), pivot, new Vector2(-20, -20), new Vector2(450, 34));
            var panel = rt.gameObject.AddComponent<CollapsiblePanel>();
            panel.Build(font, title, contentHeight, new Color(0.16f, 0.19f, 0.25f, 0.97f));
            return panel;
        }

        /// Prawa kolumna: panele jeden pod drugim, żeby rozwinięty panel przesuwał kolejne w dół.
        private void LayoutRightColumn()
        {
            float y = -20f;
            foreach (var p in new[] { resultsPanel, cityPanel, logPanel })
            {
                if (p == null) continue;
                var rt = (RectTransform)p.transform;
                rt.anchoredPosition = new Vector2(-20f, y);
                y -= p.CurrentHeight + 8f;
            }
            if (chartPanel != null && chartsPanel != null)
                chartPanel.offsetMax = new Vector2(chartPanel.offsetMax.x, 20f + chartsPanel.CurrentHeight);
        }

        private void UpdateChartsTitle()
        {
            if (chartsPanel != null)
                chartsPanel.SetTitle("Wykresy: " + ImpactCharts.TabNames[(int)charts.Current] +
                                     (charts.HasPrevious ? "  <color=#B0BEC5>(szara linia = poprzednie parametry)</color>" : ""));
        }

        private void ShowChart(ImpactCharts.Kind kind)
        {
            charts.Show(kind);
            HighlightTab(kind);
            UpdateChartsTitle();
        }

        private void HighlightTab(ImpactCharts.Kind kind)
        {
            for (int i = 0; i < tabImages.Count; i++)
                tabImages[i].color = i == (int)kind ? ButtonActiveColor : ButtonColor;
        }

        private void SetSpeed(float speed)
        {
            if (simulation == null) return;
            simulation.TimeScale = speed;
            UpdateSpeedButtons();
        }

        private void UpdateSpeedButtons()
        {
            if (simulation == null) return;
            for (int i = 0; i < speedImages.Count; i++)
                speedImages[i].color = Mathf.Approximately(speeds[i], simulation.TimeScale) ? ButtonActiveColor : ButtonColor;
        }

        private void Restart()
        {
            if (simulation == null || !simulation.HasImpacted) return;
            simulation.TriggerImpact(simulation.GroundZero);
        }

        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();
        }

        private static RectTransform Panel(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pivot, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            go.AddComponent<Image>().color = PanelColor;
            return rt;
        }

        private Text MakeLabel(RectTransform parent, string text, int size, TextAnchor anchor, FontStyle style)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = font;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = anchor;
            t.color = new Color(0.92f, 0.94f, 0.96f);
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        private Button MakeButton(RectTransform parent, string text, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Button", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = ButtonColor;
            var b = go.AddComponent<Button>();
            b.targetGraphic = img;
            b.onClick.AddListener(onClick);

            var label = MakeLabel((RectTransform)go.transform, text, 18, TextAnchor.MiddleCenter, FontStyle.Bold);
            SetRect(label.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            return b;
        }

        private static void SetRect(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
        }
    }
}
