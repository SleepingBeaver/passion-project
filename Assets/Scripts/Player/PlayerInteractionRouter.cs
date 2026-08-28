using System;
using System.Collections.Generic;
using UnityEngine;

public enum PlayerInteractionInput
{
    Primary = 0,
    Pointer = 1
}

// Descreve uma interacao resolvida sem conhecer seu dominio concreto.
public readonly struct PlayerInteractionCandidate : IEquatable<PlayerInteractionCandidate>
{
    public PlayerInteractionCandidate(
        IPlayerInteractionSource source,
        PlayerInteractionInput input,
        int priority,
        string promptText,
        UnityEngine.Object target = null,
        int contextKey = 0,
        bool requiresHold = false,
        float holdDuration = 0f)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        SourceId = string.IsNullOrWhiteSpace(source.SourceId)
            ? source.GetType().Name
            : source.SourceId;
        Input = input;
        Priority = priority;
        PromptText = promptText ?? string.Empty;
        Target = target;
        ContextKey = contextKey;
        RequiresHold = requiresHold;
        HoldDuration = requiresHold ? Mathf.Max(0.05f, holdDuration) : 0f;
    }

    public IPlayerInteractionSource Source { get; }
    public string SourceId { get; }
    public PlayerInteractionInput Input { get; }
    public int Priority { get; }
    public string PromptText { get; }
    public UnityEngine.Object Target { get; }
    public int ContextKey { get; }
    public bool RequiresHold { get; }
    public float HoldDuration { get; }
    public bool IsValid => Source != null;

    public bool Equals(PlayerInteractionCandidate other)
    {
        return ReferenceEquals(Source, other.Source) &&
               Input == other.Input &&
               Target == other.Target &&
               ContextKey == other.ContextKey;
    }

    public override bool Equals(object obj)
    {
        return obj is PlayerInteractionCandidate other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = Source != null ? Source.GetHashCode() : 0;
            hash = (hash * 397) ^ (int)Input;
            hash = (hash * 397) ^ (Target != null ? Target.GetHashCode() : 0);
            return (hash * 397) ^ ContextKey;
        }
    }

    public static bool operator ==(PlayerInteractionCandidate left, PlayerInteractionCandidate right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(PlayerInteractionCandidate left, PlayerInteractionCandidate right)
    {
        return !left.Equals(right);
    }
}

public readonly struct PlayerInteractionEvent
{
    public PlayerInteractionEvent(PlayerInteractionCandidate interaction, object payload)
    {
        Interaction = interaction;
        Payload = payload;
    }

    public PlayerInteractionCandidate Interaction { get; }
    public object Payload { get; }

    public bool TryGetPayload<T>(out T value)
    {
        if (Payload is T typedValue)
        {
            value = typedValue;
            return true;
        }

        value = default;
        return false;
    }
}

// Contrato minimo para qualquer interacao atual ou futura.
public interface IPlayerInteractionSource
{
    string SourceId { get; }
    void Refresh(PlayerInteractor interactor);
    bool TryGetCandidate(
        PlayerInteractor interactor,
        PlayerInteractionInput input,
        out PlayerInteractionCandidate candidate);
    bool TryPerform(
        PlayerInteractor interactor,
        PlayerInteractionCandidate candidate,
        out object payload);
}

// Interfaces opcionais evitam obrigar fontes simples a implementar callbacks vazios.
public interface IPlayerInteractionFocusHandler
{
    void OnFocusEnter(PlayerInteractor interactor, PlayerInteractionCandidate candidate);
    void OnFocusExit(PlayerInteractor interactor, PlayerInteractionCandidate candidate);
}

public interface IPlayerInteractionHoldHandler
{
    void OnHoldStarted(PlayerInteractor interactor, PlayerInteractionCandidate candidate);
    void OnHoldCanceled(PlayerInteractor interactor, PlayerInteractionCandidate candidate);
}

public interface IPlayerInteractionPresentationHandler
{
    void UpdatePresentation(
        PlayerInteractor interactor,
        PlayerInteractionCandidate primaryInteraction,
        PlayerInteractionCandidate pointerInteraction);
}

public interface IPlayerInteractionLifecycleHandler
{
    void OnInteractionSystemEnabled(PlayerInteractor interactor);
    void OnInteractionSystemDisabled(PlayerInteractor interactor);
}

// Arbitra fontes registradas por canal e prioridade. Uma fonte com falha e suspensa para
// preservar todas as outras interacoes, em vez de interromper o Update do jogador.
public sealed class PlayerInteractionRouter
{
    private readonly PlayerInteractor interactor;
    private readonly List<IPlayerInteractionSource> sources = new();
    private readonly List<IPlayerInteractionSource> suspendedSources = new();
    private bool isActive;

