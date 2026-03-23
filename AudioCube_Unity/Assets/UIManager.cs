using UnityEngine;
using TMPro;

public class UIManager : MonoBehaviour
{
    public PathManager pathManager;
    public TextMeshProUGUI buttonText;

    void Start()
    {
        if (pathManager == null)
        {
            pathManager = Object.FindAnyObjectByType<PathManager>();
        }
    }

    public void TogglePathMode()
    {

        if (!pathManager.isSettingPath)
        {   
            pathManager.isSettingPath = true;
            pathManager.currentState = PathManager.EditorState.PlacingStart;

            buttonText.text = "CANCEL";
            buttonText.color = Color.red;
        }
        else
        {
            pathManager.isSettingPath = false;
            pathManager.currentState = PathManager.EditorState.Idle;

            pathManager.FinalizePath();

            buttonText.text = "DRAW PATH";
            buttonText.color = Color.white;
        }
    }
}
