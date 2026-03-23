using UnityEngine;
using TMPro;

public class UIManager : MonoBehaviour
{
    private PathManager pathManager;
    public TextMeshProUGUI buttonText;

    void Start()
    {
        pathManager = Object.FindAnyObjectByType<PathManager>();
    }

    public void TogglePathMode()
    {
        pathManager.isSettingPath = !pathManager.isSettingPath;

        if (pathManager.isSettingPath)
        {
            pathManager.currentState = PathManager.EditorState.PlacingStart;
            buttonText.text = "CANCEL";
            buttonText.color = Color.red;
        }
        else
        {
            pathManager.currentState = PathManager.EditorState.Idle;
            buttonText.text = "DRAW PATH";
            buttonText.color = Color.white;
        }
    }
}