    public PlayerInteractionRouter(PlayerInteractor interactor)
    {
        this.interactor = interactor ?? throw new ArgumentNullException(nameof(interactor));
    }

    public PlayerInteractionCandidate PrimaryInteraction { get; private set; }
    public PlayerInteractionCandidate PointerInteraction { get; private set; }
    public int RegisteredSourceCount => sources.Count;

    public bool Register(IPlayerInteractionSource source)
    {
        if (!IsSourceAlive(source) || ContainsReference(sources, source))
            return false;

        sources.Add(source);
        RemoveReference(suspendedSources, source);

        if (isActive && source is IPlayerInteractionLifecycleHandler lifecycle)
            InvokeSafely(source, () => lifecycle.OnInteractionSystemEnabled(interactor), "enable");

        return true;
    }

    public bool Unregister(IPlayerInteractionSource source)
    {
        int index = IndexOfReference(sources, source);
        if (index < 0)
            return false;

        if (PrimaryInteraction.IsValid && ReferenceEquals(PrimaryInteraction.Source, source))
        {
            TransitionFocus(PrimaryInteraction, default);
            PrimaryInteraction = default;
        }
        if (PointerInteraction.IsValid && ReferenceEquals(PointerInteraction.Source, source))
        {
            TransitionFocus(PointerInteraction, default);
            PointerInteraction = default;
        }

        sources.RemoveAt(index);
        RemoveReference(suspendedSources, source);

        if (isActive && source is IPlayerInteractionLifecycleHandler lifecycle)
            InvokeSafely(source, () => lifecycle.OnInteractionSystemDisabled(interactor), "disable");

        return true;
    }

    public void Activate()
    {
        if (isActive)
            return;

        // Uma nova ativacao permite que fontes corrigidas ou reconfiguradas sejam testadas novamente.
        suspendedSources.Clear();
        isActive = true;
        for (int i = 0; i < sources.Count; i++)
        {
            IPlayerInteractionSource source = sources[i];
            if (IsSourceAvailable(source) && source is IPlayerInteractionLifecycleHandler lifecycle)
                InvokeSafely(source, () => lifecycle.OnInteractionSystemEnabled(interactor), "enable");
        }
    }

    public void Deactivate()
    {
        if (!isActive)
            return;

        TransitionFocus(PrimaryInteraction, default);
        TransitionFocus(PointerInteraction, default);
        PrimaryInteraction = default;
        PointerInteraction = default;

        for (int i = 0; i < sources.Count; i++)
        {
            IPlayerInteractionSource source = sources[i];
            if (IsSourceAlive(source) && source is IPlayerInteractionLifecycleHandler lifecycle)
                InvokeSafely(source, () => lifecycle.OnInteractionSystemDisabled(interactor), "disable");
        }

        isActive = false;
    }

    public void Refresh()
    {
        if (!isActive)
            return;

        RemoveDestroyedSources();

        for (int i = 0; i < sources.Count; i++)
        {
            IPlayerInteractionSource source = sources[i];
            if (!IsSourceAvailable(source))
                continue;

            try
            {
                source.Refresh(interactor);
            }
            catch (Exception exception)
            {
                Suspend(source, "refresh", exception);
            }
        }

        PlayerInteractionCandidate nextPrimary = Resolve(PlayerInteractionInput.Primary);
        PlayerInteractionCandidate nextPointer = Resolve(PlayerInteractionInput.Pointer);

        TransitionFocus(PrimaryInteraction, nextPrimary);
        TransitionFocus(PointerInteraction, nextPointer);
        PrimaryInteraction = nextPrimary;
        PointerInteraction = nextPointer;

        for (int i = 0; i < sources.Count; i++)
        {
            IPlayerInteractionSource source = sources[i];
            if (!IsSourceAvailable(source) || source is not IPlayerInteractionPresentationHandler presentation)
                continue;

            try
            {
                presentation.UpdatePresentation(interactor, PrimaryInteraction, PointerInteraction);
            }
            catch (Exception exception)
            {
                Suspend(source, "presentation", exception);
            }
        }
    }

    public bool TryPerform(PlayerInteractionCandidate candidate, out object payload)
    {
        payload = null;
        if (!candidate.IsValid || !IsSourceAvailable(candidate.Source) ||
            candidate != GetCurrent(candidate.Input))
        {
            return false;
        }

        try
        {
            return candidate.Source.TryPerform(interactor, candidate, out payload);
        }
        catch (Exception exception)
        {
            Suspend(candidate.Source, "perform", exception);
            payload = null;
            return false;
        }
    }

    public bool NotifyHoldStarted(PlayerInteractionCandidate candidate)
    {
        if (!candidate.IsValid || candidate != PrimaryInteraction ||
            candidate.Source is not IPlayerInteractionHoldHandler holdHandler)
        {
            return candidate.IsValid && candidate == PrimaryInteraction;
        }

        return InvokeSafely(
            candidate.Source,
            () => holdHandler.OnHoldStarted(interactor, candidate),
            "hold start");
    }

