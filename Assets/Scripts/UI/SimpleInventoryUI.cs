using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class SimpleInventoryUI : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private GameObject inventoryUIRoot;

    [Header("Background")]
    [SerializeField] private CanvasGroup dimBackgroundCanvasGroup;
    [SerializeField] private float backgroundFadeDuration = 0.18f;
    [SerializeField, Range(0f, 1f)] private float backgroundMaxAlpha = 0.45f;

    [Header("Panel")]
    [SerializeField] private CanvasGroup inventoryPanelCanvasGroup;
    [SerializeField] private RectTransform inventoryPanelRect;
    [SerializeField] private float panelFadeDuration = 0.18f;
    [SerializeField] private float panelStartScale = 0.92f;
    [SerializeField] private float panelEndScale = 1f;

    [Header("Player")]
    [SerializeField] private Behaviour playerMovementBehaviour;

    [Header("Audio")]
    [SerializeField] private AudioSource uiAudioSource;
    [SerializeField] private AudioClip openClip;
    [SerializeField] private AudioClip closeClip;
    [SerializeField, Range(0f, 1f)] private float openVolume = 1f;
    [SerializeField, Range(0f, 1f)] private float closeVolume = 1f;

    private bool isOpen;
    private bool isTransitioning;

    private void Start()
    {
        if (!ValidateReferences())
        {
            enabled = false;
            return;
        }

        SetClosedStateImmediate();
    }

    private void Update()
    {
        if (Keyboard.current == null || isTransitioning)
            return;

        bool tabPressed = Keyboard.current.tabKey.wasPressedThisFrame;
        bool escapePressed = Keyboard.current.escapeKey.wasPressedThisFrame;

        if (!isOpen && tabPressed)
        {
            StartCoroutine(OpenInventoryRoutine());
        }
        else if (isOpen && (tabPressed || escapePressed))
        {
            StartCoroutine(CloseInventoryRoutine());
        }
    }

    private bool ValidateReferences()
    {
        if (inventoryUIRoot == null)
        {
            Debug.LogWarning("InventoryUIRoot não foi atribuído no Inspector.");
            return false;
        }

        if (dimBackgroundCanvasGroup == null)
        {
            Debug.LogWarning("DimBackground CanvasGroup não foi atribuído.");
            return false;
        }

        if (inventoryPanelCanvasGroup == null)
        {
            Debug.LogWarning("InventoryPanel CanvasGroup não foi atribuído.");
            return false;
        }

        if (inventoryPanelRect == null)
        {
            inventoryPanelRect = inventoryPanelCanvasGroup.transform as RectTransform;
        }

        if (inventoryPanelRect == null)
        {
            Debug.LogWarning("InventoryPanel RectTransform não foi encontrado.");
            return false;
        }

        return true;
    }

    private IEnumerator OpenInventoryRoutine()
    {
        isTransitioning = true;
        isOpen = true;

        inventoryUIRoot.SetActive(true);
        SetPlayerMovementEnabled(false);
        PlayUIClip(openClip, openVolume);

        dimBackgroundCanvasGroup.alpha = 0f;
        dimBackgroundCanvasGroup.interactable = false;
        dimBackgroundCanvasGroup.blocksRaycasts = false;

        inventoryPanelCanvasGroup.alpha = 0f;
        inventoryPanelCanvasGroup.interactable = false;
        inventoryPanelCanvasGroup.blocksRaycasts = false;
        inventoryPanelRect.localScale = Vector3.one * panelStartScale;

        float totalDuration = Mathf.Max(backgroundFadeDuration, panelFadeDuration);
        float elapsed = 0f;

        while (elapsed < totalDuration)
        {
            elapsed += Time.unscaledDeltaTime;

            float bgT = Mathf.Clamp01(elapsed / backgroundFadeDuration);
            float panelT = Mathf.Clamp01(elapsed / panelFadeDuration);

            dimBackgroundCanvasGroup.alpha = Mathf.Lerp(0f, backgroundMaxAlpha, bgT);
            inventoryPanelCanvasGroup.alpha = Mathf.Lerp(0f, 1f, panelT);

            float easedPanelT = EaseOutBack(panelT);
            float currentScale = Mathf.Lerp(panelStartScale, panelEndScale, easedPanelT);
            inventoryPanelRect.localScale = Vector3.one * currentScale;

            yield return null;
        }

        dimBackgroundCanvasGroup.alpha = backgroundMaxAlpha;

        inventoryPanelCanvasGroup.alpha = 1f;
        inventoryPanelCanvasGroup.interactable = true;
        inventoryPanelCanvasGroup.blocksRaycasts = true;
        inventoryPanelRect.localScale = Vector3.one * panelEndScale;

        isTransitioning = false;
    }

    private IEnumerator CloseInventoryRoutine()
    {
        isTransitioning = true;

        PlayUIClip(closeClip, closeVolume);

        inventoryPanelCanvasGroup.interactable = false;
        inventoryPanelCanvasGroup.blocksRaycasts = false;

        float totalDuration = Mathf.Max(backgroundFadeDuration, panelFadeDuration);
        float elapsed = 0f;

        while (elapsed < totalDuration)
        {
            elapsed += Time.unscaledDeltaTime;

            float bgT = Mathf.Clamp01(elapsed / backgroundFadeDuration);
            float panelT = Mathf.Clamp01(elapsed / panelFadeDuration);

            dimBackgroundCanvasGroup.alpha = Mathf.Lerp(backgroundMaxAlpha, 0f, bgT);
            inventoryPanelCanvasGroup.alpha = Mathf.Lerp(1f, 0f, panelT);

            float easedPanelT = EaseInBack(panelT);
            float currentScale = Mathf.Lerp(panelEndScale, panelStartScale, easedPanelT);
            inventoryPanelRect.localScale = Vector3.one * currentScale;

            yield return null;
        }

        SetClosedStateImmediate();
        isTransitioning = false;
    }

    private void SetClosedStateImmediate()
    {
        dimBackgroundCanvasGroup.alpha = 0f;
        dimBackgroundCanvasGroup.interactable = false;
        dimBackgroundCanvasGroup.blocksRaycasts = false;

        inventoryPanelCanvasGroup.alpha = 0f;
        inventoryPanelCanvasGroup.interactable = false;
        inventoryPanelCanvasGroup.blocksRaycasts = false;

        inventoryPanelRect.localScale = Vector3.one * panelStartScale;
        inventoryUIRoot.SetActive(false);

        isOpen = false;
        SetPlayerMovementEnabled(true);
    }

    private void SetPlayerMovementEnabled(bool value)
    {
        if (playerMovementBehaviour != null)
            playerMovementBehaviour.enabled = value;
    }

    private void PlayUIClip(AudioClip clip, float volume)
    {
        if (uiAudioSource == null || clip == null)
            return;

        uiAudioSource.PlayOneShot(clip, volume);
    }

    private float EaseOutBack(float t)
    {
        float c1 = 1.70158f;
        float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }

    private float EaseInBack(float t)
    {
        float c1 = 1.70158f;
        float c3 = c1 + 1f;
        return c3 * t * t * t - c1 * t * t;
    }
}