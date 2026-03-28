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
    public string apiKey = "AIzaSyAL_voMyEwC1a8-w6RvSjOeggd52FVHRXI";

    [TextArea(5, 10)]
    public string systemPrompt = @"You are a music theory API. 
Generate a 4-10 chord progression.
Response MUST be a raw JSON object. NO conversational text.

CRITICAL: You must include the 'chordRootMIDI' field for every measure. 
Example MIDI values: C=60, C#=61, D=62, D#=63, E=64, F=65, F#=66, G=67, G#=68, A=69, A#=70, B=71.
JSON Schema:
{
  ""songName"": ""String"",
  ""bpm"": Int,
  ""timeSignature"": ""String (e.g., 4/4)"",
  ""measures"": [
    { ""index"": Int, ""chordKey"": ""String"", ""chordRootMIDI"": Int (MUST be between 48 and 72), ""semitones"": [Int, Int, Int], ""measureDuration"": Float }
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

    public void GenerateNewSong(string userVibePrompt, Action<bool> onComplete)
    {
        Debug.Log("Asking AI for: " + userVibePrompt);
        StartCoroutine(SendRequestToLLM(userVibePrompt, onComplete));
    }

    private IEnumerator SendRequestToLLM(string userPrompt, Action<bool> onComplete)
    {
        string safeSystemPrompt = systemPrompt.Replace("\"", "\\\"").Replace("\n", " ");
        string safeUserPrompt = userPrompt.Replace("\"", "\\\""); 

        string jsonPayload = $@"{{
            ""model"": ""gemini-2.5-flash"",
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