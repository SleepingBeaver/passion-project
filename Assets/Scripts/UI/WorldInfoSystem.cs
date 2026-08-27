using System;
using UnityEngine;
using UnityEngine.Serialization;

public class WorldInfoSystem : MonoBehaviour
{
    // Limites principais do calendario e da economia.
    public const int DaysPerSeason = 30;
    public const int MinutesPerStep = 10;
    public const int MaxMoney = 9_999_999;
    private const int MinutesPerDay = 24 * 60;

    // Enumeracoes que definem os estados do mundo.
    public enum Season
    {
        Spring = 0,
        Summer = 1,
        Autumn = 2,
        Winter = 3
    }

    public enum WeekDay
    {
        Monday = 0,
        Tuesday = 1,
        Wednesday = 2,
        Thursday = 3,
        Friday = 4,
        Saturday = 5,
        Sunday = 6
    }

    public enum WeatherType
    {
        Sunny = 0,
        Partial = 1,
        Cloudy = 2,
        Windy = 3,
        Rain = 4,
        Storm = 5
    }

    public readonly struct TimeChange
    {
        public TimeChange(
            int previousMinuteOfDay,
            int currentMinuteOfDay,
            long advancedGameMinutes,
            int calendarDaysAdvanced,
            bool isJump)
        {
            PreviousMinuteOfDay = previousMinuteOfDay;
            CurrentMinuteOfDay = currentMinuteOfDay;
            AdvancedGameMinutes = advancedGameMinutes;
            CalendarDaysAdvanced = calendarDaysAdvanced;
            IsJump = isJump;
        }

        public int PreviousMinuteOfDay { get; }
        public int CurrentMinuteOfDay { get; }
        public long AdvancedGameMinutes { get; }
        public int CalendarDaysAdvanced { get; }
        public bool IsJump { get; }
    }

    private static readonly int SeasonCount = Enum.GetValues(typeof(Season)).Length;
    private static readonly int WeekDayCount = Enum.GetValues(typeof(WeekDay)).Length;
    private static readonly int WeatherTypeCount = Enum.GetValues(typeof(WeatherType)).Length;

    // Configuracao de calendario.
    [Header("Calendar")]
    [SerializeField] private Season currentSeason = Season.Summer;
    [SerializeField, Min(1)] private int currentYear = 1;
    [SerializeField, Range(1, DaysPerSeason)] private int currentDayOfSeason = 22;
    [SerializeField] private WeekDay currentWeekDay = WeekDay.Monday;

    // Configuracao de tempo.
    [Header("Time")]
    [SerializeField, Range(0, 23)] private int currentHour24 = 6;
    [SerializeField, Range(0, 59)] private int currentMinute = 0;
    [SerializeField] private bool autoAdvanceTime = true;
    [FormerlySerializedAs("realSecondsPerTimeStep")]
    [SerializeField, Min(0.05f), Tooltip("Quantos segundos reais levam para o relogio avancar 10 minutos no jogo.")]
    private float secondsPerTimeAdvance = 1f;

    // Configuracao de clima.
    [Header("Weather")]
    [SerializeField] private bool randomizeWeatherOnDayChange = true;
    [SerializeField] private int weatherSeed = 2048;
    [SerializeField, Min(0)] private int weatherDayIndex;
    [SerializeField] private WeatherType currentWeather = WeatherType.Sunny;

    // Configuracao economica.
    [Header("Economy")]
    [SerializeField, Range(0, MaxMoney)] private int money = 200;

    // Estado interno do acumulador de tempo.
    private float timeAccumulator;
    private bool timeAdvancementSuspended;

    // Leitura publica do estado atual do mundo.
    public Season CurrentSeason => currentSeason;
    public int CurrentYear => currentYear;
    public int CurrentDayOfSeason => currentDayOfSeason;
    public WeekDay CurrentWeekDay => currentWeekDay;
    public int CurrentHour24 => currentHour24;
    public int CurrentMinute => currentMinute;
    public int CurrentMinuteOfDay => currentHour24 * 60 + currentMinute;
    public WeatherType CurrentWeather => currentWeather;
    public int Money => money;
    public bool IsTimeAdvancementSuspended => timeAdvancementSuspended;

