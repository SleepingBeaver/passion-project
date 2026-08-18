using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerInteractor : MonoBehaviour
{
    // Constantes de manutencao da lista de interagiveis.
    private const float NearbyCleanupInterval = 0.25f;

    // Referencias principais e elementos de UI.
    [Header("References")]
    [SerializeField] private InventorySystem inventorySystem;
    [SerializeField] private Transform distanceReference;

    [Header("UI")]
    [SerializeField] private GameObject promptRoot;
    [SerializeField] private TMP_Text promptText;
    [SerializeField] private Image holdFillImage;

    // Estado interno da deteccao e do hold.
    private readonly List<WorldInteractable> nearbyInteractables = new();

    private WorldInteractable currentInteractable;
    private bool holdInProgress;
    private float holdTimer;
    private float nextCleanupTime;
    private Keyboard keyboard;

    // Acessos publicos usados por outros sistemas.
    public InventorySystem InventorySystem => inventorySystem;
    public Transform ActorTransform => distanceReference != null ? distanceReference : transform;

    // Ciclo de vida.
    private void Awake()
    {
        if (distanceReference == null)
            distanceReference = transform;

        keyboard = Keyboard.current;
        HidePromptImmediate();
    }

    private void OnDisable()
    {
        if (currentInteractable != null)
            currentInteractable.OnFocusExit(this);

        currentInteractable = null;
        CancelHold();
        HidePromptImmediate();
    }

    private void Update()
    {
        if (Time.time >= nextCleanupTime)
        {
            CleanupNearby();
            nextCleanupTime = Time.time + NearbyCleanupInterval;
        }

        if (nearbyInteractables.Count == 0 && currentInteractable == null)
        {
            HidePromptImmediate();
            return;
        }

        keyboard ??= Keyboard.current;

        bool canInteract = UpdateCurrentInteractable();
        ResolveInteractionState(canInteract, out bool requiresHold, out float holdDuration);

        if (HandleInteractionInput(canInteract, requiresHold, holdDuration))
        {
            canInteract = currentInteractable != null && currentInteractable.CanInteract(this);
            ResolveInteractionState(canInteract, out requiresHold, out holdDuration);
        }

        UpdatePromptUI(canInteract, requiresHold, holdDuration);
    }

    // Deteccao de objetos proximos.
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
            return;

        WorldInteractable interactable = other.GetComponentInParent<WorldInteractable>();

        if (interactable != null && !nearbyInteractables.Contains(interactable))
            nearbyInteractables.Add(interactable);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        WorldInteractable interactable = other.GetComponentInParent<WorldInteractable>();

        if (interactable != null)
        {
            nearbyInteractables.Remove(interactable);

            if (currentInteractable == interactable)
            {
                currentInteractable.OnFocusExit(this);
                currentInteractable = null;
                CancelHold();
            }
        }
    }

    private void CleanupNearby()
    {
        for (int i = nearbyInteractables.Count - 1; i >= 0; i--)
        {
            if (nearbyInteractables[i] == null)
                nearbyInteractables.RemoveAt(i);
        }
    }

    // Fluxo de escolha e execucao da interacao atual.
    private bool UpdateCurrentInteractable()
    {
        WorldInteractable best = ResolveBestInteractable(out bool bestCanInteract);

        if (best == currentInteractable)
            return bestCanInteract;

        if (currentInteractable != null)
            currentInteractable.OnFocusExit(this);

        currentInteractable = best;
        CancelHold();

        if (currentInteractable != null)
            currentInteractable.OnFocusEnter(this);

        return bestCanInteract;
    }

    // Prioriza o alvo mais proximo que possa ser usado agora; se nada estiver utilizavel,
    // ainda escolhemos o melhor fallback para manter o prompt contextual visivel.
    private WorldInteractable ResolveBestInteractable(out bool canInteract)
    {
        WorldInteractable bestAvailable = null;
        WorldInteractable bestFallback = null;
        float bestAvailableDistanceSqr = float.MaxValue;
        float bestFallbackDistanceSqr = float.MaxValue;
        Vector3 origin = ActorTransform.position;

        for (int i = 0; i < nearbyInteractables.Count; i++)
        {
            WorldInteractable candidate = nearbyInteractables[i];

            if (candidate == null || !candidate.CanFocus(this))
                continue;

            float distanceSqr = (candidate.transform.position - origin).sqrMagnitude;
            bool candidateCanInteract = candidate.CanInteract(this);

            if (candidateCanInteract)
            {
                if (distanceSqr < bestAvailableDistanceSqr)
                {
                    bestAvailableDistanceSqr = distanceSqr;
                    bestAvailable = candidate;
                }
            }
            else if (bestAvailable == null && distanceSqr < bestFallbackDistanceSqr)
            {
                bestFallbackDistanceSqr = distanceSqr;
                bestFallback = candidate;
            }
        }

        canInteract = bestAvailable != null;
        return canInteract ? bestAvailable : bestFallback;
    }

    private void ResolveInteractionState(bool canInteract, out bool requiresHold, out float holdDuration)
    {
        requiresHold = canInteract && currentInteractable != null && currentInteractable.GetRequiresHold(this);
        holdDuration = requiresHold ? currentInteractable.GetHoldDuration(this) : 0f;
    }

    private bool HandleInteractionInput(bool canInteract, bool requiresHold, float holdDuration)
    {
        if (keyboard == null || currentInteractable == null)
            return false;

        if (!canInteract)
        {
            CancelHoldBecauseInteractionBecameInvalid();
            return false;
        }

        if (requiresHold)
            return HandleHoldInteraction(holdDuration);

        if (!keyboard.eKey.wasPressedThisFrame)
            return false;

        currentInteractable.TryInteract(this);
        return true;
    }

    private void CancelHoldBecauseInteractionBecameInvalid()
    {
        if (!holdInProgress)
            return;

        currentInteractable.OnHoldCanceled(this);
        CancelHold();
    }

    private bool HandleHoldInteraction(float holdDuration)
    {
        if (!holdInProgress && keyboard.eKey.wasPressedThisFrame)
        {
            holdInProgress = true;
            holdTimer = 0f;
            currentInteractable.OnHoldStarted(this);
        }

        if (!holdInProgress)
            return false;

        if (!keyboard.eKey.isPressed)
        {
            currentInteractable.OnHoldCanceled(this);
            CancelHold();
            return false;
        }

        holdTimer += Time.deltaTime;

        if (holdTimer >= holdDuration)
        {
            currentInteractable.TryInteract(this);
            CancelHold();
            return true;
        }

        return false;
    }

    private void CancelHold()
    {
        holdInProgress = false;
        holdTimer = 0f;

        SetHoldFillAmount(0f);
    }

    // Atualizacao visual do prompt e da barra de hold.
    private void UpdatePromptUI(bool canInteract, bool requiresHold, float holdDuration)
    {
        bool show = currentInteractable != null && canInteract;

        SetPromptVisible(show);

        if (!show)
        {
            SetHoldIndicatorVisible(false);
            SetHoldFillAmount(0f);
            return;
        }

        SetPromptText(currentInteractable.GetPromptText(this));
        SetHoldIndicatorVisible(requiresHold);

        float fillAmount = 0f;
        if (requiresHold && holdInProgress && holdDuration > 0f)
            fillAmount = Mathf.Clamp01(holdTimer / holdDuration);

        SetHoldFillAmount(fillAmount);
    }

    private void HidePromptImmediate()
    {
        SetPromptVisible(false);
        SetHoldIndicatorVisible(false);
        SetHoldFillAmount(0f);
    }

    // Utilitarios de UI.
    private void SetPromptVisible(bool value)
    {
        if (promptRoot != null && promptRoot.activeSelf != value)
            promptRoot.SetActive(value);
    }

    private void SetPromptText(string value)
    {
        if (promptText != null && promptText.text != value)
            promptText.text = value;
    }

    private void SetHoldIndicatorVisible(bool value)
    {
        if (holdFillImage != null && holdFillImage.gameObject.activeSelf != value)
            holdFillImage.gameObject.SetActive(value);
    }

    private void SetHoldFillAmount(float value)
    {
        if (holdFillImage != null && !Mathf.Approximately(holdFillImage.fillAmount, value))
            holdFillImage.fillAmount = value;
    }
}
