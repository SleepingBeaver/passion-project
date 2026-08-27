using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// Farming participa do mesmo contrato de qualquer outra interacao, sem regras especiais no roteador.
internal sealed class FarmingInteractionSource :
    IPlayerInteractionSource,
    IPlayerInteractionPresentationHandler,
    IPlayerInteractionLifecycleHandler
{
    private const string FarmingSourceId = "farming";
    private const int FarmingPriority = 0;

    private FarmingSystem farmingSystem;
    private Camera worldCamera;
    private EventSystem eventSystem;

    public FarmingInteractionSource(
        FarmingSystem farmingSystem,
        Camera worldCamera,
        EventSystem eventSystem)
    {
        Configure(farmingSystem, worldCamera, eventSystem);
    }

    public string SourceId => FarmingSourceId;
    public FarmingSystem.FarmingInteraction PrimaryInteraction { get; private set; }
    public FarmingSystem.FarmingInteraction PointerInteraction { get; private set; }
    public bool CanUsePrimary { get; private set; }
    public bool CanUsePointer { get; private set; }
    public bool PointerTargetsSoilInRange { get; private set; }

    public void Configure(FarmingSystem system, Camera camera, EventSystem currentEventSystem)
    {
        farmingSystem = system;
        worldCamera = camera;
        eventSystem = currentEventSystem;
        ResetTargets();
    }

    public void Refresh(PlayerInteractor interactor)
    {
        FarmingSystem.FarmingInteraction primary = default;
        CanUsePrimary = farmingSystem != null &&
                        farmingSystem.TryGetInteraction(interactor, out primary);
        PrimaryInteraction = primary;

        ResolvePointer(interactor, Mouse.current);
    }

    public bool TryGetCandidate(
        PlayerInteractor interactor,
        PlayerInteractionInput input,
        out PlayerInteractionCandidate candidate)
    {
        candidate = default;
        FarmingSystem.FarmingInteraction interaction;

        if (input == PlayerInteractionInput.Primary)
        {
            if (!CanUsePrimary)
                return false;

            interaction = PrimaryInteraction;
        }
        else
        {
            if (!CanUsePointer)
                return false;

            interaction = PointerInteraction;
        }

        candidate = new PlayerInteractionCandidate(
            this,
            input,
            FarmingPriority,
            interaction.PromptText,
            farmingSystem,
            GetInteractionKey(interaction));
        return true;
    }

    public bool TryPerform(
        PlayerInteractor interactor,
        PlayerInteractionCandidate candidate,
        out object payload)
    {
        payload = null;
        FarmingSystem.FarmingInteraction interaction;

        if (candidate.Input == PlayerInteractionInput.Pointer)
        {
            if (!CanUsePointer)
                return false;

            interaction = PointerInteraction;
        }
        else
        {
            if (!CanUsePrimary)
                return false;

            interaction = PrimaryInteraction;
        }

        if (candidate.ContextKey != GetInteractionKey(interaction) ||
            farmingSystem == null ||
            !farmingSystem.TryPerformInteraction(interactor, interaction))
        {
            return false;
        }

        // O struct so e convertido em object quando uma acao realmente acontece, nunca por frame.
        payload = interaction;
        return true;
    }

    public void UpdatePresentation(
        PlayerInteractor interactor,
        PlayerInteractionCandidate primaryInteraction,
        PlayerInteractionCandidate pointerInteraction)
    {
        if (farmingSystem == null)
            return;

        bool pointerIsSelected = pointerInteraction.IsValid &&
                                 ReferenceEquals(pointerInteraction.Source, this);
        bool primaryIsSelected = primaryInteraction.IsValid &&
                                 ReferenceEquals(primaryInteraction.Source, this);

        if (pointerIsSelected && CanUsePointer)
            farmingSystem.ShowTargetOutline(PointerInteraction);
        else if (PointerTargetsSoilInRange)
            farmingSystem.HideTargetOutline();
        else if (primaryIsSelected && CanUsePrimary)
            farmingSystem.ShowTargetOutline(PrimaryInteraction);
        else
            farmingSystem.HideTargetOutline();
    }

    public void OnInteractionSystemEnabled(PlayerInteractor interactor)
    {
        ResetTargets();
    }

    public void OnInteractionSystemDisabled(PlayerInteractor interactor)
    {
        farmingSystem?.HideTargetOutline();
        ResetTargets();
    }

    private void ResolvePointer(PlayerInteractor interactor, Mouse mouse)
    {
        PointerInteraction = default;
        CanUsePointer = false;
        PointerTargetsSoilInRange = false;

        if (farmingSystem == null || mouse == null || worldCamera == null ||
            Time.timeScale <= 0f ||
            (eventSystem != null && eventSystem.IsPointerOverGameObject()))
        {
            return;
        }

        Vector2 screenPosition = mouse.position.ReadValue();
        float depth = farmingSystem.SoilTilemap != null
            ? Mathf.Abs(worldCamera.transform.position.z - farmingSystem.SoilTilemap.transform.position.z)
            : Mathf.Abs(worldCamera.transform.position.z);
        Vector3 worldPosition = worldCamera.ScreenToWorldPoint(
            new Vector3(screenPosition.x, screenPosition.y, depth));
        worldPosition.z = 0f;

        CanUsePointer = farmingSystem.TryGetMouseInteraction(
            interactor,
            worldPosition,
            out FarmingSystem.FarmingInteraction pointer,
            out bool targetsSoilInRange);
        PointerTargetsSoilInRange = targetsSoilInRange;
        PointerInteraction = pointer;
    }

    private static int GetInteractionKey(FarmingSystem.FarmingInteraction interaction)
    {
        unchecked
        {
            int hash = interaction.Cell.x;
            hash = (hash * 397) ^ interaction.Cell.y;
            hash = (hash * 397) ^ interaction.Cell.z;
            return (hash * 397) ^ (int)interaction.Action;
        }
    }

    private void ResetTargets()
    {
        PrimaryInteraction = default;
        PointerInteraction = default;
        CanUsePrimary = false;
        CanUsePointer = false;
        PointerTargetsSoilInRange = false;
    }
}
