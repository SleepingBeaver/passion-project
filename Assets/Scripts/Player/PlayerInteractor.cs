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

    private void Update()
    {
        keyboard ??= Keyboard.current;

        if (Time.time >= nextCleanupTime)
        {
            CleanupNearby();
            nextCleanupTime = Time.time + NearbyCleanupInterval;
        }

        UpdateCurrentInteractable();
        HandleInteractionInput();
        UpdatePromptUI();
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
    private void UpdateCurrentInteractable()
    {
        WorldInteractable best = null;
        float bestDistanceSqr = float.MaxValue;
        Vector3 origin = ActorTransform.position;

        for (int i = 0; i < nearbyInteractables.Count; i++)
        {
            WorldInteractable candidate = nearbyInteractables[i];

            if (candidate == null || !candidate.CanInteract(this))
                continue;

            float distanceSqr = (candidate.transform.position - origin).sqrMagnitude;

            if (distanceSqr < bestDistanceSqr)
            {
                bestDistanceSqr = distanceSqr;
                best = candidate;
            }
        }

        if (best == currentInteractable)
            return;

        if (currentInteractable != null)
            currentInteractable.OnFocusExit(this);

        currentInteractable = best;
        CancelHold();

        if (currentInteractable != null)
            currentInteractable.OnFocusEnter(this);
    }

    private void HandleInteractionInput()
    {
        if (keyboard == null || currentInteractable == null)
            return;

        if (currentInteractable.RequiresHold)
        {
            HandleHoldInteraction();
        }
        else
        {
            if (keyboard.eKey.wasPressedThisFrame)
                currentInteractable.TryInteract(this);
        }
    }

    private void HandleHoldInteraction()
    {
        if (!holdInProgress && keyboard.eKey.wasPressedThisFrame)
        {
            holdInProgress = true;
            holdTimer = 0f;
            currentInteractable.OnHoldStarted(this);
        }

        if (!holdInProgress)
            return;

        if (!keyboard.eKey.isPressed)
        {
            currentInteractable.OnHoldCanceled(this);
            CancelHold();
            return;
        }

        holdTimer += Time.deltaTime;

        if (holdTimer >= currentInteractable.HoldDuration)
        {
            currentInteractable.TryInteract(this);
            CancelHold();
        }
    }

    private void CancelHold()
    {
        holdInProgress = false;
        holdTimer = 0f;

        SetHoldFillAmount(0f);
    }

    // Atualizacao visual do prompt e da barra de hold.
    private void UpdatePromptUI()
    {
        bool show = currentInteractable != null;

        SetPromptVisible(show);

        if (!show)
        {
            SetHoldIndicatorVisible(false);
            SetHoldFillAmount(0f);
            return;
        }

        SetPromptText(currentInteractable.PromptText);
        SetHoldIndicatorVisible(currentInteractable.RequiresHold);

        float fillAmount = 0f;
        if (currentInteractable.RequiresHold && holdInProgress && currentInteractable.HoldDuration > 0f)
            fillAmount = Mathf.Clamp01(holdTimer / currentInteractable.HoldDuration);

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
