using UnityEngine;

namespace Simulation.Core {
    /// <summary>
    /// Base class for scene-scoped <see cref="MonoBehaviour"/> singletons.
    /// </summary>
    /// <typeparam name="T">Concrete singleton component type.</typeparam>
    public abstract class Singleton<T> : MonoBehaviour where T : MonoBehaviour {
        /// <summary>
        /// Gets the active singleton instance of <typeparamref name="T"/>.
        /// </summary>
        public static T Instance { get; private set; }

        /// <summary>
        /// Registers this component as <see cref="Instance"/> or destroys duplicate instances.
        /// </summary>
        protected virtual void Awake() {
            if (Instance != null && Instance != this) {
                Destroy(gameObject);
                return;
            }

            Instance = this as T;
        }

        /// <summary>
        /// Clears <see cref="Instance"/> when the active singleton is destroyed.
        /// </summary>
        protected virtual void OnDestroy() {
            if (Instance == this) {
                Instance = null;
            }
        }
    }
}
