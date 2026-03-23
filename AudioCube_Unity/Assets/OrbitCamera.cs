using UnityEngine;

public class OrbitCamera : MonoBehaviour
{
    public Vector3 target = new Vector3(6.6f, 0f, 3.6f);
    
    [Range(5f, 20f)]
    public float distance = 5.0f; // doesnt work?
    public float xSpeed = 150.0f;
    public float ySpeed = 150.0f;

    private float x = 0.0f;
    private float y = 80.0f;

    void Start()
    {
        x = 0f;
        y = 80f;

        UpdateCameraPosition();
    }

    void LateUpdate()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        distance -= scroll * 10f;
        distance = Mathf.Clamp(distance, 5f, 20f);

        if (Input.GetMouseButton(1))
        {
            x += Input.GetAxis("Mouse X") * xSpeed * 0.2f;
            y -= Input.GetAxis("Mouse Y") * ySpeed * 0.2f;

            y = Mathf.Clamp(y, 10f, 85f);
        }
        
        UpdateCameraPosition();
    }

    void UpdateCameraPosition()
    {
        Quaternion rotation = Quaternion.Euler(y, x, 0);
            
        Vector3 position = rotation * new Vector3 (0.0f, 0.0f, -distance) + target;

        transform.rotation = rotation;
        transform.position = position;
    }
}
