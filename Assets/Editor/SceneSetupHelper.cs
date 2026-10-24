using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Simulation;

namespace Simulation.Editor
{
    public static class SceneSetupHelper
    {
        [MenuItem("Simulation/Build SampleScene Simulation Setup")]
        public static void BuildSetup()
        {
            // 1. Ensure 3D Plane Platform exists
            GameObject platform = GameObject.Find("Platform");
            if (platform == null)
            {
                platform = GameObject.CreatePrimitive(PrimitiveType.Plane);
                platform.name = "Platform";
                platform.transform.position = Vector3.zero;
                platform.transform.localScale = new Vector3(10, 1, 10); // 100m x 100m plane
            }

            // 2. Ensure EventSystem exists
            EventSystem es = Object.FindFirstObjectByType<EventSystem>();
            if (es == null)
            {
                GameObject esObj = new GameObject("EventSystem");
                esObj.AddComponent<EventSystem>();
                esObj.AddComponent<StandaloneInputModule>();
            }

            // 3. Canvas setup
            GameObject canvasObj = GameObject.Find("SimulationCanvas");
            if (canvasObj != null)
            {
                Object.DestroyImmediate(canvasObj);
            }

            canvasObj = new GameObject("SimulationCanvas");
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObj.AddComponent<GraphicRaycaster>();

            SimulationUIController controller = canvasObj.AddComponent<SimulationUIController>();
            controller.platformTransform = platform.transform;

            Font defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (defaultFont == null)
            {
                defaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }

            // Panel Root (Anchored top-left with responsive bounds)
            GameObject panelObj = new GameObject("ControlPanel");
            panelObj.transform.SetParent(canvasObj.transform, false);
            RectTransform panelRect = panelObj.AddComponent<RectTransform>();
            // Anchor to top-left with fixed offset, but constrained so it never overflows screen height
            panelRect.anchorMin = new Vector2(0, 0);
            panelRect.anchorMax = new Vector2(0, 1);
            panelRect.pivot = new Vector2(0, 1);
            // Left margin 30, top margin 30, bottom margin 30, width 380
            panelRect.offsetMin = new Vector2(30, 30);
            panelRect.offsetMax = new Vector2(410, -30);

            Image panelBg = panelObj.AddComponent<Image>();
            panelBg.color = new Color(0.10f, 0.12f, 0.16f, 0.95f);

            // Add ScrollRect on Panel to guarantee fit on any resolution / aspect ratio
            ScrollRect panelScroll = panelObj.AddComponent<ScrollRect>();
            panelScroll.horizontal = false;
            panelScroll.vertical = true;
            panelScroll.scrollSensitivity = 25f;

            // Viewport
            GameObject viewportObj = new GameObject("Viewport");
            viewportObj.transform.SetParent(panelObj.transform, false);
            RectTransform vpRect = viewportObj.AddComponent<RectTransform>();
            vpRect.anchorMin = Vector2.zero;
            vpRect.anchorMax = Vector2.one;
            vpRect.sizeDelta = Vector2.zero;
            vpRect.pivot = new Vector2(0, 1);
            Mask vpMask = viewportObj.AddComponent<Mask>();
            vpMask.showMaskGraphic = false;
            Image vpImg = viewportObj.AddComponent<Image>();
            vpImg.color = Color.white;
            panelScroll.viewport = vpRect;

            // Content container inside Viewport
            GameObject contentObj = new GameObject("Content");
            contentObj.transform.SetParent(viewportObj.transform, false);
            RectTransform contentRect = contentObj.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0, 1);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(0, 0);
            panelScroll.content = contentRect;

