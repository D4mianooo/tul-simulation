using UnityEngine;
using UnityEngine.InputSystem;

namespace Simulation
{
    public class CameraController : MonoBehaviour
    {
        [SerializeField] private Vector3 sceneCenter = Vector3.zero;
        [SerializeField] private float scrollSpeed = 5f;
        [SerializeField] private float rotationSpeed = 0.25f;

        private void Update()
        {
            if (Mouse.current == null)
            {
                return;
            }

            float scrollY = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(scrollY) > 0.0001f)
            {
                float normalizedScroll = Mathf.Abs(scrollY) > 10f ? scrollY / 120f : scrollY;
                transform.Translate(0f, 0f, normalizedScroll * scrollSpeed, Space.Self);
            }

            if (Mouse.current.middleButton.isPressed)
            {
                Vector2 delta = Mouse.current.delta.ReadValue();
                if (delta.sqrMagnitude > 0.0001f)
                {
                    transform.RotateAround(sceneCenter, Vector3.up, delta.x * rotationSpeed);
                    transform.RotateAround(sceneCenter, transform.right, -delta.y * rotationSpeed);
                }
            }
        }
    }
}
