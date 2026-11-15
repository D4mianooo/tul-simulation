using UnityEngine;

namespace Simulation
{
    public class Target : MonoBehaviour
    {
        private Mouse3D mouse3D;
        private bool isSubscribed;

        private void Awake()
        {
            SubscribeToMouse3D();
            gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (isSubscribed && mouse3D != null)
            {
                mouse3D.OnLeftMouseClicked -= HandleLeftMouseClicked;
                isSubscribed = false;
            }
        }

        private void SubscribeToMouse3D()
        {
            if (isSubscribed)
            {
                return;
            }

            mouse3D = Mouse3D.Instance != null ? Mouse3D.Instance : FindFirstObjectByType<Mouse3D>();
            if (mouse3D != null)
            {
                mouse3D.OnLeftMouseClicked += HandleLeftMouseClicked;
                isSubscribed = true;
            }
        }

        private void HandleLeftMouseClicked(Vector3 mousePosition)
        {
            if (this == null)
            {
                return;
            }

            transform.position = mousePosition;
            gameObject.SetActive(true);
        }
    }
}
