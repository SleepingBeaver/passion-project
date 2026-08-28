using System;
using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(WorldInfoSystem))]
public class GameplayDayCycleController : MonoBehaviour
{
    private const int MinutesPerDay = 24 * 60;

    // Contratos publicos usados para explicar por que o dia terminou e qual foi o salto aplicado.
    public enum DayEndReason
    {
        VoluntarySleep = 0,
        ForcedAtEndOfDay = 1
    }

    public readonly struct DayTransition
    {
        public DayTransition(
            DayEndReason reason,
            int sleepHour24,
            int sleepMinute,
            int wakeHour24,
            bool calendarAdvancedDuringTransition)
        {
            Reason = reason;
            SleepHour24 = sleepHour24;
            SleepMinute = sleepMinute;
            WakeHour24 = wakeHour24;
            CalendarAdvancedDuringTransition = calendarAdvancedDuringTransition;
        }

        public DayEndReason Reason { get; }
        public int SleepHour24 { get; }
        public int SleepMinute { get; }
        public int WakeHour24 { get; }
        public bool CalendarAdvancedDuringTransition { get; }
    }

    // Configuracao serializada das regras do dia e da transicao visual.
    [Header("References")]
    [SerializeField] private WorldInfoSystem worldInfoSystem;

    [Header("Gameplay Day")]
    [SerializeField, Range(0, 23)] private int gameplayDayStartHour = 6;
    [SerializeField, Range(0, 23)] private int forcedEndHour = 2;

    [Header("Wake-up Rules")]
    [SerializeField, Range(0, 23)] private int earlySleepWakeHour = 6;
    [SerializeField, Range(0, 23)] private int afterMidnightWakeHour = 8;
    [SerializeField, Range(0, 23)] private int lateSleepStartsAtHour = 1;
    [SerializeField, Range(0, 23)] private int lateOrForcedWakeHour = 10;

    [Header("Day Transition Fade")]
    [SerializeField] private CanvasGroup transitionFadeCanvasGroup;
    [SerializeField, Min(0f)] private float fadeOutDuration = 0.75f;
    [SerializeField, Min(0f)] private float blackScreenDuration = 0.15f;
    [SerializeField, Min(0f)] private float fadeInDuration = 0.75f;
    [SerializeField] private bool pauseGameDuringTransition = true;

    // Estado transitorio necessario para impedir pedidos concorrentes e restaurar o jogo.
    private bool isTransitioning;
    private Coroutine transitionCoroutine;
    private float timeScaleBeforeTransition;
    private bool timeScaleCaptured;

    // API de consulta e eventos consumidos por plantacao, UI e validadores.
    public bool IsTransitioning => isTransitioning;

    public event Action SleepRequested;
    public event Action<DayTransition> DayTransitionStarted;
    public event Action<DayTransition> NewDayStarted;

    // Ciclo de vida e assinatura no relogio autoritativo.
    private void Reset()
    {
        worldInfoSystem = GetComponent<WorldInfoSystem>();
    }

    private void Awake()
    {
        ResolveReferences();
        HideTransitionFade();
    }

    private void OnEnable()
    {
        ResolveReferences();

        if (worldInfoSystem != null)
            worldInfoSystem.TimeChanged += HandleTimeChanged;
    }

    private void Start()
    {
        EvaluateForcedEndOfDay();
    }

    private void OnDisable()
    {
        if (worldInfoSystem != null)
            worldInfoSystem.TimeChanged -= HandleTimeChanged;

        CancelActiveTransition();
    }

    private void OnValidate()
    {
        ResolveReferences();
        lateSleepStartsAtHour = Mathf.Clamp(lateSleepStartsAtHour, 0, forcedEndHour);
    }

