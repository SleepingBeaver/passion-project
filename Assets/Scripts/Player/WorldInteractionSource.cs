using System.Collections.Generic;
using UnityEngine;

// Adaptador generico para todos os WorldInteractable atuais.
internal sealed class WorldInteractionSource :
    IPlayerInteractionSource,
    IPlayerInteractionFocusHandler,
    IPlayerInteractionHoldHandler,
    IPlayerInteractionLifecycleHandler
{
    private const string WorldSourceId = "world";
    private const int WorldPriority = 100;
    private const float NearbyRefreshInterval = 0.25f;

    private readonly List<WorldInteractable> nearbyInteractables = new();
    private readonly List<Collider2D> overlapResults = new(32);

    private PlayerInteractor interactor;
    private Collider2D interactionDetector;
    private ContactFilter2D contactFilter;
    private float nextRefreshTime;

    public WorldInteractionSource(PlayerInteractor interactor, Collider2D interactionDetector)
    {
        this.interactor = interactor;
        Configure(interactionDetector);
    }

    public string SourceId => WorldSourceId;
    public WorldInteractable Current { get; private set; }

    public void Configure(Collider2D detector)
    {
        interactionDetector = detector;
        contactFilter = default;
        contactFilter.useTriggers = true;
        contactFilter.useLayerMask = false;
        contactFilter.useDepth = false;
        contactFilter.useNormalAngle = false;
        nextRefreshTime = 0f;
    }

    public void Refresh(PlayerInteractor owner)
    {
        interactor = owner;

        if (Time.time >= nextRefreshTime)
        {
            RefreshNearbyInteractables();
            nextRefreshTime = Time.time + NearbyRefreshInterval;
        }

        RefreshSelection(out _);
    }

    public bool TryGetCandidate(
        PlayerInteractor owner,
        PlayerInteractionInput input,
        out PlayerInteractionCandidate candidate)
    {
        candidate = default;
        if (input != PlayerInteractionInput.Primary || Current == null || !Current.CanInteract(owner))
            return false;

        bool requiresHold = Current.GetRequiresHold(owner);
        candidate = new PlayerInteractionCandidate(
            this,
            input,
            WorldPriority,
            Current.GetPromptText(owner),
            Current,
            contextKey: 0,
            requiresHold: requiresHold,
            holdDuration: requiresHold ? Current.GetHoldDuration(owner) : 0f);
        return true;
    }

    public bool TryPerform(
        PlayerInteractor owner,
        PlayerInteractionCandidate candidate,
        out object payload)
    {
        payload = null;
        if (candidate.Input != PlayerInteractionInput.Primary ||
            candidate.Target is not WorldInteractable target ||
            target != Current ||
            !target.TryInteract(owner))
        {
            return false;
        }

        payload = target;
        return true;
    }

    public void OnFocusEnter(PlayerInteractor owner, PlayerInteractionCandidate candidate)
    {
        if (candidate.Target is WorldInteractable target)
            target.OnFocusEnter(owner);
    }

    public void OnFocusExit(PlayerInteractor owner, PlayerInteractionCandidate candidate)
    {
        if (candidate.Target is WorldInteractable target)
            target.OnFocusExit(owner);
    }

    public void OnHoldStarted(PlayerInteractor owner, PlayerInteractionCandidate candidate)
    {
        if (candidate.Target is WorldInteractable target)
            target.OnHoldStarted(owner);
    }

    public void OnHoldCanceled(PlayerInteractor owner, PlayerInteractionCandidate candidate)
    {
        if (candidate.Target is WorldInteractable target)
            target.OnHoldCanceled(owner);
    }

    public void OnInteractionSystemEnabled(PlayerInteractor owner)
    {
        interactor = owner;
        nextRefreshTime = 0f;
    }

    public void OnInteractionSystemDisabled(PlayerInteractor owner)
    {
        Clear();
    }

    public void Register(Collider2D other)
    {
        if (other == null || interactor == null || other.transform.IsChildOf(interactor.transform))
            return;

        Register(other.GetComponentInParent<WorldInteractable>());
    }

    public bool RefreshAfterExit(Collider2D other, out bool selectionChanged)
    {
        if (other == null || other.GetComponentInParent<WorldInteractable>() == null)
        {
            selectionChanged = false;
            return Current != null && interactor != null && Current.CanInteract(interactor);
        }

        // Uma nova consulta evita remover o alvo quando somente um de seus varios colliders saiu.
        RefreshNearbyInteractables();
        return RefreshSelection(out selectionChanged);
    }

    public void RefreshNearbyInteractables()
    {
        CleanupDestroyedEntries();

        if (interactionDetector == null || !interactionDetector.enabled ||
            !interactionDetector.gameObject.activeInHierarchy)
        {
            nearbyInteractables.Clear();
            overlapResults.Clear();
            return;
        }

        overlapResults.Clear();
        Physics2D.OverlapCollider(interactionDetector, contactFilter, overlapResults);
        nearbyInteractables.Clear();

        for (int i = 0; i < overlapResults.Count; i++)
        {
            Collider2D hit = overlapResults[i];
            if (hit == null || interactor == null || hit.transform.IsChildOf(interactor.transform))
                continue;

            Register(hit.GetComponentInParent<WorldInteractable>());
        }
    }

    public bool RefreshSelection(out bool selectionChanged)
    {
        WorldInteractable best = ResolveBestInteractable(out bool bestCanInteract);
        selectionChanged = best != Current;
        Current = best;
        return bestCanInteract;
    }

    private void Clear()
    {
        Current = null;
        nearbyInteractables.Clear();
        overlapResults.Clear();
        nextRefreshTime = 0f;
    }

    private void Register(WorldInteractable interactable)
    {
        if (interactable != null && !nearbyInteractables.Contains(interactable))
            nearbyInteractables.Add(interactable);
    }

    private void CleanupDestroyedEntries()
    {
        for (int i = nearbyInteractables.Count - 1; i >= 0; i--)
        {
            if (nearbyInteractables[i] == null)
                nearbyInteractables.RemoveAt(i);
        }
    }

    private WorldInteractable ResolveBestInteractable(out bool canInteract)
    {
        WorldInteractable bestAvailable = null;
        WorldInteractable bestFallback = null;
        float bestAvailableDistanceSqr = float.MaxValue;
        float bestFallbackDistanceSqr = float.MaxValue;
        Vector3 origin = interactor != null ? interactor.ActorTransform.position : Vector3.zero;

        for (int i = 0; i < nearbyInteractables.Count; i++)
        {
            WorldInteractable candidate = nearbyInteractables[i];
            if (candidate == null || interactor == null || !candidate.CanFocus(interactor))
                continue;

            float distanceSqr = (candidate.transform.position - origin).sqrMagnitude;
            if (candidate.CanInteract(interactor))
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
}
