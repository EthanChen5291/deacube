using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class PathManager : MonoBehaviour
{
    // path states 
    public enum EditorState {Idle, PlacingStart, DrawingPath}
    public EditorState currentState = EditorState.Idle;

    // tiles
    public List<TileInteraction> currentPathTiles = new List<TileInteraction>();
    private List<TileInteraction> allTiles;

    //cube attributes
    public GameObject audioCube;
    private GameObject activeCube;

    public bool isSettingPath = false;

    void Start()
    {
        var gridManager = Object.FindAnyObjectByType<AudioGridManager>();
        if (gridManager != null) allTiles = gridManager.allTiles;
    }

    public void OnTileClicked(TileInteraction clickedTile)
    {
        if (currentState == EditorState.PlacingStart)
        {
            Vector3 spawnPos = clickedTile.transform.position + new Vector3(0, ProjectConfig.CubeDropHeight, 0);

            activeCube = Instantiate(audioCube, spawnPos, Quaternion.identity);
            currentPathTiles.Add(clickedTile);

            currentState = EditorState.DrawingPath;
            HighlightNeighbors(clickedTile);
        }
        else if (currentState == EditorState.DrawingPath)
        {
            TileInteraction lastTile = currentPathTiles[currentPathTiles.Count - 1];
            float dist = Vector3.Distance(clickedTile.transform.position, lastTile.transform.position);
            
            if (dist <= 1.75f && !currentPathTiles.Contains(clickedTile))
            {
                currentPathTiles.Add(clickedTile);

                List<Vector3> pathPositions = new List<Vector3>();
                foreach(var tile in currentPathTiles)
                {
                    pathPositions.Add(tile.transform.position + new Vector3(0, ProjectConfig.CubeHoverHeight, 0));
                }

                if (activeCube != null)
                {
                    activeCube.GetComponent<AudioCube>().SetPath(pathPositions);
                    StartCoroutine(AnimateCubeMove(activeCube, clickedTile));
                }
                
                HighlightNeighbors(clickedTile);
            }
        }

    }

    IEnumerator AnimateCubeMove(GameObject cubeToMove, TileInteraction clickedTile)
    {
        Vector3 targetPos = clickedTile.transform.position + new Vector3(0, ProjectConfig.CubeHoverHeight, 0);
        Vector3 startPos = cubeToMove.transform.position;

        float elapsed = 0;
        float duration = 0.15f;

        while (elapsed < duration)
        {
            if (cubeToMove == null) yield break;

            cubeToMove.transform.position = Vector3.Lerp(
                startPos,
                targetPos,
                elapsed / duration
            );
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (cubeToMove != null) cubeToMove.transform.position = targetPos;
    }

    public void FinalizePath()
    {
        int pathLength = currentPathTiles.Count;
        if (pathLength > GlobalClock.MasterBeatLength) GlobalClock.MasterBeatLength = (float)pathLength;
        GlobalClock.CurrentBeat = 0;

        List<Vector3> worldPositions = new List<Vector3>();

        foreach(var tile in currentPathTiles)
        {
            Vector3 nodePos = tile.transform.position + new Vector3(0, ProjectConfig.CubeHoverHeight, 0);
            worldPositions.Add(nodePos);
        }

        activeCube.GetComponent<AudioCube>().SetPath(worldPositions);

        currentPathTiles.Clear();

        isSettingPath = false;
        currentState = EditorState.Idle;
    }

    void HighlightNeighbors(TileInteraction centerTile)
    {
        foreach(var t in allTiles)
        {
            t.ResetColor();
            int dx = Mathf.Abs(t.gridX - centerTile.gridX);
            int dz = Mathf.Abs(t.gridZ - centerTile.gridZ);

            if (dx <= 1 && dz <= 1 && t != centerTile && !currentPathTiles.Contains(t))
            {
                t.SetColor(Color.yellow);
            }
        }
    }
}
