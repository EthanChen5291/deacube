using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class PathManager : MonoBehaviour
{
    // path states 
    public enum EditorState {Idle, PlacingStart, DrawingPath}
    public EditorState currentState = EditorState.Idle;

    // instrument/cube selection
    [Header("Instrument Palette")] 
    public List<GameObject> cubePrefabs; // color prefabs
    private int selectedInstrumentIndex = 0;

    // tiles
    public List<TileInteraction> currentPathTiles = new List<TileInteraction>();
    public List<TileInteraction> allTiles;

    //cube attributes
    public GameObject audioCube;
    private GameObject activeCube;

    public bool isSettingPath = false;

    void Start()
    {
        var gridManager = Object.FindAnyObjectByType<AudioGridManager>();
        if (gridManager != null) allTiles = gridManager.allTiles;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1)) selectInstrument(0);
        if (Input.GetKeyDown(KeyCode.Alpha2)) selectInstrument(1);
        if (Input.GetKeyDown(KeyCode.Alpha3)) selectInstrument(2);
        if (Input.GetKeyDown(KeyCode.Alpha4)) selectInstrument(3);
        if (Input.GetKeyDown(KeyCode.Alpha5)) selectInstrument(4);
        if (Input.GetKeyDown(KeyCode.Alpha6)) selectInstrument(5);
    }

    public void selectInstrument(int index)
    {
        selectedInstrumentIndex = Mathf.Clamp(index, 0, cubePrefabs.Count - 1);
    }

    public void OnTileClicked(TileInteraction clickedTile)
    {
        if (currentState == EditorState.PlacingStart)
        {
            Vector3 spawnPos = clickedTile.transform.position + new Vector3(0, ProjectConfig.CubeDropHeight, 0);

            GameObject selectedPrefab = cubePrefabs[selectedInstrumentIndex];
            activeCube = Instantiate(selectedPrefab, spawnPos, Quaternion.identity);

            activeCube.GetComponent<AudioCube>().assignedGridIndex = clickedTile.parentMeasureIndex;

            Rigidbody rb = activeCube.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
            }

            currentPathTiles.Add(clickedTile);

            UpdateLiveMasterLength(currentPathTiles.Count);

            currentState = EditorState.DrawingPath;
            HighlightNeighbors(clickedTile);
        }
        else if (currentState == EditorState.DrawingPath)
        {
            TileInteraction lastTile = currentPathTiles[currentPathTiles.Count - 1];
            float dist = Vector3.Distance(clickedTile.transform.position, lastTile.transform.position);
            
            if (dist <= 1.75f)
            {
                currentPathTiles.Add(clickedTile);

                UpdateLiveMasterLength(currentPathTiles.Count);

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

    private void UpdateLiveMasterLength(int length)
    {
        if ((float)length > GlobalClock.MasterBeatLength)
        {
            GlobalClock.MasterBeatLength = (float)length;
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
        if (pathLength <= 0) return;

        List<Vector3> worldPositions = new List<Vector3>();
        foreach(var tile in currentPathTiles)
        {
            Vector3 nodePos = tile.transform.position + new Vector3(0, ProjectConfig.CubeHoverHeight, 0);
            worldPositions.Add(nodePos);
        }

        AudioCube myCube = activeCube.GetComponent<AudioCube>();
        myCube.SetPath(worldPositions);

        ClearAllHighlights();
        currentPathTiles.Clear();
        isSettingPath = false;
        currentState = EditorState.Idle;

        myCube.isFinalized = true;

        SequenceMaster.RecalculateTimeline();
    }

    void HighlightNeighbors(TileInteraction centerTile)
    {
        foreach(var t in allTiles)
        {
            t.ResetColor();
            int dx = Mathf.Abs(t.gridX - centerTile.gridX);
            int dz = Mathf.Abs(t.gridZ - centerTile.gridZ);

            if (dx <= 1 && dz <= 1) //&& t != centerTile && !currentPathTiles.Contains(t)
            {
                t.SetColor(Color.yellow);
            }
        }
    }

    public void ClearAllHighlights()
    {
        if (allTiles == null) return;

        foreach(var tile in allTiles)
        {
            tile.ResetColor();
        }
    }

    public void ClearAllPaths()
    {
        AudioCube[] allCubes = Object.FindObjectsByType<AudioCube>(FindObjectsSortMode.None);
        foreach(var cube in allCubes)
        {
            Destroy(cube.gameObject);
        }

        currentPathTiles.Clear();

        TileInteraction[] allTiles = Object.FindObjectsByType<TileInteraction>(FindObjectsSortMode.None);
        foreach(var tile in allTiles)
        {
            tile.ResetTile();
        }

        GlobalClock.MasterBeatLength = 4f;
        GlobalClock.CurrentBeat = 0f;
        GlobalClock.IsPlaying = false;
    }

    public AudioSource GetSelectedAudioSource()
    {
        return cubePrefabs[selectedInstrumentIndex].GetComponent<AudioSource>();;
    }
}

