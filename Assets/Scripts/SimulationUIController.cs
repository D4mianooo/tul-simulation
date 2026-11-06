using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Simulation
{
    [System.Serializable]
    public enum SizeUnit
    {
        Meters,
        Kilometers
    }

    [System.Serializable]
    public struct PresetData
    {
        public string name;
        public int materialIndex;
        public float sizeValue;
        public SizeUnit sizeUnit;
        public float impactSpeedKmS;
        public float impactAngleDeg;
        public int targetIndex; // 0 = sedimentary rock (2500), 1 = crystalline rock (2750)
    }

    [System.Serializable]
    public struct MaterialData
    {
        public string name;
        public float density; // kg/m^3
    }

    public class SimulationUIController : MonoBehaviour
    {
        [Header("UI Controls - Dropdowns")]
        public TMP_Dropdown presetDropdown;
        public TMP_Dropdown materialDropdown;
        public TMP_Dropdown sizeUnitDropdown;
        public TMP_Dropdown targetDropdown;     

        [Header("UI Controls - Inputs & Sliders")]
        public Slider sizeSlider;
        public TMP_InputField sizeInputField;
        public TMP_Text sizeUnitLabel;

        public TMP_InputField weightInputField;
        public TMP_InputField densityInputField;
        public TMP_InputField impactSpeedInputField;
        public Slider impactAngleSlider;
        public TMP_InputField impactAngleInputField;
        public Button startButton;
        public TMP_Text statusText;             

        [Header("3D Platform")]
        public Transform platformTransform;

        public event Action<ImpactParameters> SimulationRequested;

        // Model ranges )
        private const float MinDiameterM = 20f;
        private const float MaxDiameterM = 1000f;
        private const float MinDensity = 1000f;
        private const float MaxDensity = 8000f;
        private const float MinSpeedKmS = 11f;
        private const float MaxSpeedKmS = 72f;
        private const float MinAngleDeg = 5f;
        private const float MaxAngleDeg = 90f;
        private const int CustomMaterialIndex = 3; // must be the last entry of 'materials'

        private static readonly CultureInfo CI = CultureInfo.InvariantCulture;
        private static readonly string[] TargetNames =
        {
            "Sedimentary rock (2500 kg/m3)",
            "Crystalline rock (2750 kg/m3)"
        };
        private static readonly float[] TargetDensities = { 2500f, 2750f };

        private List<MaterialData> materials = new List<MaterialData>();
        private List<PresetData> presets = new List<PresetData>();

        private bool isUpdatingUI = false;
        private SizeUnit currentUnit = SizeUnit.Meters;
        private float lastSpeedKmS = 20f;
        private float lastDensity = 3000f;

        private void Awake()
        {
            InitializeData();
            PopulateDropdowns();
            RegisterListeners();
        }

        private void Start()
        {
            // Apply first preset initially
            if (presets.Count > 0)
            {
                ApplyPreset(0);
            }
        }

        private void InitializeData()
        {
            // Only materials inside the model range (1000-8000 kg/m^3).
            // Ice/Comet (920) and Gold (19300) were removed: out of range, comets not supported.
            materials = new List<MaterialData>
            {
                new MaterialData { name = "Iron / Nickel", density = 7870f },
                new MaterialData { name = "Stone / Chondrite", density = 3000f },
                new MaterialData { name = "Carbonaceous / Porous", density = 1500f },
                new MaterialData { name = "Custom", density = 3000f }
            };

            // Simulation Presets (all inside model ranges)
            presets = new List<PresetData>
            {
                new PresetData
                {
                    name = "Chelyabinsk Meteor",
                    materialIndex = 1, // Stone
                    sizeValue = 20f,
                    sizeUnit = SizeUnit.Meters,
                    impactSpeedKmS = 19.16f,
                    impactAngleDeg = 18f,
                    targetIndex = 0
                },
                new PresetData
                {
                    name = "Tunguska Event",
                    materialIndex = 1, // Stone
                    sizeValue = 50f,
                    sizeUnit = SizeUnit.Meters,
                    impactSpeedKmS = 15f,
                    impactAngleDeg = 35f,
                    targetIndex = 0
                },
                new PresetData
                {
                    name = "Barringer Crater (Meteor Crater)",
                    materialIndex = 0, // Iron
                    sizeValue = 50f,
                    sizeUnit = SizeUnit.Meters,
                    impactSpeedKmS = 12.8f,
                    impactAngleDeg = 45f,
                    targetIndex = 0
                },
                new PresetData
                {
                    name = "Reference case (Impact: Earth!)",
                    materialIndex = 1, // Stone, 3000 kg/m^3
                    sizeValue = 130f,
                    sizeUnit = SizeUnit.Meters,
                    impactSpeedKmS = 19f,
                    impactAngleDeg = 45f,
                    targetIndex = 0    // sedimentary rock, 2500 kg/m^3
                },
                new PresetData
                {
                    name = "Custom Simulation",
                    materialIndex = CustomMaterialIndex,
                    sizeValue = 100f,
                    sizeUnit = SizeUnit.Meters,
                    impactSpeedKmS = 20f,
                    impactAngleDeg = 45f,
                    targetIndex = 0
                }
            };
            // Chicxulub (10 km) removed: above the 1000 m limit of the model.
        }

        private void PopulateDropdowns()
        {
            if (presetDropdown != null)
            {
                presetDropdown.ClearOptions();
                List<string> options = new List<string>();
                foreach (var p in presets) options.Add(p.name);
                presetDropdown.AddOptions(options);
            }

            if (materialDropdown != null)
            {
                materialDropdown.ClearOptions();
                List<string> options = new List<string>();
                foreach (var m in materials) options.Add(m.name);
                materialDropdown.AddOptions(options);
            }

            if (sizeUnitDropdown != null)
            {
                sizeUnitDropdown.ClearOptions();
                sizeUnitDropdown.AddOptions(new List<string> { "Meters (m)", "Kilometers (km)" });
            }

            if (targetDropdown != null)
            {
                targetDropdown.ClearOptions();
                targetDropdown.AddOptions(new List<string>(TargetNames));
            }
        }

        private void RegisterListeners()
        {
            if (presetDropdown != null)
                presetDropdown.onValueChanged.AddListener(OnPresetChanged);

            if (materialDropdown != null)
                materialDropdown.onValueChanged.AddListener(OnMaterialChanged);

            if (sizeUnitDropdown != null)
                sizeUnitDropdown.onValueChanged.AddListener(OnSizeUnitChanged);

            if (sizeSlider != null)
                sizeSlider.onValueChanged.AddListener(OnSliderSizeChanged);

            if (sizeInputField != null)
                sizeInputField.onEndEdit.AddListener(OnInputSizeChanged);

            if (densityInputField != null)
                densityInputField.onEndEdit.AddListener(OnDensityChanged);

            if (weightInputField != null)
                weightInputField.onEndEdit.AddListener(OnWeightChanged);

            if (impactSpeedInputField != null)
                impactSpeedInputField.onEndEdit.AddListener(OnSpeedChanged);  

            if (impactAngleSlider != null)
            {
                impactAngleSlider.minValue = MinAngleDeg;
                impactAngleSlider.maxValue = MaxAngleDeg;
                impactAngleSlider.onValueChanged.AddListener(OnSliderAngleChanged);
            }

            if (impactAngleInputField != null)
                impactAngleInputField.onEndEdit.AddListener(OnInputAngleChanged);

            if (startButton != null)
                startButton.onClick.AddListener(OnStartClicked);               
        }


        private void OnPresetChanged(int index)
        {
            if (isUpdatingUI) return;
            ApplyPreset(index);
        }

        private void ApplyPreset(int index)
        {
            if (index < 0 || index >= presets.Count) return;
            var preset = presets[index];

            isUpdatingUI = true;

            if (presetDropdown != null)
                presetDropdown.SetValueWithoutNotify(index);

            if (materialDropdown != null)
                materialDropdown.SetValueWithoutNotify(preset.materialIndex);

            lastDensity = materials[preset.materialIndex].density;
            if (densityInputField != null)
                densityInputField.text = lastDensity.ToString("F1", CI);

            if (targetDropdown != null)
                targetDropdown.SetValueWithoutNotify(Mathf.Clamp(preset.targetIndex, 0, TargetDensities.Length - 1));

            currentUnit = preset.sizeUnit;
            if (sizeUnitDropdown != null)
                sizeUnitDropdown.SetValueWithoutNotify((int)preset.sizeUnit);

            ConfigureSliderForUnit(preset.sizeUnit);

            if (sizeSlider != null)
                sizeSlider.SetValueWithoutNotify(preset.sizeValue);

            if (sizeInputField != null)
                sizeInputField.text = FormatSize(preset.sizeValue, preset.sizeUnit);

            lastSpeedKmS = Mathf.Clamp(preset.impactSpeedKmS, MinSpeedKmS, MaxSpeedKmS);
            if (impactSpeedInputField != null)
                impactSpeedInputField.text = lastSpeedKmS.ToString("F2", CI);

            float clampedAngle = Mathf.Clamp(preset.impactAngleDeg, MinAngleDeg, MaxAngleDeg);
            if (impactAngleSlider != null)
            {
                impactAngleSlider.minValue = MinAngleDeg;
                impactAngleSlider.maxValue = MaxAngleDeg;
                impactAngleSlider.SetValueWithoutNotify(clampedAngle);
            }

            if (impactAngleInputField != null)
                impactAngleInputField.text = clampedAngle.ToString("F1", CI);

            isUpdatingUI = false;

            SetStatus(string.Empty);
            RecalculateWeight();
        }


        private void OnMaterialChanged(int index)
        {
            if (isUpdatingUI) return;
            if (index < 0 || index >= materials.Count) return;

            if (index < CustomMaterialIndex) // Not custom
            {
                lastDensity = materials[index].density;
                isUpdatingUI = true;
                if (densityInputField != null)
                    densityInputField.text = lastDensity.ToString("F1", CI);
                isUpdatingUI = false;
            }

            RecalculateWeight();
        }

        private void OnDensityChanged(string text)
        {
            if (isUpdatingUI) return;

            float density = lastDensity;
            if (TryParseFloat(text, out float parsed))
                density = Mathf.Clamp(parsed, MinDensity, MaxDensity);

            lastDensity = density;
            isUpdatingUI = true;
            if (densityInputField != null)
                densityInputField.text = density.ToString("F1", CI);
            if (materialDropdown != null)
                materialDropdown.SetValueWithoutNotify(CustomMaterialIndex);
            isUpdatingUI = false;

            RecalculateWeight();
        }


        private void OnSizeUnitChanged(int index)
        {
            if (isUpdatingUI) return;

            SizeUnit newUnit = (SizeUnit)index;
            if (newUnit == currentUnit) return;

            // Convert the value instead of keeping the number (100 m -> 0.1 km)
            float valueInMeters = GetDiameterInMeters();
            currentUnit = newUnit;

            isUpdatingUI = true;
            ConfigureSliderForUnit(newUnit);

            float display = newUnit == SizeUnit.Kilometers ? valueInMeters / 1000f : valueInMeters;
            if (sizeSlider != null)
            {
                display = Mathf.Clamp(display, sizeSlider.minValue, sizeSlider.maxValue);
                sizeSlider.SetValueWithoutNotify(display);
            }
            if (sizeInputField != null)
                sizeInputField.text = FormatSize(display, newUnit);
            isUpdatingUI = false;

            RecalculateWeight();
        }

        private void ConfigureSliderForUnit(SizeUnit unit)
        {
            if (sizeSlider == null) return;

            if (unit == SizeUnit.Meters)
            {
                sizeSlider.minValue = MinDiameterM;
                sizeSlider.maxValue = MaxDiameterM;
                if (sizeUnitLabel != null) sizeUnitLabel.text = "m";
            }
            else
            {
                sizeSlider.minValue = MinDiameterM / 1000f;   // 0.02 km
                sizeSlider.maxValue = MaxDiameterM / 1000f;   // 1 km
                if (sizeUnitLabel != null) sizeUnitLabel.text = "km";
            }
        }

        private void OnSliderSizeChanged(float val)
        {
            if (isUpdatingUI) return;
            isUpdatingUI = true;
            if (sizeInputField != null)
                sizeInputField.text = FormatSize(val, currentUnit);
            isUpdatingUI = false;
            RecalculateWeight();
        }

        private void OnInputSizeChanged(string text)
        {
            if (isUpdatingUI || sizeSlider == null) return;

            float val = sizeSlider.value;
            if (TryParseFloat(text, out float parsed))
                val = Mathf.Clamp(parsed, sizeSlider.minValue, sizeSlider.maxValue);

            isUpdatingUI = true;
            sizeSlider.SetValueWithoutNotify(val);
            if (sizeInputField != null)
                sizeInputField.text = FormatSize(val, currentUnit);
            isUpdatingUI = false;
            RecalculateWeight();
        }


        private void OnSpeedChanged(string text)
        {
            if (isUpdatingUI) return;

            float speed = lastSpeedKmS;
            if (TryParseFloat(text, out float parsed))
                speed = Mathf.Clamp(parsed, MinSpeedKmS, MaxSpeedKmS);

            lastSpeedKmS = speed;
            if (impactSpeedInputField != null)
                impactSpeedInputField.text = speed.ToString("F2", CI);
        }


        private void OnSliderAngleChanged(float val)
        {
            if (isUpdatingUI) return;
            isUpdatingUI = true;
            if (impactAngleInputField != null)
                impactAngleInputField.text = val.ToString("F1", CI);
            isUpdatingUI = false;
        }

        private void OnInputAngleChanged(string text)
        {
            if (isUpdatingUI) return;

            float val = impactAngleSlider != null ? impactAngleSlider.value : 45f;
            if (TryParseFloat(text, out float parsed))
                val = Mathf.Clamp(parsed, MinAngleDeg, MaxAngleDeg);

            isUpdatingUI = true;
            if (impactAngleSlider != null)
                impactAngleSlider.SetValueWithoutNotify(val);
            if (impactAngleInputField != null)
                impactAngleInputField.text = val.ToString("F1", CI);
            isUpdatingUI = false;
        }


        private void OnWeightChanged(string text)
        {
            if (isUpdatingUI) return;
            if (TryParseFloat(text, out float weightKg) && weightKg > 0f)
            {
                double diameterMeters = GetDiameterInMeters();
                double radius = diameterMeters * 0.5;
                double volume = (4.0 / 3.0) * Math.PI * radius * radius * radius;
                float newDensity = Mathf.Clamp((float)(weightKg / volume), MinDensity, MaxDensity);

                lastDensity = newDensity;
                isUpdatingUI = true;
                if (densityInputField != null)
                    densityInputField.text = newDensity.ToString("F1", CI);
                if (materialDropdown != null)
                    materialDropdown.SetValueWithoutNotify(CustomMaterialIndex);
                isUpdatingUI = false;
            }

            RecalculateWeight();
        }

        private void RecalculateWeight()
        {
            double diameterMeters = GetDiameterInMeters();
            float density = lastDensity;
            if (densityInputField != null && TryParseFloat(densityInputField.text, out float d))
            {
                density = Mathf.Clamp(d, MinDensity, MaxDensity);
            }

            double radius = diameterMeters * 0.5;
            double volume = (4.0 / 3.0) * Math.PI * radius * radius * radius;
            double massKg = volume * density;

            if (weightInputField != null)
            {
                isUpdatingUI = true;
                // Scientific notation for very large values, plain number otherwise
                if (massKg >= 1e9 || massKg <= 1e-3)
                    weightInputField.text = massKg.ToString("E3", CI);
                else
                    weightInputField.text = massKg.ToString("F1", CI);
                isUpdatingUI = false;
            }
        }


        private void OnStartClicked()
        {
            if (!TryBuildParameters(out ImpactParameters parameters, out string error))
            {
                SetStatus(error);
                return;
            }

            SimulationSettings.Current = parameters;
            SetStatus(string.Empty);
            SimulationRequested?.Invoke(parameters);
        }

        /// Reads all fields, clamps them to the model ranges, writes the normalized values
        /// back to the UI and converts units (size -> m, speed km/s -> m/s).
        public bool TryBuildParameters(out ImpactParameters parameters, out string error)
        {
            parameters = default;
            error = null;

            if (sizeSlider == null)
            {
                error = "Size slider is not assigned.";
                return false;
            }

            // Size (in the currently selected unit)
            if (!ReadClamped(sizeInputField, sizeSlider.minValue, sizeSlider.maxValue,
                             currentUnit == SizeUnit.Kilometers ? "F3" : "F1", out float sizeValue))
            {
                error = "Invalid impactor size.";
                return false;
            }
            sizeSlider.SetValueWithoutNotify(sizeValue);
            float diameterM = currentUnit == SizeUnit.Kilometers ? sizeValue * 1000f : sizeValue;

            // Impactor density
            if (!ReadClamped(densityInputField, MinDensity, MaxDensity, "F1", out float density))
            {
                error = "Invalid impactor density.";
                return false;
            }
            lastDensity = density;

            // Speed (km/s)
            if (!ReadClamped(impactSpeedInputField, MinSpeedKmS, MaxSpeedKmS, "F2", out float speedKmS))
            {
                error = "Invalid impact speed.";
                return false;
            }
            lastSpeedKmS = speedKmS;

            // Angle (degrees)
            if (!ReadClamped(impactAngleInputField, MinAngleDeg, MaxAngleDeg, "F1", out float angle))
            {
                error = "Invalid impact angle.";
                return false;
            }
            if (impactAngleSlider != null)
                impactAngleSlider.SetValueWithoutNotify(angle);

            // Target (ground) density
            int targetIndex = targetDropdown != null
                ? Mathf.Clamp(targetDropdown.value, 0, TargetDensities.Length - 1)
                : 0;
            float targetDensity = TargetDensities[targetIndex];

            parameters = new ImpactParameters(diameterM, density, speedKmS * 1000f, angle, targetDensity);
            RecalculateWeight();
            return true;
        }

        // ---------------------------------------------------------------- helpers

        private float GetDiameterInMeters()
        {
            float val = 0f;
            if (sizeInputField != null && TryParseFloat(sizeInputField.text, out float v))
            {
                val = v;
            }
            else if (sizeSlider != null)
            {
                val = sizeSlider.value;
            }

            if (currentUnit == SizeUnit.Kilometers)
            {
                val *= 1000f;
            }
            return Mathf.Clamp(val, MinDiameterM, MaxDiameterM);
        }

        private static bool ReadClamped(TMP_InputField field, float min, float max, string format, out float value)
        {
            value = min;
            if (field == null || !TryParseFloat(field.text, out float parsed))
                return false;

            value = Mathf.Clamp(parsed, min, max);
            field.SetTextWithoutNotify(value.ToString(format, CI));
            return true;
        }

        // Accepts both "12.5" and "12,5" and exponent notation (e.g. 1.571E+009)
        private static bool TryParseFloat(string text, out float value)
        {
            value = 0f;
            if (string.IsNullOrWhiteSpace(text)) return false;
            return float.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CI, out value);
        }

        private static string FormatSize(float value, SizeUnit unit)
        {
            return value.ToString(unit == SizeUnit.Kilometers ? "F3" : "F1", CI);
        }

        private void SetStatus(string message)
        {
            if (statusText != null) statusText.text = message;
        }
    }
}