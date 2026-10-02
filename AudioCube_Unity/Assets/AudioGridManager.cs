using UnityEngine;
using System.Collections.Generic;

/// <summary>Legacy flat-grid builder. Islands are now built by KeyBlock; kept so old prefabs keep their references.</summary>
public class AudioGridManager : MonoBehaviour
{
    public GameObject noteTemplate;
    public List<TileInteraction> allTiles = new List<TileInteraction>();
}
