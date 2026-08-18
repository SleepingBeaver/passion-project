using System;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(WorldInfoSystem))]
public class GameplayDayCycleController : MonoBehaviour
{
    private const int MinutesPerDay = 24 * 60;

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

    private bool isTransitioning;

    public bool IsTransitioning => isTransitioning;

    public event Action SleepRequested;
    public event Action<DayTransition> DayTransitionStarted;
    public event Action<DayTransition> NewDayStarted;

    private void Reset()
    {
        worldInfoSystem = GetComponent<WorldInfoSystem>();
    }

    private void Awake()
    {
        ResolveReferences();
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
            worldInfoSystem.ApplyGameplayDayTransition(wakeHour, 0, advanceCalendar);
            NewDayStarted?.Invoke(transition);
            return true;
        }
        finally
        {
            worldInfoSystem.SetTimeAdvancementSuspended(false);
            isTransitioning = false;
        }
    }

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
