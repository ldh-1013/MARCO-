using UnityEngine;

namespace Marco.Prototype
{
    public sealed class SurvivorInteractionPrototype : MonoBehaviour
    {
        [SerializeField] private float interactionRange = 1.5f;
        private void Update()
        {
            if (!Input.GetKeyDown(KeyCode.E)) return;
            foreach (var hit in Physics.OverlapSphere(transform.position, interactionRange))
            {
                var target = hit.GetComponent<PrototypeTarget>();
                if (target == null) continue;
                target.Treat(); Debug.Log("Survivor interaction complete"); return;
            }
        }
    }
}
