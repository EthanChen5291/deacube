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
        int indexB = indexA + 1;

        if (indexA >= pathNodes.Count - 1)
        {
            transform.position = pathNodes[pathNodes.Count - 1];
            return;
        }

        float percentToNextTile = currentBeat - indexA;

        transform.position = Vector3.Lerp(
            pathNodes[indexA],
            pathNodes[indexB],
            percentToNextTile
        );

        //check if entering a new beat
        int currentTileIndex = Mathf.FloorToInt(GlobalClock.CurrentBeat) % pathNodes.Count;

        if (currentTileIndex != lastIndex)
        {
            TriggerTileSound(currentTileIndex);
            lastIndex = currentTileIndex;
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
        if (pathNodes.Count > 0) transform.position = startPos;
        isReadyToPlay = true;
    }

    public void SetPath(List<Vector3> nodes)
    {
        pathNodes = new List<Vector3>(nodes);
        if (pathNodes.Count > 0) startPos = pathNodes[0];

        isReadyToPlay = false;
    }
}
