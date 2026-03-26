using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

public class AudioCube : MonoBehaviour
{
    public List<Vector3> pathNodes;
    private Vector3 startPos;
    public bool isReadyToPlay = false;

    private int lastIndex = -1;
    private float lastBeatTracker = -1f;
    private bool hasReturned = false;

    private AudioSource myAudio;

    void Start()
    {
        myAudio = GetComponent<AudioSource>();
        startPos = transform.position;
    }

    void Update()
    {
        if (!GlobalClock.IsPlaying || pathNodes.Count < 2) return;

        float currentBeat = GlobalClock.CurrentBeat;

        if (currentBeat < lastBeatTracker) 
        {
            hasReturned = false;
            lastIndex = -1;
            isReadyToPlay = true; 
        }
        
        lastBeatTracker = currentBeat;

        if (!isReadyToPlay)
        {
            if (GlobalClock.CurrentBeat < 0.1f) isReadyToPlay = true;
            return;
        }

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

            if (hasReturned) 
            {
                transform.position = pathNodes[0];
                transform.rotation = Quaternion.identity;
                return;
            }

            float beatsPast = currentBeat - (pathNodes.Count - 1);
            float delay = (1f - ProjectConfig.snapThreshold)/2; 
            float returnDuration = 1.0f;

            float percentToFirstTile = Mathf.Clamp((beatsPast - delay) / returnDuration, 0, 1);

            if (percentToFirstTile >= 1.0f) hasReturned = true;

            float smoothProgress = Mathf.SmoothStep(0, 1, percentToFirstTile);

            Vector3 endTile = pathNodes[pathNodes.Count - 1];
            Vector3 startTile = pathNodes[0];


            if (percentToFirstTile <= 0)
            {
                transform.position = endTile;
                transform.rotation = Quaternion.identity; 
            }
            else
            {
                Vector3 basePos = Vector3.Lerp(endTile, startTile, smoothProgress);
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
                var allCubes = Object.FindObjectsByType<AudioCube>();
                int n = allCubes.Length;
                //float individualVolume = ProjectConfig.MaxSystemVolume / Mathf.Max(1, n);

                if (myAudio != null)
                {
                    myAudio.pitch = tile.myFrequency / ProjectConfig.refFreq;
                    //myAudio.volume = individualVolume;

                    myAudio.PlayOneShot(myAudio.clip);
                }
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
