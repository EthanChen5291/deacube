using System.Collections.Generic;
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

        if (indexA >= pathNodes.Count - 1)
        {
            transform.position = pathNodes[pathNodes.Count - 1];
            return;
        }

        int indexB = indexA + 1;
        float percentToNextTile = currentBeat - indexA;

        transform.position = Vector3.Lerp(
            pathNodes[indexA],
            pathNodes[indexB],
            percentToNextTile
        );
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
