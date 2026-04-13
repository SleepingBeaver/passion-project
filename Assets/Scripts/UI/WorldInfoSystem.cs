using System;
using UnityEngine;
using UnityEngine.Serialization;

public class WorldInfoSystem : MonoBehaviour
{
    // Limites principais do calendario e da economia.
    public const int DaysPerSeason = 30;
    public const int MinutesPerStep = 10;
    public const int MaxMoney = 9_999_999;

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

    // Configuracao de calendario.
    [Header("Calendar")]
    [SerializeField] private Season currentSeason = Season.Summer;
    [SerializeField, Min(1)] private int currentYear = 1;
    [SerializeField, Range(1, DaysPerSeason)] private int currentDayOfSeason = 22;
    [SerializeField] private WeekDay currentWeekDay = WeekDay.Monday;

    // Configuracao de tempo.
    [Header("Time")]
    [SerializeField, Range(0, 23)] private int currentHour24 = 6;
    [SerializeField, Range(0, 50)] private int currentMinute = 0;
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

    // Leitura publica do estado atual do mundo.
    public Season CurrentSeason => currentSeason;
    public int CurrentYear => currentYear;
    public int CurrentDayOfSeason => currentDayOfSeason;
    public WeekDay CurrentWeekDay => currentWeekDay;
    public int CurrentHour24 => currentHour24;
    public int CurrentMinute => currentMinute;
    public WeatherType CurrentWeather => currentWeather;
    public int Money => money;

    // Evento para avisar a UI que algo mudou.
    public event Action InfoChanged;

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
        if (!autoAdvanceTime || secondsPerTimeAdvance <= 0f)
            return;

        timeAccumulator += Time.deltaTime;
        bool advancedTime = false;

        while (timeAccumulator >= secondsPerTimeAdvance)
        {
            timeAccumulator -= secondsPerTimeAdvance;
            AdvanceTimeStepsInternal(1);
            advancedTime = true;
        }

        if (advancedTime)
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
        currentWeather = (WeatherType)(((int)currentWeather + 1) % Enum.GetValues(typeof(WeatherType)).Length);
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

        AdvanceTimeStepsInternal(stepCount);
        NotifyInfoChanged();
    }

    public void SetTime(int hour24, int minute)
    {
        currentHour24 = Mathf.Clamp(hour24, 0, 23);
        currentMinute = NormalizeMinute(minute);
        NotifyInfoChanged();
    }

    public void SetMoney(int value)
    {
        money = Mathf.Clamp(value, 0, MaxMoney);
        NotifyInfoChanged();
    }

    public void AddMoney(int amount)
    {
        if (amount == 0)
            return;

        SetMoney(money + amount);
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
    private void AdvanceTimeStepsInternal(int stepCount)
    {
        for (int i = 0; i < stepCount; i++)
        {
            currentMinute += MinutesPerStep;

            if (currentMinute >= 60)
            {
                currentMinute = 0;
                currentHour24++;
            }

            if (currentHour24 >= 24)
            {
                currentHour24 = 0;
                AdvanceDayInternal();
            }
        }
    }

    private void AdvanceDayInternal()
    {
        currentDayOfSeason++;
        currentWeekDay = (WeekDay)(((int)currentWeekDay + 1) % Enum.GetValues(typeof(WeekDay)).Length);

        if (currentDayOfSeason > DaysPerSeason)
        {
            currentDayOfSeason = 1;
            currentSeason = (Season)(((int)currentSeason + 1) % Enum.GetValues(typeof(Season)).Length);

            if (currentSeason == Season.Spring)
                currentYear++;
        }

        if (randomizeWeatherOnDayChange)
        {
            weatherDayIndex++;
            currentWeather = ResolveWeatherForCurrentCycle();
        }
    }

    private WeatherType ResolveWeatherForCurrentCycle()
    {
        System.Random random = new(weatherSeed + weatherDayIndex * 7919);
        int weatherCount = Enum.GetValues(typeof(WeatherType)).Length;
        return (WeatherType)random.Next(0, weatherCount);
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
        int rounded = Mathf.RoundToInt(minute / (float)MinutesPerStep) * MinutesPerStep;
        return Mathf.Clamp(rounded, 0, 50);
    }

    private void NotifyInfoChanged()
    {
        InfoChanged?.Invoke();
    }
}
