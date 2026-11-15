using UnityEngine;

namespace Simulation
{
    /// Obraca etykietę tekstu tak, żeby zawsze była zwrócona do kamery.
    public class FaceCamera : MonoBehaviour
    {
        private void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;
            transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position, cam.transform.up);
        }
    }
}
