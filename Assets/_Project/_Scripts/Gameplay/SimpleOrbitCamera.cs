using UnityEngine;

public class SimpleOrbitCamera : MonoBehaviour
{
    [Header("Target")]
    public Transform target;

    [Header("Camera Control")]
    public float distance = 7f;
    public float rotationX = 45f;
    public float rotationY = 0f;

    void LateUpdate()
    {
        if (target == null) return;

        // Только логика движения. Никакого Volume, никакого фокуса.
        Quaternion rotation = Quaternion.Euler(rotationX, rotationY, 0);
        Vector3 position = target.position - (rotation * Vector3.forward * distance);

        transform.position = position;
        transform.rotation = rotation;
    }
}