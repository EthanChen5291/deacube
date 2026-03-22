using UnityEngine;
using System.Collections;

public class TileInteraction : MonoBehaviour
{
    private Vector3 initialPosition;
    public float pressDepth = 0.2f;
    public float pressSpeed = 20f;
    public float returnSpeed = 8f;

    public float myFrequency;
    private AudioSource audioSource;

    void Start()
    {
        initialPosition = transform.position;
        audioSource = GetComponent<AudioSource>();
    }

    void PlayNote()
    {
        audioSource.pitch = myFrequency / ProjectConfig.refFreq;
        audioSource.Play();

        StopAllCoroutines();
        StartCoroutine(AnimatePress());
    }

    void OnTriggerEnter(Collider other) // for whenever paths are created
    {
        if (other.CompareTag("AudioCube"))
        {
            PlayNote();
        }
    }

    void OnMouseDown()
    {
        PlayNote();
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
}
