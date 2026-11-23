using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Simulation.Core {
    /// <summary>
    /// Free-fly camera controller with mouse look, WASD movement, and scroll zoom.
    /// </summary>
    public class CameraController : MonoBehaviour {
        [Tooltip("Prędkość ruchu (jednostki/s) na każde 10 jednostek wysokości nad terenem.")]
        [SerializeField]
        private float _moveSpeed = 10f;

        [SerializeField]
        private float _minMoveSpeed = 1.5f;

        [SerializeField]
        private float _fastMultiplier = 4f;

        [SerializeField]
        private float _slowMultiplier = 0.25f;

        [Tooltip("Czułość myszy przy rozglądaniu się (stopnie na piksel).")]
        [SerializeField]
        private float _lookSensitivity = 0.15f;

        [SerializeField]
        private float _turnSpeed = 60f;

        [SerializeField]
        private float _scrollSpeed = 5f;

        [SerializeField]
        private float _minHeightAboveGround = 0.3f;

        private float _yaw;
        private float _pitch;

        private void OnEnable() {
            Vector3 e = transform.eulerAngles;
            _yaw = e.y;
            _pitch = e.x > 180f ? e.x - 360f : e.x;
        }

        private void Update() {
            var mouse = Mouse.current;
            var kb = Keyboard.current;
            float dt = Time.unscaledDeltaTime;
            bool typing = TypingInField();

            if (mouse != null && mouse.rightButton.isPressed) {
                Vector2 d = mouse.delta.ReadValue();
                _yaw += d.x * _lookSensitivity;
                _pitch = Mathf.Clamp(_pitch - d.y * _lookSensitivity, -89f, 89f);
            }
            if (mouse != null && mouse.rightButton.wasPressedThisFrame) Cursor.lockState = CursorLockMode.Locked;
            if (mouse != null && mouse.rightButton.wasReleasedThisFrame) Cursor.lockState = CursorLockMode.None;

            if (kb != null && !typing) {
                if (kb.qKey.isPressed) _yaw -= _turnSpeed * dt;
                if (kb.eKey.isPressed) _yaw += _turnSpeed * dt;
            }
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);

            float speed = Mathf.Max(_minMoveSpeed, _moveSpeed * HeightAboveGround() / 10f);
            Vector3 move = Vector3.zero;
            if (kb != null && !typing) {
                if (kb.wKey.isPressed) move += transform.forward;
                if (kb.sKey.isPressed) move -= transform.forward;
                if (kb.dKey.isPressed) move += transform.right;
                if (kb.aKey.isPressed) move -= transform.right;
                if (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed) speed *= _fastMultiplier;
                else if (kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed) speed *= _slowMultiplier;
            }
            Vector3 delta = move.sqrMagnitude > 0f ? move.normalized * (speed * dt) : Vector3.zero;

            if (mouse != null && !PointerOverUI()) {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.0001f) {
                    float steps = Mathf.Abs(scroll) > 10f ? scroll / 120f : scroll;
                    delta += transform.forward * (steps * _scrollSpeed * Mathf.Max(0.3f, HeightAboveGround() / 10f));
                }
            }

            if (delta != Vector3.zero) transform.position = KeepAboveGround(transform.position + delta);
        }

        private void OnDisable() {
            Cursor.lockState = CursorLockMode.None;
        }

        /// <summary>
        /// Ensures a <see cref="CameraController"/> component is attached to the main camera.
        /// </summary>
        public static void EnsureOnMainCamera() {
            var cam = Camera.main;
            if (cam != null && cam.GetComponent<CameraController>() == null)
                cam.gameObject.AddComponent<CameraController>();
        }

        private float HeightAboveGround() {
            Vector3 p = transform.position;
            if (Physics.Raycast(p + Vector3.up * 1000f, Vector3.down, out RaycastHit hit, 100000f))
                return Mathf.Max(0.1f, p.y - hit.point.y);
            return Mathf.Max(0.1f, Mathf.Abs(p.y));
        }

        private Vector3 KeepAboveGround(Vector3 p) {
            if (Physics.Raycast(new Vector3(p.x, p.y + 1000f, p.z), Vector3.down, out RaycastHit hit, 100000f))
                p.y = Mathf.Max(p.y, hit.point.y + _minHeightAboveGround);
            return p;
        }

        private static bool TypingInField() {
            var es = EventSystem.current;
            if (es == null || es.currentSelectedGameObject == null) return false;
            var tmp = es.currentSelectedGameObject.GetComponent<TMPro.TMP_InputField>();
            if (tmp != null && tmp.isFocused) return true;
            var legacy = es.currentSelectedGameObject.GetComponent<UnityEngine.UI.InputField>();
            return legacy != null && legacy.isFocused;
        }

        private static bool PointerOverUI() {
            return EventSystem.current && EventSystem.current.IsPointerOverGameObject();
        }
    }
}