    // Eventos mantem a UI existente e permitem integracoes especificas sem polling.
    public event Action InfoChanged;
    public event Action<TimeChange> TimeChanged;
    public event Action CalendarDayAdvanced;

    // Ciclo de vida.
    private void Awake()
    {
        SanitizeState();

        if (randomizeWeatherOnDayChange)
            currentWeather = ResolveWeatherForCurrentCycle();

        NotifyInfoChanged();
    }

    private void OnValidate()
    {
        SanitizeState();

        if (randomizeWeatherOnDayChange)
            currentWeather = ResolveWeatherForCurrentCycle();

        NotifyInfoChanged();
    }

    private void Update()
    {
        if (!autoAdvanceTime || timeAdvancementSuspended || secondsPerTimeAdvance <= 0f)
            return;

        timeAccumulator += Time.deltaTime;
        int elapsedSteps = Mathf.FloorToInt(timeAccumulator / secondsPerTimeAdvance);
        if (elapsedSteps <= 0)
            return;

        timeAccumulator -= elapsedSteps * secondsPerTimeAdvance;
        TimeChange change = AdvanceTimeStepsInternal(elapsedSteps);
        NotifyTimeChanged(change);
        NotifyInfoChanged();
    }

    // Acoes rapidas uteis para debug no Inspector.
    [ContextMenu("Advance 10 Minutes")]
    public void AdvanceTenMinutes()
    {
        AdvanceTimeSteps(1);
    }

    [ContextMenu("Advance Day")]
    public void AdvanceDay()
    {
        AdvanceDayInternal();
        NotifyInfoChanged();
    }

    [ContextMenu("Cycle Weather")]
    public void CycleWeather()
    {
        currentWeather = (WeatherType)(((int)currentWeather + 1) % WeatherTypeCount);
        NotifyInfoChanged();
    }

    [ContextMenu("Add 100 Money")]
    public void AddOneHundredMoney()
    {
        AddMoney(100);
    }

    // API publica para alterar o estado do mundo.
    public void AdvanceTimeSteps(int stepCount)
    {
        if (stepCount <= 0)
            return;

        TimeChange change = AdvanceTimeStepsInternal(stepCount);
        NotifyTimeChanged(change);
        NotifyInfoChanged();
    }

    public void SetTime(int hour24, int minute)
    {
        int previousMinuteOfDay = CurrentMinuteOfDay;
        currentHour24 = Mathf.Clamp(hour24, 0, 23);
        currentMinute = NormalizeMinute(minute);

        NotifyTimeChanged(new TimeChange(
            previousMinuteOfDay,
            CurrentMinuteOfDay,
            advancedGameMinutes: 0L,
            calendarDaysAdvanced: 0,
            isJump: true));
        NotifyInfoChanged();
    }

    public void SetTimeAdvancementSuspended(bool suspended)
    {
        timeAdvancementSuspended = suspended;
    }

    // Aplica data e horario de despertar de forma atomica. O controlador do dia
    // informa se a meia-noite civil ja avancou o calendario nesta sessao.
    public void ApplyGameplayDayTransition(int wakeHour24, int wakeMinute, bool advanceCalendar)
    {
        int previousMinuteOfDay = CurrentMinuteOfDay;
        int calendarDaysAdvanced = 0;

        if (advanceCalendar)
        {
            AdvanceDayInternal();
            calendarDaysAdvanced = 1;
        }

        currentHour24 = Mathf.Clamp(wakeHour24, 0, 23);
        currentMinute = NormalizeMinute(wakeMinute);
        timeAccumulator = 0f;

        NotifyTimeChanged(new TimeChange(
            previousMinuteOfDay,
            CurrentMinuteOfDay,
            advancedGameMinutes: 0L,
            calendarDaysAdvanced,
            isJump: true));
        NotifyInfoChanged();
    }

