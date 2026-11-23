using UnityEngine;

namespace Simulation.Visualization {
    /// <summary>
    /// Billboards the transform to always face the main camera.
    /// </summary>
    public class FaceCamera : MonoBehaviour {
        private Camera _mainCamera;

        private void Awake() {
            _mainCamera = Camera.main;
        }

        private void LateUpdate() {
            if (_mainCamera == null) return;
            transform.rotation = Quaternion.LookRotation(transform.position - _mainCamera.transform.position, _mainCamera.transform.up);
        }
    }
}