            VerticalLayoutGroup vlg = contentObj.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(18, 18, 18, 18);
            vlg.spacing = 8;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            ContentSizeFitter csf = contentObj.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            // Helper methods for UI creation
            Text CreateLabel(Transform parent, string text, int fontSize = 12, FontStyle style = FontStyle.Normal)
            {
                GameObject obj = new GameObject("Label_" + text);
                obj.transform.SetParent(parent, false);
                Text t = obj.AddComponent<Text>();
                t.font = defaultFont;
                t.text = text;
                t.fontSize = fontSize;
                t.fontStyle = style;
                t.color = new Color(0.85f, 0.88f, 0.93f, 1f);
                t.alignment = TextAnchor.MiddleLeft;
                LayoutElement le = obj.AddComponent<LayoutElement>();
                le.preferredHeight = 18;
                return t;
            }

            Dropdown CreateDropdown(Transform parent, string name)
            {
                GameObject ddObj = new GameObject(name);
                ddObj.transform.SetParent(parent, false);
                Image img = ddObj.AddComponent<Image>();
                img.color = new Color(0.18f, 0.20f, 0.25f, 1f);
                Dropdown dd = ddObj.AddComponent<Dropdown>();

                GameObject captionObj = new GameObject("Label");
                captionObj.transform.SetParent(ddObj.transform, false);
                RectTransform capRect = captionObj.AddComponent<RectTransform>();
                capRect.anchorMin = Vector2.zero;
                capRect.anchorMax = Vector2.one;
                capRect.offsetMin = new Vector2(10, 0);
                capRect.offsetMax = new Vector2(-28, 0);
                Text capText = captionObj.AddComponent<Text>();
                capText.font = defaultFont;
                capText.fontSize = 12;
                capText.alignment = TextAnchor.MiddleLeft;
                capText.color = Color.white;
                dd.captionText = capText;

                // Arrow indicator
                GameObject arrowObj = new GameObject("Arrow");
                arrowObj.transform.SetParent(ddObj.transform, false);
                RectTransform arrRect = arrowObj.AddComponent<RectTransform>();
                arrRect.anchorMin = new Vector2(1, 0.5f);
                arrRect.anchorMax = new Vector2(1, 0.5f);
                arrRect.pivot = new Vector2(1, 0.5f);
                arrRect.anchoredPosition = new Vector2(-8, 0);
                arrRect.sizeDelta = new Vector2(16, 16);
                Text arrText = arrowObj.AddComponent<Text>();
                arrText.font = defaultFont;
                arrText.text = "▼";
                arrText.fontSize = 10;
                arrText.alignment = TextAnchor.MiddleCenter;
                arrText.color = new Color(0.7f, 0.7f, 0.7f, 1f);

                // Template for dropdown
                GameObject template = new GameObject("Template");
                template.transform.SetParent(ddObj.transform, false);
                RectTransform tRect = template.AddComponent<RectTransform>();
                tRect.anchorMin = new Vector2(0, 0);
                tRect.anchorMax = new Vector2(1, 0);
                tRect.pivot = new Vector2(0.5f, 1f);
                tRect.anchoredPosition = new Vector2(0, -2);
                tRect.sizeDelta = new Vector2(0, 140);
                Image tImg = template.AddComponent<Image>();
                tImg.color = new Color(0.14f, 0.16f, 0.20f, 1f);
                ScrollRect sr = template.AddComponent<ScrollRect>();

                GameObject viewport = new GameObject("Viewport");
                viewport.transform.SetParent(template.transform, false);
                RectTransform vpRectD = viewport.AddComponent<RectTransform>();
                vpRectD.anchorMin = Vector2.zero;
                vpRectD.anchorMax = Vector2.one;
                viewport.AddComponent<Mask>().showMaskGraphic = false;
                viewport.AddComponent<Image>().color = Color.white;

                GameObject content = new GameObject("Content");
                content.transform.SetParent(viewport.transform, false);
                RectTransform cRect = content.AddComponent<RectTransform>();
                cRect.anchorMin = new Vector2(0, 1);
                cRect.anchorMax = new Vector2(1, 1);
                cRect.pivot = new Vector2(0.5f, 1f);
                cRect.sizeDelta = new Vector2(0, 26);

                GameObject item = new GameObject("Item");
                item.transform.SetParent(content.transform, false);
                RectTransform itemRect = item.AddComponent<RectTransform>();
                itemRect.anchorMin = new Vector2(0, 0.5f);
                itemRect.anchorMax = new Vector2(1, 0.5f);
                itemRect.sizeDelta = new Vector2(0, 24);
                item.AddComponent<Toggle>();

                GameObject itemLabel = new GameObject("Item Label");
                itemLabel.transform.SetParent(item.transform, false);
                RectTransform ilRect = itemLabel.AddComponent<RectTransform>();
                ilRect.anchorMin = Vector2.zero;
                ilRect.anchorMax = Vector2.one;
                ilRect.offsetMin = new Vector2(10, 0);
                ilRect.offsetMax = new Vector2(-10, 0);
                Text ilText = itemLabel.AddComponent<Text>();
                ilText.font = defaultFont;
                ilText.fontSize = 12;
                ilText.alignment = TextAnchor.MiddleLeft;
                ilText.color = Color.white;

                dd.itemText = ilText;
                dd.template = tRect;
                template.SetActive(false);

                LayoutElement le = ddObj.AddComponent<LayoutElement>();
                le.preferredHeight = 28;

                return dd;
            }

