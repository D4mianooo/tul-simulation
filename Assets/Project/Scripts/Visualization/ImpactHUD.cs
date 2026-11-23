using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Simulation;
using Simulation.City;
using Simulation.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Simulation.Visualization {

    [DefaultExecutionOrder(-200)]
    public class ImpactHUD : MonoBehaviour {
        [SerializeField]
        private ImpactSimulation _simulation;
        [Tooltip("Wyłącza wypisywanie raportu do konsoli (ImpactSimulation.logReport).")]
        [SerializeField]
        private bool _muteConsoleLogs = true;
        [Tooltip("Lewa krawędź dolnego panelu wykresów — żeby nie zasłaniał panelu parametrów.")]
        [SerializeField]
        private float _leftMargin = 530f;
        [SerializeField]
        private int _maxLogLines = 12;

        private static readonly CultureInfo CI = CultureInfo.InvariantCulture;
        private static readonly Color PanelColor = new Color(0.08f, 0.10f, 0.14f, 0.92f);
        private static readonly Color ButtonColor = new Color(0.20f, 0.24f, 0.32f, 1f);
        private static readonly Color ButtonActiveColor = new Color(0.95f, 0.55f, 0.15f, 1f);

        private Font _font;
        private GameObject _root;
        private Text _clockText;
        private Text _resultsText;
        private Text _logText;
        private Text _cityText;
        private CityLayer _city;
        private float _nextCityRefresh;
        private RectTransform _chartPanel;
        private RectTransform _chartArea;
        private ImpactCharts _charts;
        private readonly List<Image> _tabImages = new List<Image>();
        private readonly List<Image> _speedImages = new List<Image>();
        private readonly float[] _speeds = { 1f, 5f, 20f, 100f };
        private CollapsiblePanel _resultsPanel, _cityPanel, _logPanel, _chartsPanel;
        private SimulationUIController _ui;
        private float _previewAt = -1f;
        private bool _firstPreview = true;

        private readonly List<string> _log = new List<string>();
        private readonly List<(double time, string message)> _milestones = new List<(double, string)>();
        private int _nextMilestone;
        private readonly Dictionary<ImpactTarget, (bool thermal, bool seismic, bool blast)> _targetState =
            new Dictionary<ImpactTarget, (bool, bool, bool)>();
        private readonly List<ImpactTarget> _subscribedTargets = new List<ImpactTarget>();

        private void Awake() {
            if (_simulation == null) _simulation = FindFirstObjectByType<ImpactSimulation>();
            if (_simulation != null && _muteConsoleLogs) _simulation.LogEnabled = false;

            _city = FindFirstObjectByType<CityLayer>();
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildUI();
            RefreshCityText();
            _root.SetActive(false);
        }

        private void OnEnable() {
            if (_simulation == null) return;
            _simulation.ResultsComputed += OnResultsComputed;
            _simulation.Impacted += OnImpacted;
            _simulation.Ticked += OnTicked;
            if (_city != null) _city.StatsChanged += OnCityStatsChanged;
        }

        private void OnDisable() {
            if (_simulation != null) {
                _simulation.ResultsComputed -= OnResultsComputed;
                _simulation.Impacted -= OnImpacted;
                _simulation.Ticked -= OnTicked;
            }
            if (_city != null) _city.StatsChanged -= OnCityStatsChanged;
            UnsubscribeTargets();
        }

        private void Start() {

            _root.SetActive(true);
            if (_simulation != null) {
                OnResultsComputed(_simulation.Results);
                if (_simulation.HasImpacted) OnImpacted(_simulation.GroundZero, _simulation.Results);
            }
            _clockText.text = "Wybierz parametry i kliknij Start";
            HookParameterPanel();
        }

        private void OnResultsComputed(ImpactResults res) {
            _resultsText.text = FormatResults(res);
            UpdateResultsTitle(res, false);

            if (_root.activeSelf) { _charts.SetResults(res); UpdateChartsTitle(); }
        }

        private void OnImpacted(Vector3 point, ImpactResults res) {
            _root.SetActive(true);
            OnResultsComputed(res);

            _log.Clear();
            AddLog(0.0, string.Format(CI, "<b>Uderzenie</b>: E = {0} Mt TNT, M = {1:F1}, krater {2}",
                FormatNumber(res.energyMt), res.magnitude, ImpactChartData.FormatDistance(res.finalDiameterM)));

            BuildMilestones(res);
            SubscribeTargets();
            _casualtyChartFresh = false;
            RefreshCityText();
            UpdateSpeedButtons();
        }

        private void OnTicked(double time) {
            _clockText.text = string.Format(CI, "t = {0}", ImpactChartData.FormatTime(time));

            while (_nextMilestone < _milestones.Count && _milestones[_nextMilestone].time <= time) {
                AddLog(_milestones[_nextMilestone].time, _milestones[_nextMilestone].message);
                _nextMilestone++;
            }
        }

        private void BuildMilestones(ImpactResults res) {
            _milestones.Clear();
            _nextMilestone = 0;

            if (res.hasFireball) {
                _milestones.Add((res.timeOfMaxRadiationS, string.Format(CI,
                    "<color=#FFA726>Kula ognia</color> R = {0}: maksimum promieniowania",
                    ImpactChartData.FormatDistance(res.fireballRadiusM))));
                _milestones.Add((res.irradiationDurationS, "<color=#FFA726>Koniec naświetlania cieplnego</color>"));
            }

            double rMax = ImpactChartData.RadiusForOverpressure(res, BlastDamage.Levels[0].thresholdPa) * 1.05;
            var table = new BlastFrontTable(res, rMax);
            var levels = BlastDamage.Levels;
            for (int i = levels.Length - 1; i >= 0; i--) {
                double r = ImpactChartData.RadiusForOverpressure(res, levels[i].thresholdPa);
                if (r <= res.CraterRimRadiusM) continue;
                _milestones.Add((table.TimeAt(r), string.Format(CI,
                    "<color=#4FC3F7>Fala</color> na {0}: {1} kPa – {2}",
                    ImpactChartData.FormatDistance(r), (levels[i].thresholdPa / 1000.0).ToString("0.#", CI),
                    ImpactChartData.BlastLevelNamesPl[i].ToLowerInvariant())));
            }

            _milestones.Sort((a, b) => a.time.CompareTo(b.time));
        }

        private void SubscribeTargets() {
            UnsubscribeTargets();
            foreach (var target in ImpactTarget.All) {
                target.StateChanged += OnTargetStateChanged;
                _subscribedTargets.Add(target);
                _targetState[target] = (false, false, false);
            }
        }

        private void UnsubscribeTargets() {
            foreach (var target in _subscribedTargets)
                if (target != null) target.StateChanged -= OnTargetStateChanged;
            _subscribedTargets.Clear();
            _targetState.Clear();
        }

        private void OnTargetStateChanged(ImpactTarget t) {
            _targetState.TryGetValue(t, out var prev);
            var e = t.Effects;
            string who = string.Format(CI, "<b>{0}</b> ({1})", t.Label, ImpactChartData.FormatDistance(e.distanceM));
            double now = _simulation != null ? _simulation.TimeSinceImpact : 0.0;

            if (t.ThermalArrived && !prev.thermal) {
                string burns = t.Burns == BurnDegree.None ? "brak oparzeń" : "oparzenia " + (int)t.Burns + "°";
                string ignited = t.IgnitedMaterials.Count > 0 ? ", zapłon: " + TranslateMaterials(t.IgnitedMaterials) : "";
                AddLog(now, string.Format(CI, "{0} <color=#FFA726>ciepło</color> {1:0.##} MJ/m²: {2}{3}",
                    who, e.thermalExposure / 1e6, burns, ignited));
            }
            if (t.SeismicArrived && !prev.seismic) {
                AddLog(now, string.Format(CI, "{0} <color=#BA68C8>wstrząs</color> M_eff = {1:F1}", who, e.effectiveMagnitude));
            }
            if (t.BlastArrived && !prev.blast) {
                string state = t.BlastDamageLevel >= 0
                    ? ImpactChartData.BlastLevelNamesPl[t.BlastDamageLevel].ToLowerInvariant()
                    : "bez zniszczeń";
                AddLog(now, string.Format(CI, "{0} <color=#4FC3F7>fala</color> {1:0.#} kPa, wiatr {2:F0} m/s → {3}",
                    who, e.overpressurePa / 1000.0, e.windVelocity, state));
            }

            _targetState[t] = (t.ThermalArrived, t.SeismicArrived, t.BlastArrived);
        }

        private static string TranslateMaterials(List<string> names) {
            var result = new List<string>();
            foreach (var n in names) {
                int idx = System.Array.FindIndex(ThermalDamage.Materials, m => m.name == n);
                result.Add(idx >= 0 ? ImpactChartData.MaterialNamesPl[idx].ToLowerInvariant() : n);
            }
            return string.Join(", ", result);
        }

        private void AddLog(double time, string message) {
            _log.Add(string.Format(CI, "<color=#90A4AE>[{0,7}]</color> {1}", ImpactChartData.FormatTime(time), message));
            while (_log.Count > _maxLogLines) _log.RemoveAt(0);
            _logText.text = string.Join("\n", _log);
            if (_logPanel != null) _logPanel.SetTitle("Dziennik: " + StripTags(message));
        }

        private void HookParameterPanel() {
            _ui = FindFirstObjectByType<SimulationUIController>();
            if (_ui == null) return;
            if (_ui.PresetDropdown != null) _ui.PresetDropdown.onValueChanged.AddListener(_ => SchedulePreview());
            if (_ui.MaterialDropdown != null) _ui.MaterialDropdown.onValueChanged.AddListener(_ => SchedulePreview());
            if (_ui.TargetDropdown != null) _ui.TargetDropdown.onValueChanged.AddListener(_ => SchedulePreview());
            if (_ui.SizeSlider != null) _ui.SizeSlider.onValueChanged.AddListener(_ => SchedulePreview());
            if (_ui.ImpactAngleSlider != null) _ui.ImpactAngleSlider.onValueChanged.AddListener(_ => SchedulePreview());
            if (_ui.SizeInputField != null) _ui.SizeInputField.onEndEdit.AddListener(_ => SchedulePreview());
            if (_ui.WeightInputField != null) _ui.WeightInputField.onEndEdit.AddListener(_ => SchedulePreview());
            if (_ui.DensityInputField != null) _ui.DensityInputField.onEndEdit.AddListener(_ => SchedulePreview());
            if (_ui.ImpactSpeedInputField != null) _ui.ImpactSpeedInputField.onEndEdit.AddListener(_ => SchedulePreview());
            if (_ui.ImpactAngleInputField != null) _ui.ImpactAngleInputField.onEndEdit.AddListener(_ => SchedulePreview());
            SchedulePreview();
        }

        private void SchedulePreview() => _previewAt = Time.unscaledTime + 0.15f;

        private void RunPreview() {
            if (_ui == null || !_ui.TryBuildParameters(out ImpactParameters p, out _)) return;
            var res = ImpactResults.Compute(p);
            _resultsText.text = "<color=#FFD54F><b>Podgląd</b> – kliknij Start, aby uruchomić uderzenie</color>\n" + FormatResults(res);
            UpdateResultsTitle(res, true);
            if (_firstPreview) {

                _firstPreview = false;
                _charts.ReplaceResults(res);
            } else _charts.SetResults(res);
            UpdateChartsTitle();
        }

        private void UpdateResultsTitle(ImpactResults r, bool preview) {
            if (_resultsPanel == null) return;
            _resultsPanel.SetTitle(string.Format(CI, "{0}: {1} Mt, krater {2}{3}",
                preview ? "Podgląd" : "Wyniki", FormatNumber(r.energyMt),
                ImpactChartData.FormatDistance(r.finalDiameterM),
                r.breaksUpInAtmosphere ? ", rozpad w atmosferze" : ""));
        }

        private bool _casualtyChartFresh;

        private void OnCityStatsChanged() {

            if (!_casualtyChartFresh && _city != null && _city.Stats.Impacted && _root.activeSelf
                && _charts.Current == ImpactCharts.Kind.Casualties) {
                _casualtyChartFresh = true;
                _charts.Show(ImpactCharts.Kind.Casualties);
            }

            if (Time.unscaledTime < _nextCityRefresh) { pendingCityRefresh = true; return; }
            _nextCityRefresh = Time.unscaledTime + 0.1f;
            pendingCityRefresh = false;
            RefreshCityText();
        }

        private bool pendingCityRefresh;

        private void LateUpdate() {
            LayoutRightColumn();
            if (_previewAt > 0f && Time.unscaledTime >= _previewAt) {
                _previewAt = -1f;
                RunPreview();
            }

            if (pendingCityRefresh && Time.unscaledTime >= _nextCityRefresh) {
                pendingCityRefresh = false;
                _nextCityRefresh = Time.unscaledTime + 0.1f;
                RefreshCityText();
            }
        }

        private void RefreshCityText() {
            if (_cityText == null) return;
            if (_city == null || !_city.HasData) {
                _cityText.text = "<b><color=#E57373>Straty w mieście</color></b>\n" +
                                "Brak danych o budynkach.\nSimulation → Import buildings from OpenStreetMap...,\n" +
                                "potem Simulation → Add Results HUD + Visuals.";
                return;
            }

            var st = _city.Stats;
            var sb = new StringBuilder();
            sb.AppendLine(string.Format(CI, "<b><color=#E57373>Straty: {0}</color></b>  <size=12>(model Rumpf i in. 2017)</size>",
                string.IsNullOrEmpty(st.Place) ? "miasto" : st.Place));
            sb.AppendLine(string.Format(CI, "Mieszkańcy na mapie: <b>{0}</b>  ({1} budynków)",
                Thousands(st.Population), Thousands(st.TotalBuildings)));

            if (!st.Impacted) {
                sb.Append("Kliknij Start, aby zobaczyć skutki.");
                _cityText.text = sb.ToString();
                return;
            }

            double pct = st.Population > 0 ? 100.0 * st.Deaths / st.Population : 0.0;
            if (_cityPanel != null)
                _cityPanel.SetTitle(string.Format(CI, "Straty: <color=#FF5252>{0}</color> ofiar ({1:F1}%)", Thousands(st.Deaths), pct));
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
            if (total > 0) {
                var parts = new List<string>();
                for (int i = 0; i < st.DeathsByEffect.Length; i++) {
                    double share = 100.0 * st.DeathsByEffect[i] / total;
                    if (share >= 0.5) parts.Add(string.Format(CI, "{0} {1:F0}%", CasualtyModel.EffectNamesPl[i], share));
                }
                sb.Append("Przyczyny (prognoza): " + string.Join(", ", parts));
            }
            _cityText.text = sb.ToString();
        }

        private static string StripTags(string s) {
            string plain = System.Text.RegularExpressions.Regex.Replace(s, "<.*?>", "");
            return plain.Length > 48 ? plain.Substring(0, 47) + "…" : plain;
        }

        private static string Thousands(double v) {
            var nfi = (NumberFormatInfo)CI.NumberFormat.Clone();
            nfi.NumberGroupSeparator = " ";
            return Math.Round(v).ToString("#,0", nfi);
        }

        private static string FormatResults(ImpactResults r) {
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
            for (int i = levels.Length - 1; i >= 0; i--) {
                if (i > 0 && System.Math.Abs(levels[i].thresholdPa - levels[i - 1].thresholdPa) < 1.0) continue;
                double rad = ImpactChartData.RadiusForOverpressure(r, levels[i].thresholdPa);
                sb.AppendLine(string.Format(CI, "{0} ({1:0.#} kPa): {2}",
                    ImpactChartData.BlastLevelNamesPl[i], levels[i].thresholdPa / 1000.0,
                    rad <= r.CraterRimRadiusM ? "w kraterze" : ImpactChartData.FormatDistance(rad)));
            }
            return sb.ToString().TrimEnd();
        }

        private static string FormatNumber(double v) {
            double a = System.Math.Abs(v);
            if (a != 0.0 && (a >= 1e5 || a < 0.01)) return v.ToString("0.00E+0", CI);
            if (a >= 100) return v.ToString("F0", CI);
            return v.ToString("0.##", CI);
        }

        private void BuildUI() {
            EnsureEventSystem();

            _root = new GameObject("ImpactHUDCanvas", typeof(RectTransform));
            _root.transform.SetParent(transform, false);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            _root.AddComponent<GraphicRaycaster>();
            var rootRt = (RectTransform)_root.transform;

            var bar = Panel(rootRt, "TimeBar", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(120f, -20f), new Vector2(620f, 52f));
            _clockText = MakeLabel(bar, "t = 0 s", 24, TextAnchor.MiddleLeft, FontStyle.Bold);
            SetRect(_clockText.rectTransform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(16, 0), new Vector2(190, 0));

            float x = 200f;
            foreach (float s in _speeds) {
                float speed = s;
                var b = MakeButton(bar, "x" + speed.ToString("0", CI), () => SetSpeed(speed));
                SetRect((RectTransform)b.transform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(x, -18), new Vector2(x + 62, 18));
                _speedImages.Add(b.GetComponent<Image>());
                x += 68f;
            }
            var restart = MakeButton(bar, "Powtórz", Restart);
            SetRect((RectTransform)restart.transform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(x + 6, -18), new Vector2(x + 132, 18));

            _resultsPanel = MakeCollapsible(rootRt, "Results", "Wyniki symulacji", 470f, new Vector2(1, 1));
            _resultsText = MakeLabel(_resultsPanel.Content, "", 15, TextAnchor.UpperLeft, FontStyle.Normal);
            SetRect(_resultsText.rectTransform, Vector2.zero, Vector2.one, new Vector2(16, 6), new Vector2(-12, -6));

            _cityPanel = MakeCollapsible(rootRt, "CityStats", "Straty w mieście", 225f, new Vector2(1, 1));
            _cityText = MakeLabel(_cityPanel.Content, "", 15, TextAnchor.UpperLeft, FontStyle.Normal);
            SetRect(_cityText.rectTransform, Vector2.zero, Vector2.one, new Vector2(16, 6), new Vector2(-12, -6));

            _logPanel = MakeCollapsible(rootRt, "EventLog", "Dziennik zdarzeń", 280f, new Vector2(1, 1));
            _logText = MakeLabel(_logPanel.Content, "", 15, TextAnchor.LowerLeft, FontStyle.Normal);
            SetRect(_logText.rectTransform, Vector2.zero, Vector2.one, new Vector2(16, 8), new Vector2(-12, -6));
            _logText.verticalOverflow = VerticalWrapMode.Truncate;

            _chartsPanel = MakeCollapsible(rootRt, "Charts", "Wykresy", 580f, new Vector2(0.5f, 0));
            var chartRt = (RectTransform)_chartsPanel.transform;
            chartRt.anchorMin = new Vector2(0, 0);
            chartRt.anchorMax = new Vector2(1, 0);
            chartRt.pivot = new Vector2(0.5f, 0);
            chartRt.offsetMin = new Vector2(_leftMargin, 20);
            chartRt.offsetMax = new Vector2(-490, 20 + _chartsPanel.CurrentHeight);
            _chartPanel = chartRt;

            var tabs = new GameObject("Tabs", typeof(RectTransform));
            var tabsRt = (RectTransform)tabs.transform;
            tabsRt.SetParent(_chartsPanel.Content, false);
            SetRect(tabsRt, new Vector2(0, 1), new Vector2(1, 1), new Vector2(8, -40), new Vector2(-8, -4));
            var layout = tabs.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 4;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            for (int i = 0; i < ImpactCharts.TabNames.Length; i++) {
                var kind = (ImpactCharts.Kind)i;
                var b = MakeButton(tabsRt, ImpactCharts.TabNames[i], () => ShowChart(kind));
                b.GetComponentInChildren<Text>().fontSize = 15;
                _tabImages.Add(b.GetComponent<Image>());
            }

            var areaGo = new GameObject("ChartArea", typeof(RectTransform));
            _chartArea = (RectTransform)areaGo.transform;
            _chartArea.SetParent(_chartsPanel.Content, false);
            SetRect(_chartArea, Vector2.zero, Vector2.one, new Vector2(6, 6), new Vector2(-6, -46));

            _charts = gameObject.AddComponent<ImpactCharts>();
            _charts.Setup(_chartArea);
            HighlightTab(_charts.Current);
            UpdateChartsTitle();
        }

        private CollapsiblePanel MakeCollapsible(RectTransform parent, string name, string title, float contentHeight, Vector2 pivot) {
            var rt = Panel(parent, name, new Vector2(1, 1), new Vector2(1, 1), pivot, new Vector2(-20, -20), new Vector2(450, 34));
            var panel = rt.gameObject.AddComponent<CollapsiblePanel>();
            panel.Build(_font, title, contentHeight, new Color(0.16f, 0.19f, 0.25f, 0.97f));
            return panel;
        }

        private void LayoutRightColumn() {
            float y = -20f;
            foreach (var p in new[] { _resultsPanel, _cityPanel, _logPanel }) {
                if (p == null) continue;
                var rt = (RectTransform)p.transform;
                rt.anchoredPosition = new Vector2(-20f, y);
                y -= p.CurrentHeight + 8f;
            }
            if (_chartPanel != null && _chartsPanel != null)
                _chartPanel.offsetMax = new Vector2(_chartPanel.offsetMax.x, 20f + _chartsPanel.CurrentHeight);
        }

        private void UpdateChartsTitle() {
            if (_chartsPanel != null)
                _chartsPanel.SetTitle("Wykresy: " + ImpactCharts.TabNames[(int)_charts.Current] +
                                     (_charts.HasPrevious ? "  <color=#B0BEC5>(szara linia = poprzednie parametry)</color>" : ""));
        }

        private void ShowChart(ImpactCharts.Kind kind) {
            _charts.Show(kind);
            HighlightTab(kind);
            UpdateChartsTitle();
        }

        private void HighlightTab(ImpactCharts.Kind kind) {
            for (int i = 0; i < _tabImages.Count; i++)
                _tabImages[i].color = i == (int)kind ? ButtonActiveColor : ButtonColor;
        }

        private void SetSpeed(float speed) {
            if (_simulation == null) return;
            _simulation.TimeScale = speed;
            UpdateSpeedButtons();
        }

        private void UpdateSpeedButtons() {
            if (_simulation == null) return;
            for (int i = 0; i < _speedImages.Count; i++)
                _speedImages[i].color = Mathf.Approximately(_speeds[i], _simulation.TimeScale) ? ButtonActiveColor : ButtonColor;
        }

        private void Restart() {
            if (_simulation == null || !_simulation.HasImpacted) return;
            _simulation.TriggerImpact(_simulation.GroundZero);
        }

        private static void EnsureEventSystem() {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();
        }

        private static RectTransform Panel(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pivot, Vector2 position, Vector2 size) {
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

        private Text MakeLabel(RectTransform parent, string text, int size, TextAnchor anchor, FontStyle style) {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = _font;
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

        private Button MakeButton(RectTransform parent, string text, UnityEngine.Events.UnityAction onClick) {
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

        private static void SetRect(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax) {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
        }
    }
}
