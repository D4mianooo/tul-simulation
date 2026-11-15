using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Simulation
{
    /// Panel, który w spoczynku pokazuje tylko nagłówek, a po najechaniu myszką płynnie się rozwija.
    /// Kliknięcie nagłówka przypina panel (zostaje rozwinięty), ponowne kliknięcie odpina.
    /// Treść ma stałą wysokość i jest przycinana (RectMask2D), więc np. wykres nie przelicza się przy animacji.
    [RequireComponent(typeof(RectTransform))]
    public class CollapsiblePanel : MonoBehaviour
    {
        public float headerHeight = 34f;
        public float contentHeight = 300f;
        public bool pinned;
        [Tooltip("Ile sekund panel czeka po zjechaniu myszką, zanim się zwinie.")]
        public float closeDelay = 0.35f;
        public float speed = 6f;

        public RectTransform Content { get; private set; }
        public float CurrentHeight { get; private set; }
        public bool IsOpen => openAmount > 0.5f;

        private RectTransform rect;
        private CanvasGroup contentGroup;
        private Text headerText;
        private Text pinText;
        private string title = "";
        private float openAmount;
        private float lastHover = -10f;

        /// Buduje nagłówek i kontener treści. Wywołać raz po utworzeniu panelu.
        public void Build(Font font, string headerTitle, float contentH, Color headerColor)
        {
            rect = (RectTransform)transform;
            contentHeight = contentH;
            gameObject.AddComponent<RectMask2D>();

            // Nagłówek: cały pasek jest przyciskiem przypinającym.
            var header = new GameObject("Header", typeof(RectTransform));
            var hrt = (RectTransform)header.transform;
            hrt.SetParent(rect, false);
            hrt.anchorMin = new Vector2(0, 1);
            hrt.anchorMax = new Vector2(1, 1);
            hrt.pivot = new Vector2(0.5f, 1);
            hrt.offsetMin = new Vector2(0, -headerHeight);
            hrt.offsetMax = Vector2.zero;
            var img = header.AddComponent<Image>();
            img.color = headerColor;
            var button = header.AddComponent<Button>();
            button.targetGraphic = img;
            button.onClick.AddListener(TogglePin);

            headerText = MakeText(hrt, font, 16, TextAnchor.MiddleLeft,
                new Vector2(0, 0), new Vector2(1, 1), new Vector2(12, 0), new Vector2(-135, 0));
            pinText = MakeText(hrt, font, 13, TextAnchor.MiddleRight,
                new Vector2(1, 0), new Vector2(1, 1), new Vector2(-130, 0), new Vector2(-10, 0));
            pinText.color = new Color(0.7f, 0.75f, 0.8f);

            // Treść pod nagłówkiem, stałej wysokości.
            var content = new GameObject("Content", typeof(RectTransform));
            Content = (RectTransform)content.transform;
            Content.SetParent(rect, false);
            Content.anchorMin = new Vector2(0, 1);
            Content.anchorMax = new Vector2(1, 1);
            Content.pivot = new Vector2(0.5f, 1);
            Content.offsetMin = new Vector2(0, -headerHeight - contentHeight);
            Content.offsetMax = new Vector2(0, -headerHeight);
            contentGroup = content.AddComponent<CanvasGroup>();

            SetTitle(headerTitle);
            Apply(pinned ? 1f : 0f);
            openAmount = pinned ? 1f : 0f;
        }

        public void SetTitle(string text)
        {
            title = text;
            RefreshHeader();
        }

        public void TogglePin()
        {
            pinned = !pinned;
            RefreshHeader();
        }

        private void RefreshHeader()
        {
            if (headerText == null) return;
            headerText.text = (IsOpen || pinned ? "▼ " : "► ") + title;
            pinText.text = pinned ? "przypięty ✕" : "kliknij: przypnij";
        }

        private void Update()
        {
            if (rect == null) return;

            bool hover = false;
            var mouse = Mouse.current;
            if (mouse != null)
                hover = RectTransformUtility.RectangleContainsScreenPoint(rect, mouse.position.ReadValue(), null);
            if (hover) lastHover = Time.unscaledTime;

            bool wantOpen = pinned || hover || Time.unscaledTime - lastHover < closeDelay;
            bool wasOpen = IsOpen;
            openAmount = Mathf.MoveTowards(openAmount, wantOpen ? 1f : 0f, Time.unscaledDeltaTime * speed);
            Apply(openAmount);
            if (wasOpen != IsOpen) RefreshHeader();
        }

        private void Apply(float amount)
        {
            float e = amount * amount * (3f - 2f * amount);   // płynne przejście (smoothstep)
            CurrentHeight = headerHeight + contentHeight * e;
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, CurrentHeight);
            if (contentGroup != null)
            {
                contentGroup.alpha = e;
                contentGroup.interactable = e > 0.5f;
                contentGroup.blocksRaycasts = e > 0.5f;
            }
        }

        private static Text MakeText(RectTransform parent, Font font, int size, TextAnchor anchor,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 offMin, Vector2 offMax)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offMin;
            rt.offsetMax = offMax;
            var t = go.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = new Color(0.94f, 0.95f, 0.97f);
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.raycastTarget = false;
            return t;
        }
    }
}
