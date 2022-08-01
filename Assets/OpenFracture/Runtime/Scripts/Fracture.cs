using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(Rigidbody))]
public class Fracture : MonoBehaviour
{
    public TriggerOptions    triggerOptions;
    public FractureOptions   fractureOptions;
    public RefractureOptions refractureOptions;
    public CallbackOptions   callbackOptions;
    public ContactPoint      firstHitPoint;

    /// <summary>
    /// The number of times this fragment has been re-fractured.
    /// </summary>
    [HideInInspector]
    public int currentRefractureCount = 0;

    /// <summary>
    /// Collector object that stores the produced fragments
    /// </summary>
    private GameObject fragmentRoot;

    [ContextMenu("Print Mesh Info")]
    public void PrintMeshInfo()
    {
        var mesh = GetComponent<MeshFilter>().mesh;
        Debug.Log("Positions");

        var positions = mesh.vertices;
        var normals = mesh.normals;
        var uvs = mesh.uv;

        for (int i = 0; i < positions.Length; i++)
        {
            Debug.Log($"Vertex {i}");
            Debug.Log($"POS | X: {positions[i].x} Y: {positions[i].y} Z: {positions[i].z}");
            Debug.Log($"NRM | X: {normals[i].x} Y: {normals[i].y} Z: {normals[i].z} LEN: {normals[i].magnitude}");
            Debug.Log($"UV  | U: {uvs[i].x} V: {uvs[i].y}");
            Debug.Log("");
        }
    }