            InputField CreateInputField(Transform parent, string name, string defaultPlaceholder)
            {
                GameObject ifObj = new GameObject(name);
                ifObj.transform.SetParent(parent, false);
                Image img = ifObj.AddComponent<Image>();
                img.color = new Color(0.18f, 0.20f, 0.25f, 1f);
                InputField inf = ifObj.AddComponent<InputField>();

                GameObject phObj = new GameObject("Placeholder");
                phObj.transform.SetParent(ifObj.transform, false);
                RectTransform phRect = phObj.AddComponent<RectTransform>();
                phRect.anchorMin = Vector2.zero;
                phRect.anchorMax = Vector2.one;
                phRect.offsetMin = new Vector2(10, 0);
                phRect.offsetMax = new Vector2(-10, 0);
                Text phText = phObj.AddComponent<Text>();
                phText.font = defaultFont;
                phText.fontSize = 12;
                phText.alignment = TextAnchor.MiddleLeft;
                phText.color = new Color(0.6f, 0.6f, 0.6f, 0.6f);
                phText.text = defaultPlaceholder;
                inf.placeholder = phText;

                GameObject tObj = new GameObject("Text");
                tObj.transform.SetParent(ifObj.transform, false);
                RectTransform tRect = tObj.AddComponent<RectTransform>();
                tRect.anchorMin = Vector2.zero;
                tRect.anchorMax = Vector2.one;
                tRect.offsetMin = new Vector2(10, 0);
                tRect.offsetMax = new Vector2(-10, 0);
                Text tText = tObj.AddComponent<Text>();
                tText.font = defaultFont;
                tText.fontSize = 12;
                tText.alignment = TextAnchor.MiddleLeft;
                tText.color = Color.white;
                inf.textComponent = tText;

                LayoutElement le = ifObj.AddComponent<LayoutElement>();
                le.preferredHeight = 28;

                return inf;
            }

            Transform container = contentObj.transform;

            // Title
            CreateLabel(container, "IMPACT SIMULATION SETUP", 15, FontStyle.Bold);

            // 1. Presets Combobox
            CreateLabel(container, "Simulation Preset");
            controller.presetDropdown = CreateDropdown(container, "PresetDropdown");

            // 2. Material Combobox
            CreateLabel(container, "Material");
            controller.materialDropdown = CreateDropdown(container, "MaterialDropdown");

            // 3. Density InputField
            CreateLabel(container, "Density (kg/m³)");
            controller.densityInputField = CreateInputField(container, "DensityInput", "Enter density...");

            // 4. Size Row: Unit Dropdown + Slider + InputField
            CreateLabel(container, "Size / Diameter");
            
            // Size Unit Dropdown
            controller.sizeUnitDropdown = CreateDropdown(container, "SizeUnitDropdown");

