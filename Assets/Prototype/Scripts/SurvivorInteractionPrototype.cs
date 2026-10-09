using UnityEngine;

namespace Marco.Prototype
{
    public sealed class SurvivorInteractionPrototype : MonoBehaviour
    {
        [SerializeField] private float interactionRange = 1.5f;
        public event System.Action<bool> InteractionPerformed;
        private void Update()
        {
            if (Cursor.lockState != CursorLockMode.Locked || !Input.GetKeyDown(KeyCode.E)) return;
            TryInteract();
        }

        public bool TryInteract()
        {
            var self = GetComponent<PrototypeTarget>();
            if (!enabled || (self != null && self.Health.IsEcho)) return false;
            foreach (var hit in Physics.OverlapSphere(transform.position, interactionRange))
            {
                var target = hit.GetComponentInParent<PrototypeTarget>();
                if (target == null || target.transform.root == transform.root || !target.Health.IsWounded) continue;
                target.Treat(); InteractionPerformed?.Invoke(true); Debug.Log("Survivor interaction complete"); return true;
            }
            InteractionPerformed?.Invoke(false);
            return false;
        }
    }
}