    // Ponto unico que a tecla temporaria e a futura cama devem chamar.
    public bool RequestSleep()
    {
        if (worldInfoSystem == null || isTransitioning)
            return false;

        if (IsForcedEndPeriod(worldInfoSystem.CurrentMinuteOfDay))
            return BeginDayTransition(DayEndReason.ForcedAtEndOfDay, lateOrForcedWakeHour, false);

        int wakeHour = ResolveVoluntaryWakeHour(worldInfoSystem.CurrentHour24);
        bool advanceCalendar = worldInfoSystem.CurrentHour24 >= gameplayDayStartHour;

        return BeginDayTransition(DayEndReason.VoluntarySleep, wakeHour, advanceCalendar);
    }

    public int ResolveVoluntaryWakeHour(int sleepHour24)
    {
        int sanitizedHour = Mathf.Clamp(sleepHour24, 0, 23);

        if (sanitizedHour >= gameplayDayStartHour)
            return earlySleepWakeHour;

        if (sanitizedHour < lateSleepStartsAtHour)
            return afterMidnightWakeHour;

        return lateOrForcedWakeHour;
    }

    // Deteccao do limite forcado de 02:00, inclusive quando um passo cruza o minuto exato.
    private void HandleTimeChanged(WorldInfoSystem.TimeChange change)
    {
        if (isTransitioning || worldInfoSystem == null)
            return;

        bool crossedForcedBoundary = change.AdvancedGameMinutes > 0L &&
                                     DidAdvanceAcrossMinute(
                                         change.PreviousMinuteOfDay,
                                         change.AdvancedGameMinutes,
                                         forcedEndHour * 60);

        if (crossedForcedBoundary || IsForcedEndPeriod(worldInfoSystem.CurrentMinuteOfDay))
            BeginDayTransition(DayEndReason.ForcedAtEndOfDay, lateOrForcedWakeHour, false);
    }

    private void EvaluateForcedEndOfDay()
    {
        if (!isTransitioning && worldInfoSystem != null &&
            IsForcedEndPeriod(worldInfoSystem.CurrentMinuteOfDay))
        {
            BeginDayTransition(DayEndReason.ForcedAtEndOfDay, lateOrForcedWakeHour, false);
        }
    }

    // Orquestracao da transicao; o salto de horario ocorre uma unica vez com a tela preta.
    private bool BeginDayTransition(DayEndReason reason, int wakeHour, bool advanceCalendar)
    {
        if (worldInfoSystem == null || isTransitioning)
            return false;

        isTransitioning = true;
        worldInfoSystem.SetTimeAdvancementSuspended(true);

        DayTransition transition = new(
            reason,
            worldInfoSystem.CurrentHour24,
            worldInfoSystem.CurrentMinute,
            wakeHour,
            advanceCalendar);

        try
        {
            if (reason == DayEndReason.VoluntarySleep)
                SleepRequested?.Invoke();

            DayTransitionStarted?.Invoke(transition);

            // Edit Mode keeps validation and editor tooling deterministic. At runtime,
            // the calendar/time jump happens only while the screen is fully black.
            if (Application.isPlaying && transitionFadeCanvasGroup != null)
            {
                ShowTransitionFade();
                PauseGameIfConfigured();
                transitionCoroutine = StartCoroutine(PerformDayTransition(transition));
            }
            else
            {
                CompleteTransitionImmediately(transition);
            }

            return true;
        }
        catch
        {
            ReleaseTransitionState();
            throw;
        }
    }

    private IEnumerator PerformDayTransition(DayTransition transition)
    {
        try
        {
            yield return Fade(0f, 1f, fadeOutDuration);

            worldInfoSystem.ApplyGameplayDayTransition(
                transition.WakeHour24,
                0,
                transition.CalendarAdvancedDuringTransition);
            NewDayStarted?.Invoke(transition);

            yield return WaitUsingUnscaledTime(blackScreenDuration);
            yield return Fade(1f, 0f, fadeInDuration);
        }
        finally
        {
            transitionCoroutine = null;
            ReleaseTransitionState();
        }
    }

