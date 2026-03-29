using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AudioCube : MonoBehaviour
{
    public List<Vector3> pathNodes;
    private Vector3 startPos;
    public bool isReadyToPlay = false;
    public bool isFinalized = false;

    public int assignedGridIndex;
    private int lastIndex = -1;
    public float startBeatOffset;

    private AudioSource myAudio;

    void Start()
    {
        myAudio = GetComponent<AudioSource>();
        startPos = transform.position;
    }

    void Update()
    {
        if (!GlobalClock.IsPlaying || pathNodes == null || pathNodes.Count < 1 || !isFinalized) return;

        float localBeat = GlobalClock.SongBeat - startBeatOffset;

        if (localBeat < 0f) 
        {
            transform.position = pathNodes[0];
            transform.rotation = Quaternion.identity;
            return; 
        }

        int indexA = Mathf.FloorToInt(localBeat);

        if (indexA >= 0 && indexA < pathNodes.Count)
        {
            if (indexA != lastIndex)
            {
                TriggerTileSound(indexA);
                lastIndex = indexA;
            }
        }

        if (indexA >= pathNodes.Count - 1) 
        {
            transform.position = pathNodes[pathNodes.Count - 1];
            transform.rotation = Quaternion.identity;
            return;
        }

        int indexB = indexA + 1;
        float percentToNextTile = localBeat - indexA;

        if (percentToNextTile < ProjectConfig.snapThreshold)
        {
            transform.position = pathNodes[indexA];
        } 
        else
        {
            float flipPercent = (percentToNextTile - ProjectConfig.snapThreshold) / (1.0f - ProjectConfig.snapThreshold);
            Vector3 basePos = Vector3.Lerp(pathNodes[indexA], pathNodes[indexB], flipPercent);
            
            float hopY = Mathf.Sin(flipPercent * Mathf.PI) * ProjectConfig.cubeHopIntensity;
            transform.position = basePos + new Vector3(0, hopY, 0);

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
                if (myAudio != null)
                {
                    myAudio.pitch = tile.myFrequency / ProjectConfig.refFreq;
                    myAudio.PlayOneShot(myAudio.clip);
                }
            }
        }
    }

    public void ResetToStart()
    {
        if (pathNodes != null && pathNodes.Count > 0) 
        {
            transform.position = pathNodes[0];
            transform.rotation = Quaternion.identity;
        }
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