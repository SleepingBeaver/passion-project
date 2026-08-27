using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerInteractor : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private InventorySystem inventorySystem;
    [SerializeField] private Transform distanceReference;
    [SerializeField] private Collider2D farmingGroundCollider;
    [SerializeField] private CircleCollider2D interactionDetector;
    [SerializeField] private IsoPlayerController2D movementController;
    [SerializeField] private FarmingSystem farmingSystem;
    [SerializeField] private Camera worldCamera;
    [SerializeField] private EventSystem eventSystem;

    [Header("UI")]
    [SerializeField] private GameObject promptRoot;
    [SerializeField] private TMP_Text promptText;
    [SerializeField] private Image holdFillImage;

    private PlayerInteractionRouter interactionRouter;
    private WorldInteractionSource worldInteractions;
    private FarmingInteractionSource farmingInteractions;
    private Keyboard keyboard;
    private Mouse mouse;
    private PlayerInteractionCandidate holdInteraction;
    private bool holdInProgress;
    private float holdTimer;

    public InventorySystem InventorySystem => inventorySystem;
    public Transform ActorTransform => distanceReference != null ? distanceReference : transform;
    public PlayerInteractionCandidate CurrentPrimaryInteraction =>
        interactionRouter?.PrimaryInteraction ?? default;
    public PlayerInteractionCandidate CurrentPointerInteraction =>
        interactionRouter?.PointerInteraction ?? default;
    public int RegisteredInteractionSourceCount => interactionRouter?.RegisteredSourceCount ?? 0;

    public Vector3 FarmingGroundPosition
    {
        get
        {
            if (farmingGroundCollider == null)
                return ActorTransform.position;

            Vector3 groundPosition = farmingGroundCollider.transform.TransformPoint(farmingGroundCollider.offset);
            groundPosition.z = ActorTransform.position.z;
            return groundPosition;
        }
    }

    public Vector2 FacingDirection => movementController != null
        ? movementController.FacingDirection
        : Vector2.down;

    // Um unico evento generico atende fontes atuais e futuras. O payload preserva o tipo
    // concreto e pode ser lido com PlayerInteractionEvent.TryGetPayload<T>().
    public event Action<PlayerInteractionEvent> InteractionPerformed;

    private void Awake()
    {
        ResolveReferences();
        EnsureInteractionSystem();
        ConfigureBuiltInSources();
        DiscoverComponentSources();
        interactionRouter.Activate();
        keyboard = Keyboard.current;
        mouse = Mouse.current;
        RefreshNearbyInteractables();
        HidePromptImmediate();
    }

    private void OnEnable()
    {
        ResolveReferences();
        EnsureInteractionSystem();
        ConfigureBuiltInSources();
        DiscoverComponentSources();
        interactionRouter.Activate();
        keyboard = Keyboard.current;
        mouse = Mouse.current;
    }

    private void OnDisable()
    {
        CancelHoldBecauseInteractionBecameInvalid();
        interactionRouter?.Deactivate();
        HidePromptImmediate();
    }

    private void OnTransformChildrenChanged()
    {
        if (isActiveAndEnabled)
            DiscoverComponentSources();
    }

    private void Update()
    {
        keyboard ??= Keyboard.current;
        mouse ??= Mouse.current;

        interactionRouter.Refresh();
        PlayerInteractionCandidate primary = interactionRouter.PrimaryInteraction;
        PlayerInteractionCandidate pointer = interactionRouter.PointerInteraction;

        bool interactionWasPerformed = HandlePrimaryInput(primary);

        // O ponteiro e um canal independente, mas no maximo uma interacao conclui por frame.
        if (!interactionWasPerformed && !holdInProgress && pointer.IsValid &&
            mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            interactionWasPerformed = TryPerformInteraction(pointer);
        }

        if (interactionWasPerformed)
        {
            interactionRouter.Refresh();
            primary = interactionRouter.PrimaryInteraction;
            pointer = interactionRouter.PointerInteraction;
        }

        UpdatePromptUI(primary, pointer);
    }

    // Fontes externas podem se registrar em runtime. Componentes no jogador e em seus
    // filhos que implementem IPlayerInteractionSource sao descobertos automaticamente.
    public bool RegisterInteractionSource(IPlayerInteractionSource source)
    {
        EnsureInteractionSystem();
        return interactionRouter.Register(source);
    }

    public bool UnregisterInteractionSource(IPlayerInteractionSource source)
    {
        return interactionRouter != null && interactionRouter.Unregister(source);
    }

    public bool TryPerformCurrentInteraction(PlayerInteractionInput input)
    {
        if (interactionRouter == null)
            return false;

        interactionRouter.Refresh();
        PlayerInteractionCandidate candidate = input == PlayerInteractionInput.Pointer
            ? interactionRouter.PointerInteraction
            : interactionRouter.PrimaryInteraction;
        return candidate.IsValid && !candidate.RequiresHold && TryPerformInteraction(candidate);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        EnsureInteractionSystem();
        worldInteractions.Register(other);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        EnsureInteractionSystem();
        worldInteractions.RefreshAfterExit(other, out _);
        interactionRouter.Refresh();

        if (holdInProgress && holdInteraction != interactionRouter.PrimaryInteraction)
            CancelHoldBecauseInteractionBecameInvalid();
    }

    private bool HandlePrimaryInput(PlayerInteractionCandidate primary)
    {
        if (!primary.IsValid || keyboard == null)
        {
            CancelHoldBecauseInteractionBecameInvalid();
            return false;
        }

        if (primary.RequiresHold)
            return HandleHoldInteraction(primary);

        CancelHoldBecauseInteractionBecameInvalid();
        return keyboard.eKey.wasPressedThisFrame && TryPerformInteraction(primary);
    }

    private bool HandleHoldInteraction(PlayerInteractionCandidate interaction)
    {
        if (!holdInProgress && keyboard.eKey.wasPressedThisFrame)
        {
            holdInteraction = interaction;
            holdTimer = 0f;
            holdInProgress = interactionRouter.NotifyHoldStarted(interaction);

            if (!holdInProgress)
                ResetHoldState();
        }

        if (!holdInProgress)
            return false;

        if (holdInteraction != interaction)
        {
            CancelHoldBecauseInteractionBecameInvalid();
            return false;
        }

        if (!keyboard.eKey.isPressed)
        {
            CancelHoldBecauseInteractionBecameInvalid();
            return false;
        }

        holdTimer += Time.deltaTime;
        if (holdTimer < interaction.HoldDuration)
            return false;

        bool performed = TryPerformInteraction(holdInteraction);
        ResetHoldState();
        return performed;
    }

    private bool TryPerformInteraction(PlayerInteractionCandidate interaction)
    {
        if (!interactionRouter.TryPerform(interaction, out object payload))
            return false;

        PublishInteractionEvent(new PlayerInteractionEvent(interaction, payload));
        return true;
    }

    private void PublishInteractionEvent(PlayerInteractionEvent interactionEvent)
    {
        Delegate[] subscribers = InteractionPerformed?.GetInvocationList();
        if (subscribers == null)
            return;

        for (int i = 0; i < subscribers.Length; i++)
        {
            try
            {
                ((Action<PlayerInteractionEvent>)subscribers[i]).Invoke(interactionEvent);
            }
            catch (Exception exception)
            {
                // Um observador de evento nao pode interromper a interacao nem os demais observadores.
                Debug.LogException(exception, this);
            }
        }
    }

    private void CancelHoldBecauseInteractionBecameInvalid()
    {
        if (!holdInProgress)
            return;

        interactionRouter?.NotifyHoldCanceled(holdInteraction);
        ResetHoldState();
    }

    private void ResetHoldState()
    {
        holdInProgress = false;
        holdTimer = 0f;
        holdInteraction = default;
        SetHoldFillAmount(0f);
    }

    // Pontos de diagnostico mantidos para o validador e recuperacao de Play Mode.
    private void RefreshNearbyInteractables()
    {
        EnsureInteractionSystem();
        worldInteractions.RefreshNearbyInteractables();
    }

    private bool UpdateCurrentInteractable()
    {
        EnsureInteractionSystem();
        return worldInteractions.RefreshSelection(out _);
    }

    private Collider2D ResolveFarmingGroundCollider()
    {
        Collider2D[] colliders = GetComponents<Collider2D>();
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null && !colliders[i].isTrigger)
                return colliders[i];
        }

        return null;
    }

    private CircleCollider2D ResolveInteractionDetector()
    {
        CircleCollider2D[] colliders = GetComponentsInChildren<CircleCollider2D>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null && colliders[i].isTrigger)
                return colliders[i];
        }

        return null;
    }

    private void ResolveReferences()
    {
        distanceReference ??= transform;
        farmingGroundCollider ??= ResolveFarmingGroundCollider();
        interactionDetector ??= ResolveInteractionDetector();
        movementController ??= GetComponent<IsoPlayerController2D>();
        farmingSystem ??= FindAnyObjectByType<FarmingSystem>();

        if (worldCamera == null)
        {
            Camera mainCamera = Camera.main;
            worldCamera = mainCamera != null ? mainCamera : FindAnyObjectByType<Camera>();
        }

        eventSystem ??= EventSystem.current;
    }

    private void EnsureInteractionSystem()
    {
        interactionRouter ??= new PlayerInteractionRouter(this);
        worldInteractions ??= new WorldInteractionSource(this, interactionDetector);
        farmingInteractions ??= new FarmingInteractionSource(farmingSystem, worldCamera, eventSystem);

        interactionRouter.Register(worldInteractions);
        interactionRouter.Register(farmingInteractions);
    }

    private void ConfigureBuiltInSources()
    {
        worldInteractions.Configure(interactionDetector);
        farmingInteractions.Configure(farmingSystem, worldCamera, eventSystem);
    }

    private void DiscoverComponentSources()
    {
        if (interactionRouter == null)
            return;

        MonoBehaviour[] behaviours = GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is IPlayerInteractionSource source)
                interactionRouter.Register(source);
        }
    }

    private void UpdatePromptUI(
        PlayerInteractionCandidate primary,
        PlayerInteractionCandidate pointer)
    {
        PlayerInteractionCandidate promptInteraction = primary.IsValid ? primary : pointer;
        SetPromptVisible(promptInteraction.IsValid);

        if (!promptInteraction.IsValid)
        {
            SetHoldIndicatorVisible(false);
            SetHoldFillAmount(0f);
            return;
        }

        SetPromptText(promptInteraction.PromptText);
        bool showHold = primary.IsValid && primary.RequiresHold;
        SetHoldIndicatorVisible(showHold);

        float fillAmount = 0f;
        if (showHold && holdInProgress && primary == holdInteraction && primary.HoldDuration > 0f)
            fillAmount = Mathf.Clamp01(holdTimer / primary.HoldDuration);

        SetHoldFillAmount(fillAmount);
    }

    private void HidePromptImmediate()
    {
        SetPromptVisible(false);
        SetHoldIndicatorVisible(false);
        SetHoldFillAmount(0f);
    }

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