    public void NotifyHoldCanceled(PlayerInteractionCandidate candidate)
    {
        if (!candidate.IsValid || candidate.Source is not IPlayerInteractionHoldHandler holdHandler)
            return;

        InvokeSafely(
            candidate.Source,
            () => holdHandler.OnHoldCanceled(interactor, candidate),
            "hold cancel");
    }

    private PlayerInteractionCandidate Resolve(PlayerInteractionInput input)
    {
        PlayerInteractionCandidate best = default;

        for (int i = 0; i < sources.Count; i++)
        {
            IPlayerInteractionSource source = sources[i];
            if (!IsSourceAvailable(source))
                continue;

            try
            {
                if (!source.TryGetCandidate(interactor, input, out PlayerInteractionCandidate candidate))
                    continue;

                if (!candidate.IsValid || !ReferenceEquals(candidate.Source, source) || candidate.Input != input)
                {
                    Suspend(source, "candidate contract", null);
                    continue;
                }

                if (!best.IsValid || candidate.Priority > best.Priority)
                    best = candidate;
            }
            catch (Exception exception)
            {
                Suspend(source, "candidate resolution", exception);
            }
        }

        return best;
    }

    private PlayerInteractionCandidate GetCurrent(PlayerInteractionInput input)
    {
        return input == PlayerInteractionInput.Pointer
            ? PointerInteraction
            : PrimaryInteraction;
    }

    private void TransitionFocus(
        PlayerInteractionCandidate previous,
        PlayerInteractionCandidate next)
    {
        if (previous == next)
            return;

        if (previous.IsValid && previous.Source is IPlayerInteractionFocusHandler previousFocus)
        {
            InvokeSafely(
                previous.Source,
                () => previousFocus.OnFocusExit(interactor, previous),
                "focus exit");
        }

        if (next.IsValid && next.Source is IPlayerInteractionFocusHandler nextFocus)
        {
            InvokeSafely(
                next.Source,
                () => nextFocus.OnFocusEnter(interactor, next),
                "focus enter");
        }
    }

    private void Suspend(IPlayerInteractionSource source, string phase, Exception exception)
    {
        if (!IsSourceAlive(source) || ContainsReference(suspendedSources, source))
            return;

        suspendedSources.Add(source);
        string sourceName = GetSourceName(source);
        Debug.LogError(
            $"Interaction source '{sourceName}' was suspended after a failure during {phase}. " +
            "Other interaction sources will continue running.",
            interactor);

        if (exception != null)
            Debug.LogException(exception, interactor);
    }

    private bool InvokeSafely(IPlayerInteractionSource source, Action callback, string phase)
    {
        try
        {
            callback();
            return true;
        }
        catch (Exception exception)
        {
            Suspend(source, phase, exception);
            return false;
        }
    }

    private void RemoveDestroyedSources()
    {
        for (int i = sources.Count - 1; i >= 0; i--)
        {
            if (IsSourceAlive(sources[i]))
                continue;

            RemoveReference(suspendedSources, sources[i]);
            sources.RemoveAt(i);
        }
    }

    private bool IsSourceAvailable(IPlayerInteractionSource source)
    {
        if (!IsSourceAlive(source) || ContainsReference(suspendedSources, source))
            return false;

        return source is not Behaviour behaviour || behaviour.isActiveAndEnabled;
    }

    private static bool IsSourceAlive(IPlayerInteractionSource source)
    {
        if (source == null)
            return false;

        return source is not UnityEngine.Object unityObject || unityObject != null;
    }

    private static string GetSourceName(IPlayerInteractionSource source)
    {
        if (!IsSourceAlive(source))
            return "destroyed";

        try
        {
            return string.IsNullOrWhiteSpace(source.SourceId)
                ? source.GetType().Name
                : source.SourceId;
        }
        catch
        {
            return source.GetType().Name;
        }
    }

    private static bool ContainsReference(
        List<IPlayerInteractionSource> collection,
        IPlayerInteractionSource source)
    {
        return IndexOfReference(collection, source) >= 0;
    }

    private static int IndexOfReference(
        List<IPlayerInteractionSource> collection,
        IPlayerInteractionSource source)
    {
        for (int i = 0; i < collection.Count; i++)
        {
            if (ReferenceEquals(collection[i], source))
                return i;
        }

        return -1;
    }

    private static void RemoveReference(
        List<IPlayerInteractionSource> collection,
        IPlayerInteractionSource source)
    {
        int index = IndexOfReference(collection, source);
        if (index >= 0)
            collection.RemoveAt(index);
    }
}
