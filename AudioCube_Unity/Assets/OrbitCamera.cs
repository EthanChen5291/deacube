using System.Runtime.Serialization;
using UnityEngine;

public class OrbitCamera : MonoBehaviour
{
    [Header("References")]
    public SongManager songManager;

    [Header("Orbit Settings")]
    public Vector3 targetDestination;
    public Vector3 targetRotation;
    private Vector3 currentLerpTarget;
    
    [Range(5f, 50)]
    public float distance = 5.0f; // doesnt work?
    public float xSpeed = 40.0f;
    public float ySpeed = 2.0f;

    [Header("Orbit Settings")]
    private float cameraSpeed = 5f;
    public float distanceToNextGrid;

    private float x = 0.0f;
    private float y = 50.0f;

    void Start()
    {
        targetDestination  = new Vector3(2f, 0f, 1.5f);
        currentLerpTarget = targetDestination;

        distanceToNextGrid = (ProjectConfig.Spacing * ProjectConfig.numInversions) + ProjectConfig.gridSpacing;
        ApplyCameraTransform();
    }

    void LateUpdate()
    {
        // zoom
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.01f)
        {
            distance -= scroll * 10f;
            distance = Mathf.Clamp(distance, 5f, 50f);
        }

        // orbit & crane rotation
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
        {
            Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

            float normalizedX = (screenCenter.x - Input.mousePosition.x) / screenCenter.x;
            float normalizedY = (screenCenter.y - Input.mousePosition.y) / screenCenter.y;

            x += normalizedX * xSpeed * Time.deltaTime;

            targetDestination.y -= normalizedY * ySpeed * Time.deltaTime * 0.05f;

            targetDestination.y = Mathf.Clamp(targetDestination.y, -2f, 5f);
        }

        // grid jumping
        if (Input.GetKeyDown(KeyCode.D))
        {
            if (songManager.currentSongData != null && songManager.currentSongData.measures != null)
            {
                float limitX = 2f + (distanceToNextGrid * (songManager.currentSongData.measures.Length - 1));
                
                if (targetDestination.x < limitX + 1) 
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
        
        if (Vector3.Distance(currentLerpTarget, targetDestination) < 0.001f)
        {
            currentLerpTarget = targetDestination;
        }

        float heightPercentage = Mathf.InverseLerp(-2f, 5f, currentLerpTarget.y);

        y = Mathf.Lerp(10f, 85f, heightPercentage);

        ApplyCameraTransform();

        ResetCamera();
    }

    void ApplyCameraTransform()
    {
        Quaternion rotation = Quaternion.Euler(y, x, 0);
        
        Vector3 position = rotation * new Vector3(0.0f, 0.0f, -distance) + currentLerpTarget;

        transform.rotation = rotation;
        transform.position = position;
    }

    void ResetCamera()
    {
        if (Input.GetKeyDown(KeyCode.Q))
        {
            x = 0f;
            y = 40f;

            targetDestination  = new Vector3(2f, 0f, 1.5f);
            currentLerpTarget = targetDestination;
        }
    }
}
