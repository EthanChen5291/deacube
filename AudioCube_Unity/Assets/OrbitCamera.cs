using UnityEngine;

public class OrbitCamera : MonoBehaviour
{
    public Vector3 target = new Vector3(6, 0, 3.5f);
    public float distance = 10.0f;
    public float xSpeed = 120.0f;
    public float ySpeed = 120.0f;

    private float x = 0.0f;
    private float y = 0.0f;

    void Start()
    {
        Vector3 angles = transform.eulerAngles;
        x = angles.y;
        y = angles.x;
    }

    void LateUpdate()
    {
        if (Input.GetMouseButton(1))
        {
            x += Input.GetAxis("Mouse X") * xSpeed * 0.04f;
            y -= Input.GetAxis("Mouse Y") * ySpeed * 0.04f;

            y = Mathf.Clamp(y, 10f, 80f);

            Quaternion rotation = Quaternion.Euler(y, x, 0);
            
            Vector3 position = rotation * new Vector3 (0.0f, 0.0f, -distance) + target;

            transform.rotation = rotation;
            transform.position = position;
        }
    }
}
