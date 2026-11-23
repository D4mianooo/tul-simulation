using System;
using System.Collections.Generic;
using System.Globalization;
using Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Simulation.UI {
    /// <summary>
    /// Represents a predefined impact simulation scenario.
    /// </summary>
    [Serializable]
    public struct PresetData {
        public string name;
        public int materialIndex;
        public float sizeValue;
        public float impactSpeedKmS;
        public float impactAngleDeg;
        public int targetIndex;
    }

    /// <summary>
    /// Represents an impactor material preset with its density in kg/m3.
    /// </summary>
    [Serializable]
    public struct MaterialData {
        public string name;
        public float density;
    }

    /// <summary>
    /// Manages the simulation setup UI, parameter validation, and preset selection.
    /// </summary>
    public class SimulationUIController : MonoBehaviour {
        private const float MIN_DIAMETER_M = 20f;
        private const float MAX_DIAMETER_M = 1000f;
        private const float MIN_DENSITY = 1000f;
        private const float MAX_DENSITY = 8000f;
        private const float MIN_SPEED_KM_S = 11f;
        private const float MAX_SPEED_KM_S = 72f;
        private const float MIN_ANGLE_DEG = 5f;
        private const float MAX_ANGLE_DEG = 90f;
        private const int CUSTOM_MATERIAL_INDEX = 3;

        private static readonly CultureInfo _ci = CultureInfo.InvariantCulture;
        private static readonly string[] _targetNames = {
            "Sedimentary rock (2500 kg/m3)",
            "Crystalline rock (2750 kg/m3)"
        };
        private static readonly float[] _targetDensities = { 2500f, 2750f };

        [Header("UI Controls - Dropdowns")]
        [SerializeField]
        private TMP_Dropdown _presetDropdown;

        [SerializeField]
        private TMP_Dropdown _materialDropdown;

        [SerializeField]
        private TMP_Dropdown _targetDropdown;

        [Header("UI Controls - Inputs & Sliders")]
        [SerializeField]
        private Slider _sizeSlider;

        [SerializeField]
        private TMP_InputField _sizeInputField;

        [SerializeField]
        private TMP_InputField _weightInputField;

        [SerializeField]
        private TMP_InputField _energyJoulesInputField;

        [SerializeField]
        private TMP_InputField _energyKtTntInputField;

        [SerializeField]
        private TMP_InputField _densityInputField;

        [SerializeField]
        private TMP_InputField _impactSpeedInputField;

        [SerializeField]
        private Slider _impactAngleSlider;

        [SerializeField]
        private TMP_InputField _impactAngleInputField;

        [SerializeField]
        private Button _startButton;

        [SerializeField]
        private TMP_Text _statusText;

        [Header("3D Platform")]
        [SerializeField]
        private Transform _platformTransform;

        private List<MaterialData> _materials = new List<MaterialData>();
        private List<PresetData> _presets = new List<PresetData>();
        private bool _isUpdatingUI;
        private float _lastSpeedKmS = 20f;
        private float _lastDensity = 3000f;

        /// <summary>Gets or sets the preset selection dropdown.</summary>
        public TMP_Dropdown PresetDropdown { get => _presetDropdown; set => _presetDropdown = value; }

        /// <summary>Gets or sets the material selection dropdown.</summary>
        public TMP_Dropdown MaterialDropdown { get => _materialDropdown; set => _materialDropdown = value; }

        /// <summary>Gets or sets the target ground dropdown.</summary>
        public TMP_Dropdown TargetDropdown { get => _targetDropdown; set => _targetDropdown = value; }

        /// <summary>Gets or sets the impactor diameter slider.</summary>
        public Slider SizeSlider { get => _sizeSlider; set => _sizeSlider = value; }

        /// <summary>Gets or sets the impactor diameter input field.</summary>
        public TMP_InputField SizeInputField { get => _sizeInputField; set => _sizeInputField = value; }

        /// <summary>Gets or sets the impactor weight input field.</summary>
        public TMP_InputField WeightInputField { get => _weightInputField; set => _weightInputField = value; }

        /// <summary>Gets or sets the energy in Joules input field.</summary>
        public TMP_InputField EnergyJoulesInputField { get => _energyJoulesInputField; set => _energyJoulesInputField = value; }

        /// <summary>Gets or sets the energy in kt TNT input field.</summary>
        public TMP_InputField EnergyKtTntInputField { get => _energyKtTntInputField; set => _energyKtTntInputField = value; }

        /// <summary>Gets or sets the impactor density input field.</summary>
        public TMP_InputField DensityInputField { get => _densityInputField; set => _densityInputField = value; }

        /// <summary>Gets or sets the impact speed input field.</summary>
        public TMP_InputField ImpactSpeedInputField { get => _impactSpeedInputField; set => _impactSpeedInputField = value; }

        /// <summary>Gets or sets the impact angle slider.</summary>
        public Slider ImpactAngleSlider { get => _impactAngleSlider; set => _impactAngleSlider = value; }

        /// <summary>Gets or sets the impact angle input field.</summary>
        public TMP_InputField ImpactAngleInputField { get => _impactAngleInputField; set => _impactAngleInputField = value; }

        /// <summary>Gets or sets the start simulation button.</summary>
        public Button StartButton { get => _startButton; set => _startButton = value; }

        /// <summary>Gets or sets the status message text.</summary>
        public TMP_Text StatusText { get => _statusText; set => _statusText = value; }

        /// <summary>Gets or sets the 3D platform transform used as ground zero.</summary>
        public Transform PlatformTransform { get => _platformTransform; set => _platformTransform = value; }

        /// <summary>
        /// Occurs when the user clicks Start with valid simulation parameters.
        /// </summary>
        public event Action<ImpactParameters> SimulationRequested;

        private void Awake() {
            InitializeData();
            PopulateDropdowns();
            RegisterListeners();
        }

        private void Start() {
            if (_presets.Count > 0) {
                ApplyPreset(0);
            }
        }

        /// <summary>
        /// Validates and builds an <see cref="ImpactParameters"/> struct from current UI inputs.
        /// </summary>
        /// <param name="parameters">The resulting simulation parameters.</param>
        /// <param name="error">An error message if validation fails; otherwise <c>null</c>.</param>
        /// <returns><c>true</c> if all inputs are valid; otherwise <c>false</c>.</returns>
        public bool TryBuildParameters(out ImpactParameters parameters, out string error) {
            parameters = default;
            error = null;

            if (_sizeSlider == null) {
                error = "Size slider is not assigned.";
                return false;
            }

            if (!ReadClamped(_sizeInputField, _sizeSlider.minValue, _sizeSlider.maxValue,
                             "F1", out float diameterM)) {
                error = "Invalid impactor size.";
                return false;
            }
            _sizeSlider.SetValueWithoutNotify(diameterM);

            if (!ReadClamped(_densityInputField, MIN_DENSITY, MAX_DENSITY, "F1", out float density)) {
                error = "Invalid impactor density.";
                return false;
            }
            _lastDensity = density;

            if (!ReadClamped(_impactSpeedInputField, MIN_SPEED_KM_S, MAX_SPEED_KM_S, "F2", out float speedKmS)) {
                error = "Invalid impact speed.";
                return false;
            }
            _lastSpeedKmS = speedKmS;

            if (!ReadClamped(_impactAngleInputField, MIN_ANGLE_DEG, MAX_ANGLE_DEG, "F1", out float angle)) {
                error = "Invalid impact angle.";
                return false;
            }
            if (_impactAngleSlider != null)
                _impactAngleSlider.SetValueWithoutNotify(angle);

            int targetIndex = _targetDropdown != null
                ? Mathf.Clamp(_targetDropdown.value, 0, _targetDensities.Length - 1)
                : 0;
            float targetDensity = _targetDensities[targetIndex];

            parameters = new ImpactParameters(diameterM, density, speedKmS * 1000f, angle, targetDensity);
            RecalculateWeight();
            return true;
        }

        private void InitializeData() {
            _materials = new List<MaterialData> {
                new MaterialData { name = "Iron / Nickel", density = 7870f },
                new MaterialData { name = "Stone / Chondrite", density = 3000f },
                new MaterialData { name = "Carbonaceous / Porous", density = 1500f },
                new MaterialData { name = "Custom", density = 3000f }
            };

            _presets = new List<PresetData> {
                new PresetData {
                    name = "Chelyabinsk Meteor",
                    materialIndex = 1,
                    sizeValue = 20f,
                    impactSpeedKmS = 19.16f,
                    impactAngleDeg = 18f,
                    targetIndex = 0
                },
                new PresetData {
                    name = "Tunguska Event",
                    materialIndex = 1,
                    sizeValue = 50f,
                    impactSpeedKmS = 15f,
                    impactAngleDeg = 35f,
                    targetIndex = 0
                },
                new PresetData {
                    name = "Barringer Crater (Meteor Crater)",
                    materialIndex = 0,
                    sizeValue = 50f,
                    impactSpeedKmS = 12.8f,
                    impactAngleDeg = 45f,
                    targetIndex = 0
                },
                new PresetData {
                    name = "Reference case (Impact: Earth!)",
                    materialIndex = 1,
                    sizeValue = 130f,
                    impactSpeedKmS = 19f,
                    impactAngleDeg = 45f,
                    targetIndex = 0
                },
                new PresetData {
                    name = "Custom Simulation",
                    materialIndex = CUSTOM_MATERIAL_INDEX,
                    sizeValue = 100f,
                    impactSpeedKmS = 20f,
                    impactAngleDeg = 45f,
                    targetIndex = 0
                }
            };
        }

        private void PopulateDropdowns() {
            if (_presetDropdown != null) {
                _presetDropdown.ClearOptions();
                List<string> options = new List<string>();
                foreach (var p in _presets) options.Add(p.name);
                _presetDropdown.AddOptions(options);
            }

            if (_materialDropdown != null) {
                _materialDropdown.ClearOptions();
                List<string> options = new List<string>();
                foreach (var m in _materials) options.Add(m.name);
                _materialDropdown.AddOptions(options);
            }

            if (_targetDropdown != null) {
                _targetDropdown.ClearOptions();
                _targetDropdown.AddOptions(new List<string>(_targetNames));
            }
        }

        private void RegisterListeners() {
            if (_presetDropdown != null)
                _presetDropdown.onValueChanged.AddListener(OnPresetChanged);

            if (_materialDropdown != null)
                _materialDropdown.onValueChanged.AddListener(OnMaterialChanged);

            if (_sizeSlider != null)
                _sizeSlider.onValueChanged.AddListener(OnSliderSizeChanged);

            if (_sizeInputField != null)
                _sizeInputField.onEndEdit.AddListener(OnInputSizeChanged);

            if (_densityInputField != null)
                _densityInputField.onEndEdit.AddListener(OnDensityChanged);

            if (_weightInputField != null)
                _weightInputField.onEndEdit.AddListener(OnWeightChanged);

            if (_impactSpeedInputField != null)
                _impactSpeedInputField.onEndEdit.AddListener(OnSpeedChanged);

            if (_impactAngleSlider != null) {
                _impactAngleSlider.minValue = MIN_ANGLE_DEG;
                _impactAngleSlider.maxValue = MAX_ANGLE_DEG;
                _impactAngleSlider.onValueChanged.AddListener(OnSliderAngleChanged);
            }

            if (_impactAngleInputField != null)
                _impactAngleInputField.onEndEdit.AddListener(OnInputAngleChanged);

            if (_startButton != null)
                _startButton.onClick.AddListener(OnStartClicked);
        }

        private void OnPresetChanged(int index) {
            if (_isUpdatingUI) return;
            ApplyPreset(index);
        }

        private void ApplyPreset(int index) {
            if (index < 0 || index >= _presets.Count) return;
            var preset = _presets[index];

            _isUpdatingUI = true;

            if (_presetDropdown != null)
                _presetDropdown.SetValueWithoutNotify(index);

            if (_materialDropdown != null)
                _materialDropdown.SetValueWithoutNotify(preset.materialIndex);

            _lastDensity = _materials[preset.materialIndex].density;
            if (_densityInputField != null)
                _densityInputField.text = _lastDensity.ToString("F1", _ci);

            if (_targetDropdown != null)
                _targetDropdown.SetValueWithoutNotify(Mathf.Clamp(preset.targetIndex, 0, _targetDensities.Length - 1));

            if (_sizeSlider != null) {
                _sizeSlider.minValue = MIN_DIAMETER_M;
                _sizeSlider.maxValue = MAX_DIAMETER_M;
                _sizeSlider.SetValueWithoutNotify(preset.sizeValue);
            }

            if (_sizeInputField != null)
                _sizeInputField.text = FormatSize(preset.sizeValue);

            _lastSpeedKmS = Mathf.Clamp(preset.impactSpeedKmS, MIN_SPEED_KM_S, MAX_SPEED_KM_S);
            if (_impactSpeedInputField != null)
                _impactSpeedInputField.text = _lastSpeedKmS.ToString("F2", _ci);

            float clampedAngle = Mathf.Clamp(preset.impactAngleDeg, MIN_ANGLE_DEG, MAX_ANGLE_DEG);
            if (_impactAngleSlider != null) {
                _impactAngleSlider.minValue = MIN_ANGLE_DEG;
                _impactAngleSlider.maxValue = MAX_ANGLE_DEG;
                _impactAngleSlider.SetValueWithoutNotify(clampedAngle);
            }

            if (_impactAngleInputField != null)
                _impactAngleInputField.text = clampedAngle.ToString("F1", _ci);

            _isUpdatingUI = false;

            SetStatus(string.Empty);
            RecalculateWeight();
        }

        private void OnMaterialChanged(int index) {
            if (_isUpdatingUI) return;
            if (index < 0 || index >= _materials.Count) return;

            if (index < CUSTOM_MATERIAL_INDEX) {
                _lastDensity = _materials[index].density;
                _isUpdatingUI = true;
                if (_densityInputField != null)
                    _densityInputField.text = _lastDensity.ToString("F1", _ci);
                _isUpdatingUI = false;
            }

            RecalculateWeight();
        }

        private void OnDensityChanged(string text) {
            if (_isUpdatingUI) return;

            float density = _lastDensity;
            if (TryParseFloat(text, out float parsed))
                density = Mathf.Clamp(parsed, MIN_DENSITY, MAX_DENSITY);

            _lastDensity = density;
            _isUpdatingUI = true;
            if (_densityInputField != null)
                _densityInputField.text = density.ToString("F1", _ci);
            if (_materialDropdown != null)
                _materialDropdown.SetValueWithoutNotify(CUSTOM_MATERIAL_INDEX);
            _isUpdatingUI = false;

            RecalculateWeight();
        }

        private void OnSliderSizeChanged(float val) {
            if (_isUpdatingUI) return;
            _isUpdatingUI = true;
            if (_sizeInputField != null)
                _sizeInputField.text = FormatSize(val);
            _isUpdatingUI = false;
            RecalculateWeight();
        }

        private void OnInputSizeChanged(string text) {
            if (_isUpdatingUI || _sizeSlider == null) return;

            float val = _sizeSlider.value;
            if (TryParseFloat(text, out float parsed))
                val = Mathf.Clamp(parsed, _sizeSlider.minValue, _sizeSlider.maxValue);

            _isUpdatingUI = true;
            _sizeSlider.SetValueWithoutNotify(val);
            if (_sizeInputField != null)
                _sizeInputField.text = FormatSize(val);
            _isUpdatingUI = false;
            RecalculateWeight();
        }

        private void OnSpeedChanged(string text) {
            if (_isUpdatingUI) return;

            float speed = _lastSpeedKmS;
            if (TryParseFloat(text, out float parsed))
                speed = Mathf.Clamp(parsed, MIN_SPEED_KM_S, MAX_SPEED_KM_S);

            _lastSpeedKmS = speed;
            if (_impactSpeedInputField != null)
                _impactSpeedInputField.text = speed.ToString("F2", _ci);
        }

        private void OnSliderAngleChanged(float val) {
            if (_isUpdatingUI) return;
            _isUpdatingUI = true;
            if (_impactAngleInputField != null)
                _impactAngleInputField.text = val.ToString("F1", _ci);
            _isUpdatingUI = false;
        }

        private void OnInputAngleChanged(string text) {
            if (_isUpdatingUI) return;

            float val = _impactAngleSlider != null ? _impactAngleSlider.value : 45f;
            if (TryParseFloat(text, out float parsed))
                val = Mathf.Clamp(parsed, MIN_ANGLE_DEG, MAX_ANGLE_DEG);

            _isUpdatingUI = true;
            if (_impactAngleSlider != null)
                _impactAngleSlider.SetValueWithoutNotify(val);
            if (_impactAngleInputField != null)
                _impactAngleInputField.text = val.ToString("F1", _ci);
            _isUpdatingUI = false;
        }

        private void OnWeightChanged(string text) {
            if (_isUpdatingUI) return;
            if (TryParseFloat(text, out float weightKg) && weightKg > 0f) {
                double diameterMeters = GetDiameterInMeters();
                double radius = diameterMeters * 0.5;
                double volume = (4.0 / 3.0) * Math.PI * radius * radius * radius;
                float newDensity = Mathf.Clamp((float)(weightKg / volume), MIN_DENSITY, MAX_DENSITY);

                _lastDensity = newDensity;
                _isUpdatingUI = true;
                if (_densityInputField != null)
                    _densityInputField.text = newDensity.ToString("F1", _ci);
                if (_materialDropdown != null)
                    _materialDropdown.SetValueWithoutNotify(CUSTOM_MATERIAL_INDEX);
                _isUpdatingUI = false;
            }

            RecalculateWeight();
        }

        private void RecalculateWeight() {
            double diameterMeters = GetDiameterInMeters();
            float density = _lastDensity;
            if (_densityInputField != null && TryParseFloat(_densityInputField.text, out float d)) {
                density = Mathf.Clamp(d, MIN_DENSITY, MAX_DENSITY);
            }

            double radius = diameterMeters * 0.5;
            double volume = (4.0 / 3.0) * Math.PI * radius * radius * radius;
            double massKg = volume * density;

            if (_weightInputField != null) {
                _isUpdatingUI = true;

                if (massKg >= 1e9 || massKg <= 1e-3)
                    _weightInputField.text = massKg.ToString("E3", _ci);
                else
                    _weightInputField.text = massKg.ToString("F1", _ci);
                _isUpdatingUI = false;
            }
        }

        private void OnStartClicked() {
            if (!TryBuildParameters(out ImpactParameters parameters, out string error)) {
                SetStatus(error);
                return;
            }

            SimulationSettings.Current = parameters;
            SetStatus(string.Empty);
            SimulationRequested?.Invoke(parameters);
        }

        private float GetDiameterInMeters() {
            float val = 0f;
            if (_sizeInputField != null && TryParseFloat(_sizeInputField.text, out float v)) {
                val = v;
            } else if (_sizeSlider != null) {
                val = _sizeSlider.value;
            }

            return Mathf.Clamp(val, MIN_DIAMETER_M, MAX_DIAMETER_M);
        }

        private static bool ReadClamped(TMP_InputField field, float min, float max, string format, out float value) {
            value = min;
            if (field == null || !TryParseFloat(field.text, out float parsed))
                return false;

            value = Mathf.Clamp(parsed, min, max);
            field.SetTextWithoutNotify(value.ToString(format, _ci));
            return true;
        }

        private static bool TryParseFloat(string text, out float value) {
            value = 0f;
            if (string.IsNullOrWhiteSpace(text)) return false;
            return float.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, _ci, out value);
        }

        private static string FormatSize(float value) {
            return value.ToString("F1", _ci);
        }

        private void SetStatus(string message) {
            if (_statusText != null) _statusText.text = message;
        }
    }
}