    void OnValidate()
    {
        if (transform.parent != null)
        {
            // When an object is fractured, the fragments are created as children of that object's parent.
            // Because of this, they inherit the parent transform. If the parent transform is not scaled
            // the same in all axes, the fragments will not be rendered correctly.
            var scale = transform.parent.localScale;
            if ((scale.x != scale.y) || (scale.x != scale.z) || (scale.y != scale.z))
            {
                Debug.LogWarning($"Warning: Parent transform of fractured object must be uniformly scaled in all axes or fragments will not render correctly.", transform);
            }
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        if (triggerOptions.triggerType == TriggerType.Collision)
        {
            if (collision.contactCount > 0)
            {
                // Collision force must exceed the minimum force (F = I / T)
                var contact = collision.contacts[0];
                // dI = F * dt
                float collisionForce = collision.impulse.magnitude / Time.fixedDeltaTime;

                // Colliding object tag must be in the set of allowed collision tags if filtering by tag is enabled
                bool tagAllowed = triggerOptions.IsTagAllowed(contact.otherCollider.gameObject.tag);

                // Object is unfrozen if the colliding object has the correct tag (if tag filtering is enabled)
                // and the collision force exceeds the minimum collision force.
                if (collisionForce > triggerOptions.minimumCollisionForce &&
                    (!triggerOptions.filterCollisionsByTag || tagAllowed))
                {
                    firstHitPoint = contact;
                    ComputeFracture();
                }
            }
        }
    }

    void OnTriggerEnter(Collider collider)
    {
        if (triggerOptions.triggerType == TriggerType.Trigger)
        {
            // Colliding object tag must be in the set of allowed collision tags if filtering by tag is enabled
            bool tagAllowed = triggerOptions.IsTagAllowed(collider.gameObject.tag);

            if (!triggerOptions.filterCollisionsByTag || tagAllowed)
            {
                ComputeFracture();
            }
        }
    }

    void Update()
    {
        if (triggerOptions.triggerType == TriggerType.Keyboard)
        {
            if (Input.GetKeyDown(triggerOptions.triggerKey))
            {
                ComputeFracture();
            }
        }
    }

    /// <summary>
    /// Compute the fracture and create the fragments
    /// </summary>
    /// <returns></returns>
    private void ComputeFracture() // start fracture once condition is satisfied
    {
        var mesh = GetComponent<MeshFilter>().sharedMesh;

        if (mesh != null)
        {
            // If the fragment root object has not yet been created, create it now
            if (fragmentRoot == null)
            {
                // Create a game object to contain the fragments
                fragmentRoot = new GameObject($"{name}Fragments");
                fragmentRoot.transform.SetParent(transform.parent);

                // Each fragment will handle its own scale
                fragmentRoot.transform.position = transform.position;
                fragmentRoot.transform.rotation = transform.rotation;
                fragmentRoot.transform.localScale = Vector3.one;
            }

            var fragmentTemplate = CreateFragmentTemplate();

            if (fractureOptions.asynchronous)
            {
                StartCoroutine(Fragmenter.FractureAsync(
                    gameObject,
                    fractureOptions,
                    fragmentTemplate,
                    fragmentRoot.transform,
                    () =>
                    {
                        // Done with template, destroy it
                        GameObject.Destroy(fragmentTemplate);

                        // Deactivate the original object
                        gameObject.SetActive(false);

                        // Fire the completion callback
                        if ((currentRefractureCount == 0) ||
                            (currentRefractureCount > 0 && refractureOptions.invokeCallbacks))
                        {
                            if (callbackOptions.onCompleted != null)
                            {
                                callbackOptions.onCompleted.Invoke();
                            }
                        }
                    }
                    ));
            }
            else
            {
                Fragmenter.firstHitPoint = firstHitPoint;
                Fragmenter.Fracture(gameObject,
                    fractureOptions,
                    fragmentTemplate,
                    fragmentRoot.transform);

                // Done with template, destroy it
                GameObject.Destroy(fragmentTemplate);

                // Deactivate the original object
                gameObject.SetActive(false);

                // Fire the completion callback
                if ((currentRefractureCount == 0) ||
                    (currentRefractureCount > 0 && refractureOptions.invokeCallbacks))
                {
                    if (callbackOptions.onCompleted != null)
                    {
                        callbackOptions.onCompleted.Invoke();
                    }
                }
            }
        }
    }

    /// <summary>
    /// Creates a template object which each fragment will derive from
    /// </summary>
    /// <param name="preFracture">True if this object is being pre-fractured. This will freeze all of the fragments.</param>
    /// <returns></returns>
    private GameObject CreateFragmentTemplate()
    {
        // If pre-fracturing, make the fragments children of this object so they can easily be unfrozen later.
        // Otherwise, parent to this object's parent
        GameObject obj = new GameObject();
        obj.name = "Fragment";
        obj.tag = tag;

        // Update mesh to the new sliced mesh
        obj.AddComponent<MeshFilter>();

        // Add materials. Normal material goes in slot 1, cut material in slot 2
        var meshRenderer = obj.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterials = new Material[2]
        {
            GetComponent<MeshRenderer>().sharedMaterial,
            fractureOptions.insideMaterial
        };

        // Copy collider properties to fragment
        var thisCollider = GetComponent<Collider>();
        var fragmentCollider = obj.AddComponent<MeshCollider>();
        fragmentCollider.convex = true;
        fragmentCollider.sharedMaterial = thisCollider.sharedMaterial;
        fragmentCollider.isTrigger = thisCollider.isTrigger;

        // Copy rigid body properties to fragment
        var thisRigidBody = GetComponent<Rigidbody>();
        var fragmentRigidBody = obj.AddComponent<Rigidbody>();
        fragmentRigidBody.velocity = thisRigidBody.velocity;
        fragmentRigidBody.angularVelocity = thisRigidBody.angularVelocity;
        fragmentRigidBody.drag = thisRigidBody.drag;
        fragmentRigidBody.angularDrag = thisRigidBody.angularDrag;
        fragmentRigidBody.useGravity = thisRigidBody.useGravity;

        // If refracturing is enabled, create a copy of this component and add it to the template fragment object
        if (refractureOptions.enableRefracturing &&
            (currentRefractureCount < refractureOptions.maxRefractureCount))
        {
            CopyFractureComponent(obj);
        }

        return obj;
    }

    /// <summary>
    /// Convenience method for copying this component to another component
    /// </summary>
    /// <param name="obj">The GameObject to copy the component to</param>
    private void CopyFractureComponent(GameObject obj)
    {
        var fractureComponent = obj.AddComponent<Fracture>();

        fractureComponent.triggerOptions = triggerOptions;
        fractureComponent.fractureOptions = fractureOptions;
        fractureComponent.refractureOptions = refractureOptions;
        fractureComponent.callbackOptions = callbackOptions;
        fractureComponent.currentRefractureCount = currentRefractureCount + 1;
        fractureComponent.fragmentRoot = fragmentRoot;
    }
}
