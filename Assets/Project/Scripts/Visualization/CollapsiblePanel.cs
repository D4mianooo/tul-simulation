using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Simulation.Visualization {
    /// <summary>
    /// A UI panel that collapses to its header when idle and expands on hover or when pinned.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class CollapsiblePanel : MonoBehaviour {
        [SerializeField]
        private float _headerHeight = 34f;
        [SerializeField]
        private float _contentHeight = 300f;
        [SerializeField]
        private bool _pinned;
        [SerializeField]
        private float _closeDelay = 0.35f;
        [SerializeField]
        private float _speed = 6f;

        private RectTransform _rect;
        private CanvasGroup _contentGroup;
        private Text _headerText;
        private Text _pinText;
        private string _title = "";
        private float _openAmount;
        private float _lastHover = -10f;

        /// <summary>
        /// Gets the container transform for the panel's collapsible content.
        /// </summary>
        public RectTransform Content { get; private set; }

        /// <summary>
        /// Gets the current total height of the panel in pixels.
        /// </summary>
        public float CurrentHeight { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the panel is currently expanded past halfway.
        /// </summary>
        public bool IsOpen => _openAmount > 0.5f;

        private void Update() {
            if (!_rect) return;

            bool hover = false;
            var mouse = Mouse.current;
            if (mouse != null)
                hover = RectTransformUtility.RectangleContainsScreenPoint(_rect, mouse.position.ReadValue(), null);
            if (hover) _lastHover = Time.unscaledTime;

            bool wantOpen = _pinned || hover || Time.unscaledTime - _lastHover < _closeDelay;
            bool wasOpen = IsOpen;
            _openAmount = Mathf.MoveTowards(_openAmount, wantOpen ? 1f : 0f, Time.unscaledDeltaTime * _speed);
            Apply(_openAmount);
            if (wasOpen != IsOpen) RefreshHeader();
        }

        /// <summary>
        /// Initializes the panel hierarchy, header, and content container.
        /// </summary>
        /// <param name="font">Font used for header and pin indicator text.</param>
        /// <param name="headerTitle">Initial title displayed in the header.</param>
        /// <param name="contentH">Target height of the expanded content area.</param>
        /// <param name="headerColor">Background color of the header bar.</param>
        public void Build(Font font, string headerTitle, float contentH, Color headerColor) {
            _rect = (RectTransform)transform;
            _contentHeight = contentH;
            gameObject.AddComponent<RectMask2D>();

            var header = new GameObject("Header", typeof(RectTransform));
            var hrt = (RectTransform)header.transform;
            hrt.SetParent(_rect, false);
            hrt.anchorMin = new Vector2(0, 1);
            hrt.anchorMax = new Vector2(1, 1);
            hrt.pivot = new Vector2(0.5f, 1);
            hrt.offsetMin = new Vector2(0, -_headerHeight);
            hrt.offsetMax = Vector2.zero;
            var img = header.AddComponent<Image>();
            img.color = headerColor;
            var button = header.AddComponent<Button>();
            button.targetGraphic = img;
            button.onClick.AddListener(TogglePin);

            _headerText = MakeText(hrt, font, 16, TextAnchor.MiddleLeft,
                new Vector2(0, 0), new Vector2(1, 1), new Vector2(12, 0), new Vector2(-135, 0));
            _pinText = MakeText(hrt, font, 13, TextAnchor.MiddleRight,
                new Vector2(1, 0), new Vector2(1, 1), new Vector2(-130, 0), new Vector2(-10, 0));
            _pinText.color = new Color(0.7f, 0.75f, 0.8f);

            var content = new GameObject("Content", typeof(RectTransform));
            Content = (RectTransform)content.transform;
            Content.SetParent(_rect, false);
            Content.anchorMin = new Vector2(0, 1);
            Content.anchorMax = new Vector2(1, 1);
            Content.pivot = new Vector2(0.5f, 1);
            Content.offsetMin = new Vector2(0, -_headerHeight - _contentHeight);
            Content.offsetMax = new Vector2(0, -_headerHeight);
            _contentGroup = content.AddComponent<CanvasGroup>();

            SetTitle(headerTitle);
            Apply(_pinned ? 1f : 0f);
            _openAmount = _pinned ? 1f : 0f;
        }

        /// <summary>
        /// Updates the header title text.
        /// </summary>
        /// <param name="text">The new title string.</param>
        public void SetTitle(string text) {
            _title = text;
            RefreshHeader();
        }

        /// <summary>
        /// Toggles whether the panel remains pinned open.
        /// </summary>
        public void TogglePin() {
            _pinned = !_pinned;
            RefreshHeader();
        }

        private void RefreshHeader() {
            if (_headerText == null) return;
            _headerText.text = (IsOpen || _pinned ? "▼ " : "► ") + _title;
            _pinText.text = _pinned ? "przypięty ✕" : "kliknij: przypnij";
        }

        private void Apply(float amount) {
            float e = amount * amount * (3f - 2f * amount);
            CurrentHeight = _headerHeight + _contentHeight * e;
            _rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, CurrentHeight);
            if (_contentGroup != null) {
                _contentGroup.alpha = e;
                _contentGroup.interactable = e > 0.5f;
                _contentGroup.blocksRaycasts = e > 0.5f;
            }
        }

        private static Text MakeText(RectTransform parent, Font font, int size, TextAnchor anchor,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 offMin, Vector2 offMax) {
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
