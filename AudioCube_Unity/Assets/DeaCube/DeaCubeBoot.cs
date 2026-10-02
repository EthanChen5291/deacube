using UnityEngine;

/// <summary>Creates the runtime singletons that are not placed in the scene and sets the scene look. v3: also the main menu
/// (shown at boot instead of the vibe prompt, SPEC §5.1) and the onboarding tutorial (§5.2).</summary>
[DefaultExecutionOrder(-200)]
public class DeaCubeBoot : MonoBehaviour
{
    void Awake()
    {
        Application.targetFrameRate = 120;
        Application.runInBackground = true;   // keep the music running when the window loses focus
        var pm = FindAnyObjectByType<PathManager>();
        if (pm != null) Instruments.LoadFrom(pm.cubePrefabs);
        var fx = Fx.I;
        var pool = AudioPool.I;
        Synth.Ensure();                                          // loads the SoundFont synchronously (~70 ms)
        Synth.SetMasterGain(ProjectConfig.SynthMasterGain);
        Instruments.PushToSynth();
        if (!Synth.Ready) Debug.LogWarning("SoundFont missing: falling back to samples");
        GlobalClock.Ensure();
        if (FindAnyObjectByType<SequenceMaster>() == null) new GameObject("SequenceMaster").AddComponent<SequenceMaster>();
        if (FindAnyObjectByType<UIManager>() == null) new GameObject("HUD").AddComponent<UIManager>();
        if (FindAnyObjectByType<InterfaceController>() == null) new GameObject("PromptOverlay").AddComponent<InterfaceController>();
        if (FindAnyObjectByType<Onboarding>() == null) new GameObject("Onboarding").AddComponent<Onboarding>();
        if (FindAnyObjectByType<MainMenu>() == null) new GameObject("MainMenu").AddComponent<MainMenu>();   // shows itself in Start
        Fx.SetupEnvironment();
    }
}
