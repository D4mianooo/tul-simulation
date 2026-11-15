using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Simulation
{
    /// Kamera jak w edytorze / grach FPP:
    ///   prawy przycisk myszy (przytrzymany) – rozglądanie się,
    ///   W/S/A/D – ruch w kierunku patrzenia (patrzysz w dół + W = obniżasz się),
    ///   Shift – szybciej, Ctrl – wolniej,
    ///   Q/E – obrót w lewo / w prawo,
    ///   kółko myszy – szybkie przybliżenie / oddalenie.
    /// Prędkość rośnie z wysokością nad terenem, więc działa i przy budynkach, i przy widoku całej mapy.
    public class CameraController : MonoBehaviour
    {
        [Tooltip("Prędkość ruchu (jednostki/s) na każde 10 jednostek wysokości nad terenem.")]
        [SerializeField] private float moveSpeed = 10f;
        [SerializeField] private float minMoveSpeed = 1.5f;
        [SerializeField] private float fastMultiplier = 4f;
        [SerializeField] private float slowMultiplier = 0.25f;
        [Tooltip("Czułość myszy przy rozglądaniu się (stopnie na piksel).")]
        [SerializeField] private float lookSensitivity = 0.15f;
        [SerializeField] private float turnSpeed = 60f;          // Q/E, stopnie na sekundę
        [SerializeField] private float scrollSpeed = 5f;
        [SerializeField] private float minHeightAboveGround = 0.3f;

        private float yaw, pitch;

        /// Dodaje sterowanie do głównej kamery, jeśli go nie ma (scena nie musi mieć go ustawionego).
        public static void EnsureOnMainCamera()
        {
            var cam = Camera.main;
            if (cam != null && cam.GetComponent<CameraController>() == null)
                cam.gameObject.AddComponent<CameraController>();
        }

        private void OnEnable()
        {
            Vector3 e = transform.eulerAngles;
            yaw = e.y;
            pitch = e.x > 180f ? e.x - 360f : e.x;
        }

        private void Update()
        {
            var mouse = Mouse.current;
            var kb = Keyboard.current;
            float dt = Time.unscaledDeltaTime;
            bool typing = TypingInField();

            // --- rozglądanie się: przytrzymany prawy przycisk ---
            if (mouse != null && mouse.rightButton.isPressed)
            {
                Vector2 d = mouse.delta.ReadValue();
                yaw += d.x * lookSensitivity;
                pitch = Mathf.Clamp(pitch - d.y * lookSensitivity, -89f, 89f);
            }
            if (mouse != null && mouse.rightButton.wasPressedThisFrame) Cursor.lockState = CursorLockMode.Locked;
            if (mouse != null && mouse.rightButton.wasReleasedThisFrame) Cursor.lockState = CursorLockMode.None;

            if (kb != null && !typing)
            {
                if (kb.qKey.isPressed) yaw -= turnSpeed * dt;
                if (kb.eKey.isPressed) yaw += turnSpeed * dt;
            }
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

            // --- ruch: WSAD w kierunku patrzenia ---
            float speed = Mathf.Max(minMoveSpeed, moveSpeed * HeightAboveGround() / 10f);
            Vector3 move = Vector3.zero;
            if (kb != null && !typing)
            {
                if (kb.wKey.isPressed) move += transform.forward;
                if (kb.sKey.isPressed) move -= transform.forward;
                if (kb.dKey.isPressed) move += transform.right;
                if (kb.aKey.isPressed) move -= transform.right;
                if (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed) speed *= fastMultiplier;
                else if (kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed) speed *= slowMultiplier;
            }
            Vector3 delta = move.sqrMagnitude > 0f ? move.normalized * speed * dt : Vector3.zero;

            // --- kółko myszy: przybliżanie w kierunku patrzenia (nie nad panelami) ---
            if (mouse != null && !PointerOverUI())
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.0001f)
                {
                    float steps = Mathf.Abs(scroll) > 10f ? scroll / 120f : scroll;
                    delta += transform.forward * steps * scrollSpeed * Mathf.Max(0.3f, HeightAboveGround() / 10f);
                }
            }

            if (delta != Vector3.zero) transform.position = KeepAboveGround(transform.position + delta);
        }

        private void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None;
        }

        private float HeightAboveGround()
        {
            Vector3 p = transform.position;
            if (Physics.Raycast(p + Vector3.up * 1000f, Vector3.down, out RaycastHit hit, 100000f))
                return Mathf.Max(0.1f, p.y - hit.point.y);
            return Mathf.Max(0.1f, Mathf.Abs(p.y));
        }

        private Vector3 KeepAboveGround(Vector3 p)
        {
            if (Physics.Raycast(new Vector3(p.x, p.y + 1000f, p.z), Vector3.down, out RaycastHit hit, 100000f))
                p.y = Mathf.Max(p.y, hit.point.y + minHeightAboveGround);
            return p;
        }

        /// Nie ruszamy kamerą, gdy ktoś wpisuje wartość w pole tekstowe panelu parametrów.
        private static bool TypingInField()
        {
            var es = EventSystem.current;
            if (es == null || es.currentSelectedGameObject == null) return false;
            var tmp = es.currentSelectedGameObject.GetComponent<TMPro.TMP_InputField>();
            if (tmp != null && tmp.isFocused) return true;
            var legacy = es.currentSelectedGameObject.GetComponent<UnityEngine.UI.InputField>();
            return legacy != null && legacy.isFocused;
        }

        private static bool PointerOverUI()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }
    }
}
