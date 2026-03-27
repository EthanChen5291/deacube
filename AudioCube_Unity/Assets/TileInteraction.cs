using UnityEngine;
using System.Collections;

public class TileInteraction : MonoBehaviour
{
    // grid coordinates
    public int gridX;
    public int gridZ;
    private Vector3 initialPosition;

    // path logic
    private PathManager pathManager;

    // visuals
    private Color originalColor;
    private Material mat;

    // movement physics
    public float pressDepth = 0.2f;
    public float pressSpeed = 20f;
    public float returnSpeed = 8f;

    // audio
    public float myFrequency;

    [Header("Audio Clips")]
    public AudioClip previewClip;
    public AudioSource audioSource;

    void Start()
    {
        initialPosition = transform.position;
        pathManager = Object.FindAnyObjectByType<PathManager>();

        if (audioSource == null) audioSource = GetComponent<AudioSource>();

        mat = GetComponent<Renderer>().material;
        originalColor = mat.color;
    }

    public void ResetColor() => mat.color = originalColor;
    public void SetColor(Color newCol) => mat.color = newCol;

    public float GetPitch()
    {
        return myFrequency / ProjectConfig.refFreq;
    }

    public void PlayNote()
    {
        audioSource.pitch = GetPitch();
        AudioClip currentInstrumentClip = pathManager.GetSelectedAudioSource().clip;
        audioSource.PlayOneShot(currentInstrumentClip);

        StopAllCoroutines();
        StartCoroutine(AnimatePress());
    }

    public void PlayPreviewNote()
    {
        audioSource.pitch = GetPitch();
        audioSource.PlayOneShot(previewClip);
        
        StopAllCoroutines();
        StartCoroutine(AnimatePress());
    }

    void OnMouseDown()
    {
        if (pathManager.isSettingPath)
        {
            pathManager.OnTileClicked(this);

            PlayNote();
        } 
        else
        {
            PlayPreviewNote();
        }
    }

    IEnumerator AnimatePress()
    {
        Vector3 sunkenPosition = initialPosition + new Vector3(0, -pressDepth, 0);
        
        while (Vector3.Distance(transform.position, sunkenPosition) > 0.01f)
        {
            transform.position = Vector3.Lerp(
                transform.position,
                sunkenPosition,
                Time.deltaTime * pressSpeed
            );
            yield return null;
        }
        transform.position = sunkenPosition;

        yield return new WaitForSeconds(0.05f); // should change once cube paths are created

        while (Vector3.Distance(transform.position, initialPosition) > 0.001f)
        {
            transform.position = Vector3.Lerp(
                transform.position,
                initialPosition,
                Time.deltaTime * returnSpeed
            );
            yield return null;
        }

        transform.position = initialPosition;
    }

    public void ResetTile()
    {
        StopAllCoroutines();

        transform.position = initialPosition;

        ResetColor();
    }

    public void SetNote(float frequency)
    {
        myFrequency = frequency;
    }
}
