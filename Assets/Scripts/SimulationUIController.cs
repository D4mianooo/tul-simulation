using System;
using System.Collections.Generic;
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

        [Header("3D Platform")]
        public Transform platformTransform;

        private List<MaterialData> materials = new List<MaterialData>();
        private List<PresetData> presets = new List<PresetData>();

        private bool isUpdatingUI = false;

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
            // Material presets with standard densities (kg/m^3)
            materials = new List<MaterialData>
            {
                new MaterialData { name = "Iron / Nickel", density = 7870f },
                new MaterialData { name = "Stone / Chondrite", density = 3000f },
                new MaterialData { name = "Carbonaceous / Porous", density = 1500f },
                new MaterialData { name = "Ice / Comet", density = 920f },
                new MaterialData { name = "Gold / Dense Metal", density = 19300f },
                new MaterialData { name = "Custom", density = 3000f }
            };

            // Simulation Presets
            presets = new List<PresetData>
            {
                new PresetData
                {
                    name = "Chelyabinsk Meteor",
                    materialIndex = 1, // Stone
                    sizeValue = 20f,
                    sizeUnit = SizeUnit.Meters,
                    impactSpeedKmS = 19.16f,
                    impactAngleDeg = 18f
                },
                new PresetData
                {
                    name = "Tunguska Event",
                    materialIndex = 1, // Stone
                    sizeValue = 50f,
                    sizeUnit = SizeUnit.Meters,
                    impactSpeedKmS = 15f,
                    impactAngleDeg = 35f
                },
                new PresetData
                {
                    name = "Barringer Crater (Meteor Crater)",
                    materialIndex = 0, // Iron
                    sizeValue = 50f,
                    sizeUnit = SizeUnit.Meters,
                    impactSpeedKmS = 12.8f,
                    impactAngleDeg = 45f
                },
                new PresetData
                {
                    name = "Chicxulub (Dinosaur Extinction)",
                    materialIndex = 1, // Stone
                    sizeValue = 10f,
                    sizeUnit = SizeUnit.Kilometers,
                    impactSpeedKmS = 20f,
                    impactAngleDeg = 60f
                },
                new PresetData
                {
                    name = "Custom Simulation",
                    materialIndex = 5, // Custom
                    sizeValue = 100f,
                    sizeUnit = SizeUnit.Meters,
                    impactSpeedKmS = 20f,
                    impactAngleDeg = 45f
                }
            };
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

            if (impactAngleSlider != null)
            {
                impactAngleSlider.minValue = 5f;
                impactAngleSlider.maxValue = 90f;
                impactAngleSlider.onValueChanged.AddListener(OnSliderAngleChanged);
            }

            if (impactAngleInputField != null)
                impactAngleInputField.onEndEdit.AddListener(OnInputAngleChanged);
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
                presetDropdown.value = index;

            if (materialDropdown != null)
                materialDropdown.value = preset.materialIndex;

            float density = materials[preset.materialIndex].density;
            if (densityInputField != null)
                densityInputField.text = density.ToString("F1");

            if (sizeUnitDropdown != null)
                sizeUnitDropdown.value = (int)preset.sizeUnit;

            ConfigureSliderForUnit(preset.sizeUnit);

            if (sizeSlider != null)
                sizeSlider.value = preset.sizeValue;

            if (sizeInputField != null)
                sizeInputField.text = preset.sizeValue.ToString("F1");

            if (impactSpeedInputField != null)
                impactSpeedInputField.text = preset.impactSpeedKmS.ToString("F2");

            float clampedAngle = Mathf.Clamp(preset.impactAngleDeg, 5f, 90f);
            if (impactAngleSlider != null)
            {
                impactAngleSlider.minValue = 5f;
                impactAngleSlider.maxValue = 90f;
                impactAngleSlider.value = clampedAngle;
            }

            if (impactAngleInputField != null)
                impactAngleInputField.text = clampedAngle.ToString("F1");

            isUpdatingUI = false;

            RecalculateWeight();
        }

        private void OnMaterialChanged(int index)
        {
            if (isUpdatingUI) return;
            if (index < 0 || index >= materials.Count) return;

            if (index < materials.Count - 1) // Not custom
            {
                isUpdatingUI = true;
                if (densityInputField != null)
                    densityInputField.text = materials[index].density.ToString("F1");
                isUpdatingUI = false;
            }

            RecalculateWeight();
        }

        private void OnSizeUnitChanged(int index)
        {
            SizeUnit unit = (SizeUnit)index;
            ConfigureSliderForUnit(unit);

            // Re-sync slider and inputfield
            if (float.TryParse(sizeInputField.text, out float currentVal))
            {
                if (sizeSlider != null)
                    sizeSlider.value = Mathf.Clamp(currentVal, sizeSlider.minValue, sizeSlider.maxValue);
            }
            RecalculateWeight();
        }

        private void ConfigureSliderForUnit(SizeUnit unit)
        {
            if (sizeSlider == null) return;

            if (unit == SizeUnit.Meters)
            {
                sizeSlider.minValue = 1f;
                sizeSlider.maxValue = 1000f;
                if (sizeUnitLabel != null) sizeUnitLabel.text = "m";
            }
            else
            {
                sizeSlider.minValue = 0.01f;
                sizeSlider.maxValue = 100f;
                if (sizeUnitLabel != null) sizeUnitLabel.text = "km";
            }
        }

        private void OnSliderSizeChanged(float val)
        {
            if (isUpdatingUI) return;
            isUpdatingUI = true;
            if (sizeInputField != null)
                sizeInputField.text = val.ToString("F1");
            isUpdatingUI = false;
            RecalculateWeight();
        }

        private void OnInputSizeChanged(string text)
        {
            if (isUpdatingUI) return;
            if (float.TryParse(text, out float val))
            {
                isUpdatingUI = true;
                if (sizeSlider != null)
                {
                    sizeSlider.value = Mathf.Clamp(val, sizeSlider.minValue, sizeSlider.maxValue);
                }
                isUpdatingUI = false;
                RecalculateWeight();
            }
        }

        private void OnSliderAngleChanged(float val)
        {
            if (isUpdatingUI) return;
            isUpdatingUI = true;
            if (impactAngleInputField != null)
                impactAngleInputField.text = val.ToString("F1");
            isUpdatingUI = false;
        }

        private void OnInputAngleChanged(string text)
        {
            if (isUpdatingUI) return;
            if (float.TryParse(text, out float val))
            {
                val = Mathf.Clamp(val, 5f, 90f);
                isUpdatingUI = true;
                if (impactAngleSlider != null)
                {
                    impactAngleSlider.value = Mathf.Clamp(val, impactAngleSlider.minValue, impactAngleSlider.maxValue);
                }
                if (impactAngleInputField != null)
                {
                    impactAngleInputField.text = val.ToString("F1");
                }
                isUpdatingUI = false;
            }
        }

        private void OnDensityChanged(string text)
        {
            if (isUpdatingUI) return;
            // When density is manually changed, switch material dropdown to Custom
            if (materialDropdown != null)
            {
                isUpdatingUI = true;
                materialDropdown.value = materials.Count - 1; // Custom
                isUpdatingUI = false;
            }
            RecalculateWeight();
        }

        private void OnWeightChanged(string text)
        {
            if (isUpdatingUI) return;
            // Back-calculate density from entered weight
            if (float.TryParse(text, out float weightKg) && weightKg > 0)
            {
                float diameterMeters = GetDiameterInMeters();
                if (diameterMeters > 0)
                {
                    float radius = diameterMeters * 0.5f;
                    float volume = (4f / 3f) * Mathf.PI * Mathf.Pow(radius, 3);
                    float newDensity = weightKg / volume;

                    isUpdatingUI = true;
                    if (densityInputField != null)
                        densityInputField.text = newDensity.ToString("F1");
                    if (materialDropdown != null)
                        materialDropdown.value = materials.Count - 1; // Custom
                    isUpdatingUI = false;
                }
            }
        }

        private float GetDiameterInMeters()
        {
            float val = 0f;
            if (sizeInputField != null && float.TryParse(sizeInputField.text, out float v))
            {
                val = v;
            }
            else if (sizeSlider != null)
            {
                val = sizeSlider.value;
            }

            SizeUnit unit = sizeUnitDropdown != null ? (SizeUnit)sizeUnitDropdown.value : SizeUnit.Meters;
            if (unit == SizeUnit.Kilometers)
            {
                val *= 1000f;
            }
            return val;
        }

        private void RecalculateWeight()
        {
            float diameterMeters = GetDiameterInMeters();
            float density = 3000f;
            if (densityInputField != null && float.TryParse(densityInputField.text, out float d))
            {
                density = d;
            }

            float radius = diameterMeters * 0.5f;
            float volume = (4f / 3f) * Mathf.PI * Mathf.Pow(radius, 3);
            double massKg = (double)volume * density;

            if (weightInputField != null)
            {
                isUpdatingUI = true;
                // Format mass appropriately (scientific notation if very large)
                if (massKg >= 1e9 || massKg <= 1e-3)
                    weightInputField.text = massKg.ToString("E3");
                else
                    weightInputField.text = massKg.ToString("N1");
                isUpdatingUI = false;
            }
        }
    }
}
