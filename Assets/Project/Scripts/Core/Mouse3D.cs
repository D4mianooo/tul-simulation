using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Simulation.Core {
    /// <summary>
    /// Tracks the 3D world position under the mouse cursor and broadcasts left-click events.
    /// </summary>
    public class Mouse3D : Singleton<Mouse3D> {
        [SerializeField]
        private Camera _mainCamera;

        [SerializeField]
        private LayerMask _mouseColliderLayerMask = ~0;

        /// <summary>
        /// Gets the latest raycast world position under the mouse cursor.
        /// </summary>
        public Vector3 MousePosition { get; private set; }

        /// <summary>
        /// Occurs when the user left-clicks outside UI elements.
        /// </summary>
        public event Action<Vector3> OnLeftMouseClicked;

        protected override void Awake() {
            base.Awake();
            if (Instance != this) {
                return;
            }

            if (_mainCamera == null) {
                _mainCamera = Camera.main;
            }
        }

        private void Update() {
            if (Mouse.current == null || _mainCamera == null) {
                return;
            }

            Vector2 screenPosition = Mouse.current.position.ReadValue();
            Ray ray = _mainCamera.ScreenPointToRay(screenPosition);

            if (Physics.Raycast(ray, out RaycastHit raycastHit, float.MaxValue, _mouseColliderLayerMask)) {
                MousePosition = raycastHit.point;
                transform.position = raycastHit.point;

                if (Mouse.current.leftButton.wasPressedThisFrame &&
                    (!EventSystem.current || !EventSystem.current.IsPointerOverGameObject())) {
                    OnLeftMouseClicked?.Invoke(MousePosition);
                }
            }
        }
    }
}
