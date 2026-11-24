using UnityEngine;

/// A ship flying a slow loop in the sky, for decoration: it follows an ellipse, banks into the turn and bobs a little.
public class SkyTraffic : MonoBehaviour
{
    public Vector3 centre;
    public float radiusX = 150f, radiusZ = 100f;
    public float speed = 14f;           // metres per second along the loop
    public float phase;                 // where on the loop it starts, 0..1
    public bool clockwise;

    float angle;

    void Start() { angle = phase * Mathf.PI * 2f; }

    void Update()
    {
        float circumference = Mathf.PI * (radiusX + radiusZ);
        angle += (clockwise ? -1f : 1f) * speed / Mathf.Max(1f, circumference) * Mathf.PI * 2f * Time.deltaTime;
        Vector3 p = centre + new Vector3(Mathf.Cos(angle) * radiusX, Mathf.Sin(angle * 3f + phase * 9f) * 3f, Mathf.Sin(angle) * radiusZ);
        Vector3 ahead = new Vector3(-Mathf.Sin(angle) * radiusX, 0f, Mathf.Cos(angle) * radiusZ) * (clockwise ? -1f : 1f);
        transform.position = p;
        if (ahead.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(ahead) * Quaternion.Euler(0f, modelYaw, clockwise ? 12f : -12f);
    }

    public float modelYaw;              // turns the model so its nose points along the path
}
