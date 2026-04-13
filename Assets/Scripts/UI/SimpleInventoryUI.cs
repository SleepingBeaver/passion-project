using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class SimpleInventoryUI : MonoBehaviour
{
    // Referencias principais da janela de inventario.
    [Header("Root")]
    [SerializeField] private GameObject inventoryUIRoot;

    // Configuracao do fundo escurecido.
    [Header("Background")]
    [SerializeField] private CanvasGroup dimBackgroundCanvasGroup;
    [SerializeField] private float backgroundFadeDuration = 0.18f;
    [SerializeField, Range(0f, 1f)] private float backgroundMaxAlpha = 0.45f;

    // Configuracao visual do painel central.
    [Header("Panel")]
    [SerializeField] private CanvasGroup inventoryPanelCanvasGroup;
    [SerializeField] private RectTransform inventoryPanelRect;
    [SerializeField] private float panelFadeDuration = 0.18f;
    [SerializeField] private float panelStartScale = 0.92f;
    [SerializeField] private float panelEndScale = 1f;

    // Referencias de jogo afetadas ao abrir o inventario.
    [Header("Player")]
    [SerializeField] private Behaviour playerMovementBehaviour;

    [Header("HUD")]
    [SerializeField] private GameObject[] hudObjectsToHideWhenOpen;

    // Feedback sonoro da interface.
    [Header("Audio")]
    [SerializeField] private AudioSource uiAudioSource;
    [SerializeField] private AudioClip openClip;
    [SerializeField] private AudioClip closeClip;
    [SerializeField, Range(0f, 1f)] private float openVolume = 1f;
    [SerializeField, Range(0f, 1f)] private float closeVolume = 1f;

    // Estado interno da janela.
    private bool isOpen;
    private bool isTransitioning;
    private bool isGamePausedByInventory;
    private float previousTimeScale = 1f;
    private Keyboard keyboard;
    private readonly List<HudVisibilityState> hudVisibilityStates = new();

    // Ciclo de vida.
    private void Start()
    {
        if (!ValidateReferences())
        {
            enabled = false;
            return;
        }

        keyboard = Keyboard.current;
        SetClosedStateImmediate();
    }

    private void Update()
    {
        keyboard ??= Keyboard.current;

        if (keyboard == null || isTransitioning)
            return;

        bool tabPressed = keyboard.tabKey.wasPressedThisFrame;
        bool escapePressed = keyboard.escapeKey.wasPressedThisFrame;

        if (!isOpen && tabPressed)
        {
            StartCoroutine(OpenInventoryRoutine());
        }
        else if (isOpen && (tabPressed || escapePressed))
        {
            StartCoroutine(CloseInventoryRoutine());
        }
    }

    private void OnDisable()
    {
        SetGamePaused(false);
    }

    // Validacao inicial antes de liberar a janela.
    private bool ValidateReferences()
    {
        if (inventoryUIRoot == null)
        {
            Debug.LogWarning("InventoryUIRoot nao foi atribuido no Inspector.");
            return false;
        }

        if (dimBackgroundCanvasGroup == null)
        {
            Debug.LogWarning("DimBackground CanvasGroup nao foi atribuido.");
            return false;
        }

        if (inventoryPanelCanvasGroup == null)
        {
            Debug.LogWarning("InventoryPanel CanvasGroup nao foi atribuido.");
            return false;
        }

        if (inventoryPanelRect == null)
        {
            inventoryPanelRect = inventoryPanelCanvasGroup.transform as RectTransform;
        }

        if (inventoryPanelRect == null)
        {
            Debug.LogWarning("InventoryPanel RectTransform nao foi encontrado.");
            return false;
        }

        return true;
    }

    // Sequencia de abertura do inventario.
    private IEnumerator OpenInventoryRoutine()
    {
        isTransitioning = true;
        isOpen = true;

        inventoryUIRoot.SetActive(true);
        SetPlayerMovementEnabled(false);
        SetHudObjectsVisible(false);
        SetGamePaused(true);
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

    // Sequencia de fechamento do inventario.
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

    // Restauracao imediata do estado fechado.
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
        SetHudObjectsVisible(true);
        SetGamePaused(false);
    }

    // Controle do jogador e feedback.
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

    // Curvas de animacao usadas nas transicoes.
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

    // Controle do HUD e da pausa geral.
    private void SetHudObjectsVisible(bool value)
    {
        if (value)
        {
            for (int i = 0; i < hudVisibilityStates.Count; i++)
            {
                HudVisibilityState state = hudVisibilityStates[i];
                if (state.target != null)
                    state.target.SetActive(state.wasActive);
            }

            hudVisibilityStates.Clear();
            return;
        }

        hudVisibilityStates.Clear();

        if (hudObjectsToHideWhenOpen == null)
            return;

        for (int i = 0; i < hudObjectsToHideWhenOpen.Length; i++)
        {
            GameObject hudObject = hudObjectsToHideWhenOpen[i];
            if (hudObject == null || ContainsHudState(hudObject))
                continue;

            hudVisibilityStates.Add(new HudVisibilityState(hudObject, hudObject.activeSelf));
            hudObject.SetActive(false);
        }
    }

    private bool ContainsHudState(GameObject target)
    {
        for (int i = 0; i < hudVisibilityStates.Count; i++)
        {
            if (hudVisibilityStates[i].target == target)
                return true;
        }

        return false;
    }

    private void SetGamePaused(bool value)
    {
        if (value)
        {
            if (isGamePausedByInventory)
                return;

            previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            isGamePausedByInventory = true;
            return;
        }

        if (!isGamePausedByInventory)
            return;

        Time.timeScale = previousTimeScale;
        isGamePausedByInventory = false;
    }

    // Estrutura interna para lembrar o estado original da HUD.
    private readonly struct HudVisibilityState
    {
        public HudVisibilityState(GameObject target, bool wasActive)
        {
            this.target = target;
            this.wasActive = wasActive;
        }

        public GameObject target { get; }
        public bool wasActive { get; }
    }
}
