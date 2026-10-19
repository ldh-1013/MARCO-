using UnityEngine;

namespace Marco.Prototype
{
    // Placeholder interactable for the future production valve object.
    public sealed class TemporaryValvePrototype : MonoBehaviour
    {
        [SerializeField] private Transform handle;
        [SerializeField] private Renderer indicator;
        public bool IsActivated { get; private set; }

        private void Awake()
        {
            if (handle == null)
            {
                var wheel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                wheel.name = "Temporary valve handle";
                wheel.transform.SetParent(transform, false);
                wheel.transform.localPosition = new Vector3(0f, 1.15f, 0f);
                wheel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                wheel.transform.localScale = new Vector3(0.85f, 0.09f, 0.85f);
                var wheelCollider = wheel.GetComponent<Collider>();
                if (wheelCollider != null) Destroy(wheelCollider);
                handle = wheel.transform;
                indicator = wheel.GetComponent<Renderer>();
            }

            var body = GetComponent<Renderer>();
            if (body != null) body.material.color = new Color(0.22f, 0.28f, 0.32f);
            UpdateIndicator();
        }

        public bool TryInteract()
        {
            IsActivated = !IsActivated;
            if (handle != null) handle.localRotation *= Quaternion.Euler(0f, 0f, 90f);
            UpdateIndicator();
            Debug.Log("Temporary valve " + (IsActivated ? "activated" : "deactivated"), this);
            return true;
        }

        private void UpdateIndicator()
        {
            if (indicator == null) return;
            indicator.material.color = IsActivated ? new Color(0.2f, 0.85f, 0.35f) : new Color(0.85f, 0.55f, 0.12f);
        }
    }
}