    // Animacoes em tempo nao escalado continuam funcionando enquanto o gameplay esta pausado.
    private IEnumerator Fade(float fromAlpha, float toAlpha, float duration)
    {
        if (transitionFadeCanvasGroup == null)
            yield break;

        transitionFadeCanvasGroup.alpha = fromAlpha;

        if (duration <= 0f)
        {
            transitionFadeCanvasGroup.alpha = toAlpha;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            transitionFadeCanvasGroup.alpha = Mathf.Lerp(
                fromAlpha,
                toAlpha,
                Mathf.Clamp01(elapsed / duration));
            yield return null;
        }

        transitionFadeCanvasGroup.alpha = toAlpha;
    }

    private static IEnumerator WaitUsingUnscaledTime(float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private void CompleteTransitionImmediately(DayTransition transition)
    {
        try
        {
            worldInfoSystem.ApplyGameplayDayTransition(
                transition.WakeHour24,
                0,
                transition.CalendarAdvancedDuringTransition);
            NewDayStarted?.Invoke(transition);
        }
        finally
        {
            ReleaseTransitionState();
        }
    }

    private void ShowTransitionFade()
    {
        transitionFadeCanvasGroup.gameObject.SetActive(true);
        transitionFadeCanvasGroup.alpha = 0f;
        transitionFadeCanvasGroup.interactable = false;
        transitionFadeCanvasGroup.blocksRaycasts = true;
    }

    private void HideTransitionFade()
    {
        if (transitionFadeCanvasGroup == null)
            return;

        transitionFadeCanvasGroup.alpha = 0f;
        transitionFadeCanvasGroup.interactable = false;
        transitionFadeCanvasGroup.blocksRaycasts = false;
        transitionFadeCanvasGroup.gameObject.SetActive(false);
    }

    private void PauseGameIfConfigured()
    {
        if (!pauseGameDuringTransition || timeScaleCaptured)
            return;

        timeScaleBeforeTransition = Time.timeScale;
        timeScaleCaptured = true;
        Time.timeScale = 0f;
    }

    // Restauracao centralizada para desativacao, excecao ou termino normal da coroutine.
    private void CancelActiveTransition()
    {
        if (!isTransitioning)
        {
            HideTransitionFade();
            return;
        }

        Coroutine activeCoroutine = transitionCoroutine;
        transitionCoroutine = null;

        if (activeCoroutine != null)
            StopCoroutine(activeCoroutine);

        if (isTransitioning)
            ReleaseTransitionState();
    }

    private void ReleaseTransitionState()
    {
        if (timeScaleCaptured)
        {
            Time.timeScale = timeScaleBeforeTransition;
            timeScaleCaptured = false;
        }

        HideTransitionFade();

        if (worldInfoSystem != null)
            worldInfoSystem.SetTimeAdvancementSuspended(false);

        isTransitioning = false;
    }

    // Regras puras de horario, isoladas para facilitar validacao automatica.
    private bool IsForcedEndPeriod(int minuteOfDay)
    {
        int forcedMinute = forcedEndHour * 60;
        int gameplayStartMinute = gameplayDayStartHour * 60;

        return forcedMinute < gameplayStartMinute
            ? minuteOfDay >= forcedMinute && minuteOfDay < gameplayStartMinute
            : minuteOfDay >= forcedMinute || minuteOfDay < gameplayStartMinute;
    }

    private static bool DidAdvanceAcrossMinute(int previousMinuteOfDay, long advancedMinutes, int targetMinuteOfDay)
    {
        int normalizedPrevious = ((previousMinuteOfDay % MinutesPerDay) + MinutesPerDay) % MinutesPerDay;
        int normalizedTarget = ((targetMinuteOfDay % MinutesPerDay) + MinutesPerDay) % MinutesPerDay;
        int minutesUntilTarget = (normalizedTarget - normalizedPrevious + MinutesPerDay) % MinutesPerDay;

        if (minutesUntilTarget == 0)
            minutesUntilTarget = MinutesPerDay;

        return advancedMinutes >= minutesUntilTarget;
    }

    private void ResolveReferences()
    {
        if (worldInfoSystem == null)
            worldInfoSystem = GetComponent<WorldInfoSystem>();
    }
}
