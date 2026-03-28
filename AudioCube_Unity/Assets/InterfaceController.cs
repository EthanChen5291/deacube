using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class InterfaceController : MonoBehaviour
{
    [Header("UI Panels")]
    public GameObject inputPanel;
    public GameObject loadingPanel;
    public GameObject gamePanel; // If you have one, otherwise leave null

    [Header("Input Elements")]
    public TMP_InputField promptInput;
    public Button generateButton;
    public TMP_Text errorText;

    [Header("System References")]
    public SongGenerator generator;

    void Start()
    {
        inputPanel.SetActive(true);
        loadingPanel.SetActive(false);
        if (gamePanel != null) gamePanel.SetActive(false);
        if (errorText != null) errorText.text = "";

        generateButton.onClick.AddListener(OnGenerateClicked);
    }

    private void OnGenerateClicked()
    {
        string prompt = promptInput.text;
        if (string.IsNullOrEmpty(prompt)) return;

        inputPanel.SetActive(false);
        loadingPanel.SetActive(true);
        if (errorText != null) errorText.text = "";

        generator.GenerateNewSong(prompt, OnSongReady);
    }

    private void OnSongReady(bool success)
    {
        loadingPanel.SetActive(false);

        if (success)
        {
            if (gamePanel != null) gamePanel.SetActive(true);
        }
        else
        {
            inputPanel.SetActive(true);
            if (errorText != null) errorText.text = "Signal lost. Try again.";
        }
    }
}