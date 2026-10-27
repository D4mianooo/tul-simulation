using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Simulation
{
    public class Mouse3D : MonoBehaviour
    {
        public static Mouse3D Instance { get; private set; }

        [SerializeField] private Camera mainCamera;
        [SerializeField] private LayerMask mouseColliderLayerMask = ~0;

        public Vector3 MousePosition { get; private set; }

        public event Action<Vector3> OnLeftMouseClicked;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (mainCamera == null)
            {
                mainCamera = Camera.main;
            }
        }

        private void Update()
        {
            if (Mouse.current == null || mainCamera == null)
            {
                return;
            }

            Vector2 screenPosition = Mouse.current.position.ReadValue();
            Ray ray = mainCamera.ScreenPointToRay(screenPosition);

            if (Physics.Raycast(ray, out RaycastHit raycastHit, float.MaxValue, mouseColliderLayerMask))
            {
                MousePosition = raycastHit.point;
                transform.position = raycastHit.point;

                if (Mouse.current.leftButton.wasPressedThisFrame)
                {
                    OnLeftMouseClicked?.Invoke(MousePosition);
                }
            }
        }

        public static Vector3 GetMouseWorldPosition()
        {
            return Instance != null ? Instance.MousePosition : Vector3.zero;
        }
    }
}