    public void SetMoney(int value)
    {
        int sanitizedValue = Mathf.Clamp(value, 0, MaxMoney);
        if (money == sanitizedValue)
            return;

        money = sanitizedValue;
        NotifyInfoChanged();
    }

    public void AddMoney(int amount)
    {
        if (amount == 0)
            return;

        // Soma em 64 bits para que entradas extremas nao estourem o int antes do clamp.
        long updatedMoney = (long)money + amount;
        SetMoney((int)Math.Clamp(updatedMoney, 0L, MaxMoney));
    }

    public bool TrySpendMoney(int amount)
    {
        if (amount <= 0)
            return true;

        if (money < amount)
            return false;

        SetMoney(money - amount);
        return true;
    }

    public void SetWeather(WeatherType weatherType)
    {
        if (currentWeather == weatherType)
            return;

        currentWeather = weatherType;
        NotifyInfoChanged();
    }

    public string GetFormattedTime12Hour()
    {
        int hour12 = currentHour24 % 12;
        if (hour12 == 0)
            hour12 = 12;

        string period = currentHour24 >= 12 ? "PM" : "AM";
        return $"{hour12:00}:{currentMinute:00} {period}";
    }

    // Avanco interno do calendario, clima e horario.
    private TimeChange AdvanceTimeStepsInternal(int stepCount)
    {
        int previousMinuteOfDay = CurrentMinuteOfDay;
        long totalMinutes = currentHour24 * 60L + currentMinute + stepCount * (long)MinutesPerStep;
        int daysToAdvance = (int)(totalMinutes / MinutesPerDay);
        int minuteOfDay = (int)(totalMinutes % MinutesPerDay);

        currentHour24 = minuteOfDay / 60;
        currentMinute = minuteOfDay % 60;

        for (int i = 0; i < daysToAdvance; i++)
            AdvanceDayInternal();

        return new TimeChange(
            previousMinuteOfDay,
            CurrentMinuteOfDay,
            stepCount * (long)MinutesPerStep,
            daysToAdvance,
            isJump: false);
    }

    private void AdvanceDayInternal()
    {
        currentDayOfSeason++;
        currentWeekDay = (WeekDay)(((int)currentWeekDay + 1) % WeekDayCount);

        if (currentDayOfSeason > DaysPerSeason)
        {
            currentDayOfSeason = 1;
            currentSeason = (Season)(((int)currentSeason + 1) % SeasonCount);

            if (currentSeason == Season.Spring)
                currentYear++;
        }

        if (randomizeWeatherOnDayChange)
        {
            weatherDayIndex++;
            currentWeather = ResolveWeatherForCurrentCycle();
        }

        CalendarDayAdvanced?.Invoke();
    }

    private WeatherType ResolveWeatherForCurrentCycle()
    {
        System.Random random = new(weatherSeed + weatherDayIndex * 7919);
        return (WeatherType)random.Next(0, WeatherTypeCount);
    }

    // Utilitarios de saneamento e notificacao.
    private void SanitizeState()
    {
        currentYear = Mathf.Max(1, currentYear);
        currentDayOfSeason = Mathf.Clamp(currentDayOfSeason, 1, DaysPerSeason);
        currentHour24 = Mathf.Clamp(currentHour24, 0, 23);
        currentMinute = NormalizeMinute(currentMinute);
        money = Mathf.Clamp(money, 0, MaxMoney);
        weatherDayIndex = Mathf.Max(0, weatherDayIndex);
        timeAccumulator = Mathf.Max(0f, timeAccumulator);
    }

    private static int NormalizeMinute(int minute)
    {
        return Mathf.Clamp(minute, 0, 59);
    }

    private void NotifyInfoChanged()
    {
        InfoChanged?.Invoke();
    }

    private void NotifyTimeChanged(TimeChange change)
    {
        TimeChanged?.Invoke(change);
    }
}