            // Slider & Input side-by-side
            GameObject sizeRow = new GameObject("SizeRow");
            sizeRow.transform.SetParent(container, false);
            HorizontalLayoutGroup hlg = sizeRow.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            LayoutElement sizeRowLe = sizeRow.AddComponent<LayoutElement>();
            sizeRowLe.preferredHeight = 28;

            // Slider
            GameObject sliderObj = new GameObject("SizeSlider");
            sliderObj.transform.SetParent(sizeRow.transform, false);
            LayoutElement sLe = sliderObj.AddComponent<LayoutElement>();
            sLe.flexibleWidth = 1f;

            Slider slider = sliderObj.AddComponent<Slider>();
            slider.minValue = 1f;
            slider.maxValue = 1000f;

            // Background
            GameObject sBg = new GameObject("Background");
            sBg.transform.SetParent(sliderObj.transform, false);
            RectTransform sBgRect = sBg.AddComponent<RectTransform>();
            sBgRect.anchorMin = new Vector2(0, 0.35f);
            sBgRect.anchorMax = new Vector2(1, 0.65f);
            sBgRect.offsetMin = Vector2.zero;
            sBgRect.offsetMax = Vector2.zero;
            Image sBgImg = sBg.AddComponent<Image>();
            sBgImg.color = new Color(0.22f, 0.25f, 0.32f, 1f);

            // Fill area
            GameObject fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(sliderObj.transform, false);
            RectTransform faRect = fillArea.AddComponent<RectTransform>();
            faRect.anchorMin = new Vector2(0, 0.35f);
            faRect.anchorMax = new Vector2(1, 0.65f);
            faRect.offsetMin = new Vector2(5, 0);
            faRect.offsetMax = new Vector2(-5, 0);

            GameObject fill = new GameObject("Fill");
            fill.transform.SetParent(fillArea.transform, false);
            RectTransform fillRect = fill.AddComponent<RectTransform>();
            fillRect.sizeDelta = Vector2.zero;
            Image fillImg = fill.AddComponent<Image>();
            fillImg.color = new Color(0.2f, 0.6f, 1f, 1f);
            slider.fillRect = fillRect;

            // Handle area
            GameObject handleArea = new GameObject("Handle Slide Area");
            handleArea.transform.SetParent(sliderObj.transform, false);
            RectTransform haRect = handleArea.AddComponent<RectTransform>();
            haRect.anchorMin = Vector2.zero;
            haRect.anchorMax = Vector2.one;
            haRect.offsetMin = new Vector2(8, 0);
            haRect.offsetMax = new Vector2(-8, 0);

            GameObject handle = new GameObject("Handle");
            handle.transform.SetParent(handleArea.transform, false);
            RectTransform hRect = handle.AddComponent<RectTransform>();
            hRect.sizeDelta = new Vector2(16, 16);
            Image hImg = handle.AddComponent<Image>();
            hImg.color = Color.white;
            slider.handleRect = hRect;
            slider.targetGraphic = hImg;

            controller.sizeSlider = slider;

            // Size Input Field
            controller.sizeInputField = CreateInputField(sizeRow.transform, "SizeInput", "Value");
            LayoutElement ifLe = controller.sizeInputField.GetComponent<LayoutElement>();
            ifLe.preferredWidth = 80;

            // 5. Weight InputField
            CreateLabel(container, "Calculated Weight / Mass (kg)");
            controller.weightInputField = CreateInputField(container, "WeightInput", "Calculated mass...");

            // 6. Impact Speed (km/s)
            CreateLabel(container, "Impact Speed (km/s)");
            controller.impactSpeedInputField = CreateInputField(container, "SpeedInput", "e.g. 20.0");

            // 7. Impact Angle (degrees)
            CreateLabel(container, "Impact Angle (degrees: 0° - 90°)");
            controller.impactAngleInputField = CreateInputField(container, "AngleInput", "e.g. 45.0");

            // Save scene and mark dirty
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            Debug.Log("[SceneSetupHelper] SampleScene Simulation UI & Platform setup built successfully!");
        }
    }
}
