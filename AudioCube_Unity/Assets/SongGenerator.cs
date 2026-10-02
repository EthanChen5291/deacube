using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Turns a vibe prompt into a chord progression via Gemini (OpenAI-compatible endpoint),
/// with a fully offline generator as fallback so the app always works. Both paths start the song with SongManager.StartFreshSong
/// (v3: only the first island is built; the rest of the progression becomes the island tray's deck).
/// Fixes (v3 review): every request has an id; <see cref="Cancel"/> (the prompt closed, Learn) stops it, aborts the web request and
/// retires the id, and a result is applied only while its id is still the live one (S1 / F3); a song with cubes that a result
/// replaces is copied to a rotating backup first (F15).
/// </summary>
public class SongGenerator : MonoBehaviour
{
    [Header("References")]
    public SongManager songManager;

    [Header("API Settings")]
    public string apiUrl = "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions";
    public string model = "gemini-3.1-flash-lite-preview";
    private string apiKey;

    [TextArea(5, 10)]
    public string systemPrompt = @"You are a music theory API for a spatial 3D sequencer.
Generate a 4-8 measure chord progression capturing the user's requested vibe.
Response MUST be a raw JSON object. NO conversational text, NO markdown.

CRITICAL RULES:
1. 'chordRootMIDI' is MANDATORY. It MUST be an integer between 48 and 72. (C=60, C#=61, D=62, D#=63, E=64, F=65, F#=66, G=67, G#=68, A=69, A#=70, B=71).
2. 'semitones' must be intervals RELATIVE to the root. The array MUST always start with 0. Use 4 or 5 notes.
3. Use rich voicings! Use 7ths, 9ths, and 11ths for jazz/space vibes. (e.g., Major 9th = [0, 4, 7, 11, 14]).
4. 'bpm' between 70 and 140.

PERFECT JSON EXAMPLE:
{
  ""songName"": ""Deep Space"",
  ""bpm"": 90,
  ""timeSignature"": ""4/4"",
  ""measures"": [
    { ""index"": 1, ""chordKey"": ""Cm9"", ""chordRootMIDI"": 60, ""semitones"": [0, 3, 7, 10, 14], ""measureDuration"": 4.0 },
    { ""index"": 2, ""chordKey"": ""Abmaj7"", ""chordRootMIDI"": 56, ""semitones"": [0, 4, 7, 11], ""measureDuration"": 4.0 }
  ]
}";

    [Serializable] class ChatMessage { public string role; public string content; }
    [Serializable] class ChatRequest { public string model; public ChatMessage[] messages; public float temperature; }
    [Serializable] public class OpenAIResponse { public AIChoice[] choices; }
    [Serializable] public class AIChoice { public AIMessage message; }
    [Serializable] public class AIMessage { public string content; }

    bool isGenerating;
    public bool IsGenerating => isGenerating;
    public bool HasApiKey => !string.IsNullOrEmpty(apiKey);
    public string LastError { get; private set; }
    int requestId; Coroutine running; UnityWebRequest pending;
    /// <summary>S1: the id of the newest request (a cancel retires it: a late result with an older id is dropped).</summary>
    public int RequestId => requestId;
    /// <summary>Results dropped because their request was cancelled or superseded (tests).</summary>
    public int Dropped { get; private set; }

    void Awake()
    {
        string path = Path.Combine(Application.streamingAssetsPath, "api_config.env");
        if (File.Exists(path))
        {
            foreach (string line in File.ReadAllLines(path))
            {
                if (line.StartsWith("GEMINI_API_KEY=")) { apiKey = line.Substring("GEMINI_API_KEY=".Length).Trim().Trim('"'); break; }
            }
        }
        if (!HasApiKey) Debug.LogWarning("No GEMINI_API_KEY in StreamingAssets/api_config.env: using the offline progression generator.");
    }

    SongManager Target => songManager != null ? songManager : (songManager = SongManager.I != null ? SongManager.I : FindAnyObjectByType<SongManager>());

    /// <summary>Starts a generation (online with a key, else offline); returns its request id (0 = not started: one is running).</summary>
    public int GenerateNewSong(string userVibePrompt, Action<bool> onComplete)
    {
        if (isGenerating) return 0;
        isGenerating = true;
        LastError = null;
        int id = ++requestId;
        if (!HasApiKey) { running = StartCoroutine(OfflineRoutine(userVibePrompt, id, onComplete)); return id; }
        running = StartCoroutine(SendRequestToLLM(userVibePrompt, id, ok => { if (id != requestId) return; isGenerating = false; running = null; onComplete?.Invoke(ok); }));
        return id;
    }

    /// <summary>The dice song (no network); returns its request id (0 = not started: one is running).</summary>
    public int GenerateOffline(string prompt, Action<bool> onComplete)
    {
        if (isGenerating) return 0;
        isGenerating = true;
        int id = ++requestId;
        running = StartCoroutine(OfflineRoutine(prompt, id, onComplete));
        return id;
    }

    /// <summary>S1: stops the running request (coroutine and web request) and retires its id: nothing it would have produced is
    /// applied, no callback runs.</summary>
    public void Cancel()
    {
        if (!isGenerating && running == null && pending == null) return;
        requestId++;
        if (running != null) { StopCoroutine(running); running = null; }
        if (pending != null) { var r = pending; pending = null; r.Abort(); r.Dispose(); }   // a stopped coroutine never leaves its using block
        isGenerating = false;
    }

    /// <summary>Applies a generated song if request <paramref name="id"/> is still the live one (else it is dropped): the song it
    /// replaces is backed up first when it has cubes (F15).</summary>
    bool ApplySong(int id, SongManager.SongData song)
    {
        if (id != requestId || !isGenerating) { Dropped++; return false; }
        SongIO.Backup();
        Target.StartFreshSong(song);   // v3 (SPEC v3 §3.2): island 0 + the demo Moon; the rest goes to the island tray
        return true;
    }

    IEnumerator OfflineRoutine(string prompt, int id, Action<bool> onComplete)
    {
        yield return new WaitForSeconds(0.4f);
        if (id != requestId) { Dropped++; yield break; }   // cancelled meanwhile
        var song = MusicTheory.RandomSong(MusicTheory.SeedFromPrompt(prompt + DateTime.Now.Ticks), prompt);
        ApplySong(id, song);
        isGenerating = false; running = null;
        onComplete?.Invoke(true);
    }

    IEnumerator SendRequestToLLM(string userPrompt, int id, Action<bool> onComplete)
    {
        var req = new ChatRequest
        {
            model = model,
            messages = new[] { new ChatMessage { role = "system", content = systemPrompt }, new ChatMessage { role = "user", content = userPrompt } },
            temperature = 0.75f
        };
        string jsonPayload = JsonUtility.ToJson(req);
        using (UnityWebRequest request = new UnityWebRequest(apiUrl, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonPayload));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + apiKey);
            request.timeout = 40;
            pending = request;
            yield return request.SendWebRequest();
            if (pending == request) pending = null;
            if (id != requestId) { Dropped++; yield break; }   // cancelled while waiting: the result is not ours to apply

            if (request.result != UnityWebRequest.Result.Success)
            {
                LastError = request.error;
                Debug.LogWarning("Song API error: " + request.error + "\n" + request.downloadHandler.text);
                onComplete?.Invoke(false);
            }
            else onComplete?.Invoke(ProcessAIResponse(request.downloadHandler.text, id));
        }
    }

    bool ProcessAIResponse(string rawJson, int id)
    {
        try
        {
            var envelope = JsonUtility.FromJson<OpenAIResponse>(rawJson);
            string nested = FilterForData(envelope.choices[0].message.content);
            var song = JsonUtility.FromJson<SongManager.SongData>(nested);
            if (Validate(song)) return ApplySong(id, song);   // v3: island 0 + the demo Moon; the rest goes to the island tray
            LastError = "Malformed song data";
        }
        catch (Exception e) { LastError = e.Message; Debug.LogWarning("Song JSON error: " + e.Message + "\n" + rawJson); }
        return false;
    }

    /// <summary>Tests (no network): runs a finished online response through the same guarded path; false = dropped / malformed.</summary>
    public bool SimResponse(string rawJson, int id) { return ProcessAIResponse(rawJson, id); }

    static bool Validate(SongManager.SongData s)
    {
        if (s == null || s.measures == null || s.measures.Length == 0) return false;
        foreach (var m in s.measures)
        {
            if (m.semitones == null || m.semitones.Length == 0) m.semitones = new[] { 0, 4, 7 };
            if (m.chordRootMIDI < 36 || m.chordRootMIDI > 84) m.chordRootMIDI = 60;
            if (m.bars <= 0) m.bars = 1;
        }
        if (s.bpm < 40 || s.bpm > 240) s.bpm = 100;
        if (string.IsNullOrEmpty(s.timeSignature)) s.timeSignature = "4/4";
        return true;
    }

    public string FilterForData(string message)
    {
        int startIndex = message.IndexOf('{');
        int endIndex = message.LastIndexOf('}');
        if (startIndex != -1 && endIndex > startIndex) return message.Substring(startIndex, endIndex - startIndex + 1);
        return "";
    }
}
