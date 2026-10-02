using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// v4 UI kit (scratchpad/v4/UI.md): words on demand. One pooled caption — a small inked narration box with a lowercase word or two — shown
/// after <see cref="Delay"/> seconds of hovering a control, on the side with the most room, never at rest. <see cref="Attach"/> adds the
/// hover behaviour to any UI object. Captions can be switched off (menu → settings): PlayerPrefs "deacube.captions".
/// </summary>
public class InkCaption : MonoBehaviour
{
    public const float Delay = 0.35f;
    public const string PrefKey = "deacube.captions";
    public static bool Enabled { get => PlayerPrefs.GetInt(PrefKey, 1) != 0; set { PlayerPrefs.SetInt(PrefKey, value ? 1 : 0); if (!value) Hide(null); } }

    static InkCaption I;
    Canvas canvas; RectTransform box; InkShape plate; TextMeshProUGUI text; CanvasGroup group;
    RectTransform target, pending; string pendingText; float pendingT, popT;

    /// <summary>Shows <paramref name="words"/> next to <paramref name="target"/> after the hover delay (the hover itself is the caller's).</summary>
    public static void Show(RectTransform target, string words)
    {
        if (!Enabled || target == null || string.IsNullOrEmpty(words)) return;
        var c = Ensure();
        c.pending = target; c.pendingText = words; c.pendingT = 0f;
    }

    /// <summary>Hides the caption of <paramref name="target"/> (null = whatever is shown).</summary>
    public static void Hide(RectTransform target)
    {
        if (I == null) return;
        if (target == null || I.pending == target) { I.pending = null; }
        if (target == null || I.target == target) { I.target = null; if (I.group != null) I.group.alpha = 0f; }
    }

    public static bool Showing => I != null && I.target != null && I.group != null && I.group.alpha > 0.5f;

    /// <summary>Adds hover captions to <paramref name="go"/> (pointer enter → Show after the delay, exit / press → Hide).</summary>
    public static void Attach(GameObject go, string words)
    {
        if (go == null) return;
        var h = go.GetComponent<InkCaptionHover>();
        if (h == null) h = go.AddComponent<InkCaptionHover>();
        h.words = words;
    }

    static InkCaption Ensure()
    {
        if (I != null) return I;
        var go = new GameObject("InkCaption");
        DontDestroyOnLoad(go);
        I = go.AddComponent<InkCaption>();
        I.Build();
        return I;
    }

    void Build()
    {
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 400;
        var scaler = gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920f, 1080f); scaler.matchWidthOrHeight = 0.5f;
        var bgo = new GameObject("Box", typeof(RectTransform));
        bgo.transform.SetParent(transform, false);
        box = (RectTransform)bgo.transform;
        group = bgo.AddComponent<CanvasGroup>(); group.alpha = 0f; group.blocksRaycasts = false; group.interactable = false;
        plate = InkShape.Create(box, "Plate", InkShape.Kind.Sticker, Comic.Cream, new Vector2(100f, 34f));
        plate.raycastTarget = false; plate.SetInk(1.6f, 2.6f); plate.ShadowOffset = new Vector2(3f, -4f);
        var prt = plate.rectTransform; prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one; prt.offsetMin = Vector2.zero; prt.offsetMax = Vector2.zero;
        var tgo = new GameObject("Text", typeof(RectTransform));
        tgo.transform.SetParent(box, false);
        text = tgo.AddComponent<TextMeshProUGUI>();
        text.font = Comic.Font; text.fontSize = 17f; text.color = Comic.Ink; text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false;
        text.enableWordWrapping = false;
        var trt = text.rectTransform; trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = new Vector2(10f, 4f); trt.offsetMax = new Vector2(-10f, -4f);
    }

    void LateUpdate()
    {
        if (pending != null)
        {
            pendingT += Time.unscaledDeltaTime;
            if (!pending.gameObject.activeInHierarchy) pending = null;
            else if (pendingT >= Delay) { target = pending; pending = null; text.text = pendingText.ToLowerInvariant(); popT = 0f; }
        }
        if (target == null || !target.gameObject.activeInHierarchy) { target = null; group.alpha = 0f; return; }
        // size to the text, place on the side with the most room (above by default)
        float w = Mathf.Max(56f, text.preferredWidth + 22f), h = 34f;
        box.sizeDelta = new Vector2(w, h);
        var corners = new Vector3[4];
        target.GetWorldCorners(corners);
        Camera cam = null;
        var tc = target.GetComponentInParent<Canvas>();
        if (tc != null && tc.renderMode != RenderMode.ScreenSpaceOverlay) cam = tc.worldCamera;
        Vector2 min = RectTransformUtility.WorldToScreenPoint(cam, corners[0]), max = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
        float scale = canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        Vector2 centre = (min + max) * 0.5f;
        Vector2 pos;
        if (max.y + (h + 14f) * scale < Screen.height) pos = new Vector2(centre.x, max.y + (h * 0.5f + 10f) * scale);
        else pos = new Vector2(centre.x, min.y - (h * 0.5f + 10f) * scale);
        pos.x = Mathf.Clamp(pos.x, (w * 0.5f + 8f) * scale, Screen.width - (w * 0.5f + 8f) * scale);
        box.position = pos;
        // pop in over two stepped frames
        popT += Time.unscaledDeltaTime;
        float k = popT < 1f / 12f ? 0.6f : (popT < 2f / 12f ? 1.08f : 1f);
        box.localScale = Vector3.one * k;
        group.alpha = 1f;
    }
}

/// <summary>Hover glue for <see cref="InkCaption.Attach"/>.</summary>
public class InkCaptionHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler
{
    public string words;
    public void OnPointerEnter(PointerEventData e) { InkCaption.Show(transform as RectTransform, words); }
    public void OnPointerExit(PointerEventData e) { InkCaption.Hide(transform as RectTransform); }
    public void OnPointerDown(PointerEventData e) { InkCaption.Hide(transform as RectTransform); }
    void OnDisable() { InkCaption.Hide(transform as RectTransform); }
}
