using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

public class AudioCube : MonoBehaviour
{
    public List<Vector3> pathNodes;
    private Vector3 startPos;
    public bool isReadyToPlay = false;

    private int lastIndex = -1;

    void Start()
    {
        startPos = transform.position;
    }

    void Update()
    {
        if (!GlobalClock.IsPlaying || pathNodes.Count < 2) return;

        if (!isReadyToPlay)
        {
            if (GlobalClock.CurrentBeat < 0.1f) isReadyToPlay = true;
            return;
        }

        float currentBeat = GlobalClock.CurrentBeat;
        int indexA = Mathf.FloorToInt(currentBeat);

        if (indexA < pathNodes.Count)
        {
            if (indexA != lastIndex)
            {
                if (indexA < pathNodes.Count)
                {
                    TriggerTileSound(indexA);
                    lastIndex = indexA;
                }
            }   
        }

        if (indexA >= pathNodes.Count - 1) // lerp to beginning? not sure
        {
            float beatsPast = currentBeat - indexA;
            float percentToFirstTile = Mathf.Clamp(beatsPast, 0, 1);

            float smoothProgress = Mathf.SmoothStep(0, 1, percentToFirstTile);

            if (indexA < pathNodes.Count)
            {
                Vector3 basePos = Vector3.Lerp(pathNodes[pathNodes.Count - 1], pathNodes[0], smoothProgress);

                float hopY = Mathf.Sin(smoothProgress * Mathf.PI) * ProjectConfig.cubeHopIntensity;

                transform.position = basePos + new Vector3(0, hopY, 0);

                transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.identity, smoothProgress);
            }
            return;
        }

        int indexB = indexA + 1;
        float percentToNextTile = currentBeat - indexA;

        if (percentToNextTile < ProjectConfig.snapThreshold)
        {
            transform.position = pathNodes[indexA];
        } 
        else
        {
            float flipPercent = (percentToNextTile - ProjectConfig.snapThreshold) / (1.0f - ProjectConfig.snapThreshold);

            // cube "jump"
            Vector3 basePos = Vector3.Lerp(pathNodes[indexA], pathNodes[indexB], flipPercent);

            float hopY = Mathf.Sin(flipPercent * Mathf.PI) * ProjectConfig.cubeHopIntensity;

            transform.position = basePos + new Vector3(0, hopY, 0);

            // cube rotation
            Vector3 movementDir = (pathNodes[indexB] - pathNodes[indexA]).normalized;

            Vector3 axisOfRotation = Vector3.Cross(Vector3.up, movementDir);

            float distance = Vector3.Distance(pathNodes[indexA], pathNodes[indexB]);

            float rotationDegrees = distance > 1f ? -180f : 90f;

            Quaternion targetRotation = Quaternion.AngleAxis(rotationDegrees, axisOfRotation);

            transform.rotation = Quaternion.Lerp(Quaternion.identity, targetRotation, flipPercent);
        }

    }

    private void OnCollisionEnter(Collision collision)
    {
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }
    }

    void TriggerTileSound(int index)
    {
    RaycastHit hit;
    if (Physics.Raycast(transform.position, Vector3.down, out hit, 2.0f))
        {
        TileInteraction tile = hit.collider.GetComponent<TileInteraction>();
        if (tile != null)
            {
            tile.PlayNote();
            
            //StartCoroutine(FlashTile(tile));
            }
        }
    }

    public void ResetToStart()
    {
        if (pathNodes.Count > 0) transform.position = pathNodes[0];
        lastIndex = -1;
        isReadyToPlay = true;
    }

    public void SetPath(List<Vector3> nodes)
    {
        pathNodes = new List<Vector3>(nodes);
        if (pathNodes.Count > 0) startPos = pathNodes[0];

        isReadyToPlay = true;
    }
}
