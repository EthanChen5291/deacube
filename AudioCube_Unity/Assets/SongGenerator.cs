using System;
using System.Collections;
using System.ComponentModel.Design.Serialization;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class SongGenerator : MonoBehaviour
{
    [Header("References")]
    public SongManager songManager;

    [Header("API Settings")]
    public string apiUrl = "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions";
    public string apiKey = "";

    [TextArea(5, 10)]
    public string systemPrompt = @"You are a music theory API. 
Generate a 4-10 chord progression.
Response MUST be a raw JSON object. NO conversational text.

CRITICAL RULES:
1. 'chordRootMIDI' is MANDATORY. It MUST be an integer between 48 and 72. (C=60, C#=61, D=62, D#=63, E=64, F=65, F#=66, G=67, G#=68, A=69, A#=70, B=71).
2. 'semitones' must be intervals RELATIVE to the root. The array MUST always start with 0. 
3. Use rich voicings! Use 7ths, 9ths, and 11ths for jazz/space vibes. (e.g., Major 9th = [0, 4, 7, 11, 14]).

PERFECT JSON EXAMPLE:
{
  ""songName"": ""Deep Space"",
  ""bpm"": 90,
  ""timeSignature"": ""4/4"",
  ""measures"": [
    {
      ""index"": 1,
      ""chordKey"": ""Cm9"",
      ""chordRootMIDI"": 60,
      ""semitones"": [0, 3, 7, 10, 14],
      ""measureDuration"": 4.0
    },
    {
      ""index"": 2,
      ""chordKey"": ""Abmaj7"",
      ""chordRootMIDI"": 56,
      ""semitones"": [0, 4, 7, 11],
      ""measureDuration"": 4.0
    }
  ]
}";

    [System.Serializable]
    public class OpenAIResponse
    {
        public AIChoice[] choices;
    }

    [System.Serializable]
    public class AIChoice
    {
        public AIMessage message;
    }

    [System.Serializable]
    public class AIMessage
    {
        public string content;
    }

    private bool isGenerating = false;

    public void GenerateNewSong(string userVibePrompt, Action<bool> onComplete)
    {
        if (isGenerating)
        {
            Debug.LogWarning("Chill! The AI is already thinking...");
            return;
        }

        isGenerating = true;
        Debug.Log("Asking AI for: " + userVibePrompt);
        
        StartCoroutine(SendRequestToLLM(userVibePrompt, (success) => 
        {
            isGenerating = false;
            onComplete?.Invoke(success);
        }));
    }

    private IEnumerator SendRequestToLLM(string userPrompt, Action<bool> onComplete)
    {
        string safeSystemPrompt = systemPrompt.Replace("\"", "\\\"").Replace("\n", " ");
        string safeUserPrompt = userPrompt.Replace("\"", "\\\""); 

        string jsonPayload = $@"{{
            ""model"": ""gemini-3.1-flash-lite-preview"",
            ""messages"": [
                {{""role"": ""system"", ""content"": ""{safeSystemPrompt}""}},
                {{""role"": ""user"", ""content"": ""{safeUserPrompt}""}}
            ],
            ""temperature"": 0.7
        }}";

        using (UnityWebRequest request = new UnityWebRequest(apiUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + apiKey);

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError("API Error: " + request.error);
                Debug.LogError("Response: " + request.downloadHandler.text);
                onComplete?.Invoke(false);
            }
            else
            {
                string resultText = request.downloadHandler.text;

                Debug.Log("Raw AI Response: " + resultText);
                ProcessAIResponse(resultText);
                onComplete?.Invoke(true);
            }
        }
    }

    private void ProcessAIResponse(string rawJson)
    {
        try
        {
            OpenAIResponse envelope = JsonUtility.FromJson<OpenAIResponse>(rawJson);
            string nestedJson = envelope.choices[0].message.content;

            nestedJson = FilterForData(nestedJson);
            SongManager.SongData newSong = JsonUtility.FromJson<SongManager.SongData>(nestedJson);

            if (newSong != null && newSong.measures.Length > 0)
            {
                Debug.Log($"Successfully parsed song: {newSong.songName}");
                songManager.StartNewSong(newSong);
            }
            else
            {
                Debug.LogError("Failed to parse JSON into SongData. Check the schema.");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError("JSON Parsing Error: " + e.Message);
            Debug.LogError("Raw Nested JSON was: " + rawJson);
        }
    }

    public string FilterForData(string message)
    {
        int startIndex = message.IndexOf('{');
        int endIndex = message.LastIndexOf('}');
        
        string filtered = "";
        
        if (startIndex != -1 && endIndex != -1)
        {
            filtered = message.Substring(startIndex, (endIndex - startIndex) + 1);
        }
        return filtered;
    }
}