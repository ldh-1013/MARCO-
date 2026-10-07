using UnityEngine;

namespace Marco.Prototype
{
    // Imported assets supply visuals; gameplay remains on the scene's player root.
    public sealed class CharacterVisualPrototype : MonoBehaviour
    {
        [SerializeField] private Transform[] visuals = new Transform[0];
        [SerializeField] private bool hideFromFirstPerson = true;
        private void Awake()
        {
            foreach (var visual in visuals)
            {
                if (visual == null) continue;
                foreach (var script in visual.GetComponentsInChildren<MonoBehaviour>(true)) script.enabled = false;
                foreach (var collider in visual.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                foreach (var body in visual.GetComponentsInChildren<Rigidbody>(true)) { body.isKinematic = true; body.useGravity = false; }
                foreach (var animator in visual.GetComponentsInChildren<Animator>(true)) animator.applyRootMotion = false;
                if (hideFromFirstPerson) foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true)) renderer.gameObject.layer = 2;
            }
        }
    }
}
