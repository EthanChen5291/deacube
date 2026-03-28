using System.Runtime.Serialization;
using UnityEngine;

public class OrbitCamera : MonoBehaviour
{
    [Header("References")]
    public SongManager songManager;

    [Header("Orbit Settings")]
    public Vector3 targetDestination;
    private Vector3 currentLerpTarget;
    
    [Range(5f, 20f)]
    public float distance = 5.0f; // doesnt work?
    public float xSpeed = 150.0f;
    public float ySpeed = 150.0f;

    [Header("Orbit Settings")]
    private float cameraSpeed = 1f;
    public float distanceToNextGrid;

    private float x = 0.0f;
    private float y = 50.0f;

    void Start()
    {
        targetDestination  = new Vector3(2f, 0f, 1.5f);
        currentLerpTarget = targetDestination;

        distanceToNextGrid = (ProjectConfig.Spacing * ProjectConfig.numInversions) / 2f + ProjectConfig.gridSpacing;
        ApplyCameraTransform();
    }

    void LateUpdate()
    {
        // zoom
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.01f)
        {
            distance -= scroll * 10f;
            distance = Mathf.Clamp(distance, 5f, 20f);
        }

        // rotation
        if (Input.GetMouseButton(1))
        {
            x += Input.GetAxis("Mouse X") * xSpeed * 0.2f;
            y -= Input.GetAxis("Mouse Y") * ySpeed * 0.2f;

            y = Mathf.Clamp(y, 10f, 85f);
        }

        // grid jumping
        if (Input.GetKeyDown(KeyCode.D))
        {
            if (songManager.currentSongData != null && songManager.currentSongData.measures != null)
            {
                float limitX = 2f + (distanceToNextGrid * (songManager.currentSongData.measures.Length - 1));
                
                if (targetDestination.x < limitX - 0.1f) 
                {
                    targetDestination += new Vector3(distanceToNextGrid, 0f, 0f);
                }
            }
        } 
        else if (Input.GetKeyDown(KeyCode.A))
        {
            if (targetDestination.x > 2.1f) {
                targetDestination += new Vector3(-distanceToNextGrid, 0f, 0f);
            }
        }

        currentLerpTarget = Vector3.Lerp(currentLerpTarget, targetDestination, Time.deltaTime * cameraSpeed);
        
        ApplyCameraTransform();
    }

    void ApplyCameraTransform()
    {
        Quaternion rotation = Quaternion.Euler(y, x, 0);
        
        Vector3 position = rotation * new Vector3(0.0f, 0.0f, -distance) + currentLerpTarget;

        transform.rotation = rotation;
        transform.position = position;
    }
}
