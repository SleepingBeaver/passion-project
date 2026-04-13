using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class CursorVisualController : MonoBehaviour
{
    // Modos disponiveis para o tamanho do cursor customizado.
    private enum CursorSizeMode
    {
        NativeSize = 0,
        FixedSize = 1
    }

    // Referencias do canvas onde o cursor sera desenhado.
    [Header("References")]
    [SerializeField] private Canvas targetCanvas;
    [SerializeField] private RectTransform cursorContainer;
    [SerializeField] private Image cursorImage;

    // Configuracao visual do cursor.
    [Header("Visual")]
    [SerializeField] private Sprite defaultCursorSprite;
    [SerializeField] private Color cursorTint = Color.white;
    [SerializeField] private CursorSizeMode sizeMode = CursorSizeMode.NativeSize;
    [SerializeField] private Vector2 fixedSize = new Vector2(48f, 48f);
    [SerializeField] private Vector2 hotspotPixels = Vector2.zero;
    [SerializeField] private bool hideSystemCursorWhenCustomCursorIsActive = true;
    [SerializeField] private bool keepCursorOnTop = true;

    // Estado interno do cursor em runtime.
    private Sprite activeCursorSprite;
    private Mouse mouse;
    private RectTransform targetCanvasRect;

    // Ciclo de vida.
    private void Awake()
    {
        mouse = Mouse.current;
        ResolveReferences();
        EnsureCursorGraphic();

        if (activeCursorSprite == null)
            activeCursorSprite = defaultCursorSprite;

        ApplyCursorVisual();
        UpdateCursorPosition();
    }

    private void OnEnable()
    {
        mouse = Mouse.current;
        ResolveReferences();
        EnsureCursorGraphic();

        if (activeCursorSprite == null)
            activeCursorSprite = defaultCursorSprite;

        ApplyCursorVisual();
        UpdateCursorPosition();
    }

    private void LateUpdate()
    {
        mouse ??= Mouse.current;
        UpdateCursorPosition();

        if (keepCursorOnTop &&
            cursorContainer != null &&
            cursorContainer.parent != null &&
            cursorContainer.GetSiblingIndex() != cursorContainer.parent.childCount - 1)
            cursorContainer.SetAsLastSibling();
    }

    private void OnDisable()
    {
        SetSystemCursorVisible(true);
        SetCursorGraphicVisible(false);
    }

    // API publica para trocar o cursor em tempo real.
    public void SetCursorSprite(Sprite cursorSprite)
    {
        activeCursorSprite = cursorSprite;
        ApplyCursorVisual();
    }

    public void SetCursorStyle(Sprite cursorSprite, Vector2 hotspot)
    {
        hotspotPixels = hotspot;
        SetCursorSprite(cursorSprite);
    }

    public void ResetToDefaultCursor()
    {
        activeCursorSprite = defaultCursorSprite;
        ApplyCursorVisual();
    }

    public void ClearCustomCursor()
    {
        activeCursorSprite = null;
        ApplyCursorVisual();
    }

    // Montagem automatica do elemento visual no canvas.
    private void ResolveReferences()
    {
        if (targetCanvas == null)
            targetCanvas = GetComponent<Canvas>();

        if (targetCanvas == null)
            targetCanvas = GetComponentInParent<Canvas>();

        if (cursorContainer == null && cursorImage != null)
            cursorContainer = cursorImage.rectTransform;

        if (cursorImage == null && cursorContainer != null)
            cursorImage = cursorContainer.GetComponent<Image>();

        targetCanvasRect = targetCanvas != null ? targetCanvas.transform as RectTransform : null;
    }

    private void EnsureCursorGraphic()
    {
        if (targetCanvas == null)
        {
            Debug.LogWarning("CursorVisualController precisa de um Canvas para desenhar o cursor.");
            return;
        }

        if (cursorContainer == null)
        {
            Transform existingCursor = targetCanvas.transform.Find("CursorVisual");

            if (existingCursor != null)
                cursorContainer = existingCursor as RectTransform;
        }

        if (cursorContainer == null)
        {
            GameObject cursorObject = new GameObject("CursorVisual", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            cursorObject.layer = targetCanvas.gameObject.layer;

            cursorContainer = cursorObject.GetComponent<RectTransform>();
            cursorContainer.SetParent(targetCanvas.transform, false);
        }

        if (cursorImage == null)
            cursorImage = cursorContainer.GetComponent<Image>();

        if (cursorImage == null)
            cursorImage = cursorContainer.gameObject.AddComponent<Image>();

        cursorContainer.anchorMin = Vector2.zero;
        cursorContainer.anchorMax = Vector2.zero;
        cursorContainer.pivot = new Vector2(0f, 1f);
        cursorContainer.localScale = Vector3.one;

        cursorImage.preserveAspect = true;
        cursorImage.raycastTarget = false;
        cursorImage.color = cursorTint;
    }

    // Atualizacao visual e posicionamento do cursor.
    private void ApplyCursorVisual()
    {
        if (cursorImage == null)
            return;

        bool hasCustomCursor = activeCursorSprite != null;

        cursorImage.sprite = activeCursorSprite;
        cursorImage.color = cursorTint;

        if (hasCustomCursor)
            ApplyCursorSizing(activeCursorSprite);

        SetCursorGraphicVisible(hasCustomCursor);
        SetSystemCursorVisible(!hasCustomCursor || !hideSystemCursorWhenCustomCursorIsActive);
    }

    private void ApplyCursorSizing(Sprite cursorSprite)
    {
        if (cursorContainer == null || cursorImage == null || cursorSprite == null)
            return;

        if (sizeMode == CursorSizeMode.NativeSize)
        {
            cursorImage.SetNativeSize();
        }
        else
        {
            cursorContainer.sizeDelta = fixedSize;
        }
    }

    private void UpdateCursorPosition()
    {
        if (targetCanvas == null || cursorContainer == null || mouse == null)
            return;

        if (targetCanvasRect == null)
            return;

        Camera eventCamera = targetCanvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : targetCanvas.worldCamera;

        Vector2 screenPosition = mouse.position.ReadValue();

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(targetCanvasRect, screenPosition, eventCamera, out Vector2 localPoint))
            return;

        float scaleFactor = targetCanvas.scaleFactor <= 0f ? 1f : targetCanvas.scaleFactor;
        Vector2 scaledHotspot = hotspotPixels / scaleFactor;
        Vector2 canvasPosition = localPoint - targetCanvasRect.rect.min;

        cursorContainer.anchoredPosition = canvasPosition + new Vector2(-scaledHotspot.x, scaledHotspot.y);
    }

    // Utilitarios de visibilidade.
    private void SetCursorGraphicVisible(bool value)
    {
        if (cursorContainer != null && cursorContainer.gameObject.activeSelf != value)
            cursorContainer.gameObject.SetActive(value);
    }

    private static void SetSystemCursorVisible(bool value)
    {
        Cursor.visible = value;
    }
}
