using UnityEngine;

namespace Simulation.Core {
    /// <summary>
    /// Positions a target marker at the world point clicked by <see cref="Mouse3D"/>.
    /// </summary>
    public class Target : MonoBehaviour {
        private Mouse3D _mouse3D;
        private bool _isSubscribed;

        private void Awake() {
            SubscribeToMouse3D();
            gameObject.SetActive(false);
        }

        private void OnDestroy() {
            if (_isSubscribed && _mouse3D != null) {
                _mouse3D.OnLeftMouseClicked -= HandleLeftMouseClicked;
                _isSubscribed = false;
            }
        }

        private void SubscribeToMouse3D() {
            if (_isSubscribed) {
                return;
            }

            _mouse3D = Mouse3D.Instance ? Mouse3D.Instance : FindAnyObjectByType<Mouse3D>();
            if (_mouse3D) {
                _mouse3D.OnLeftMouseClicked += HandleLeftMouseClicked;
                _isSubscribed = true;
            }
        }

        private void HandleLeftMouseClicked(Vector3 mousePosition) {
            if (!this) {
                return;
            }

            transform.position = mousePosition;
            gameObject.SetActive(true);
        }
    }
}
