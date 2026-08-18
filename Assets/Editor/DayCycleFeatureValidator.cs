using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static class DayCycleFeatureValidator
{
    private const string DevelopmentScenePath = "Assets/Scenes/SampleScene.unity";

    private readonly struct CalendarStamp : IEquatable<CalendarStamp>
    {
        public CalendarStamp(WorldInfoSystem world)
        {
            Season = world.CurrentSeason;
            Year = world.CurrentYear;
            Day = world.CurrentDayOfSeason;
            WeekDay = world.CurrentWeekDay;
        }

        public CalendarStamp(
            WorldInfoSystem.Season season,
            int year,
            int day,
            WorldInfoSystem.WeekDay weekDay)
        {
            Season = season;
            Year = year;
            Day = day;
            WeekDay = weekDay;
        }

        public WorldInfoSystem.Season Season { get; }
        public int Year { get; }
        public int Day { get; }
        public WorldInfoSystem.WeekDay WeekDay { get; }

        public bool Equals(CalendarStamp other)
        {
            return Season == other.Season && Year == other.Year && Day == other.Day && WeekDay == other.WeekDay;
        }

        public override bool Equals(object obj)
        {
            return obj is CalendarStamp other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine((int)Season, Year, Day, (int)WeekDay);
        }
    }

    [MenuItem("Tools/Day Cycle/Validate Feature", priority = 120)]
    private static void ValidateFromMenu()
    {
        int failures = Validate();
        EditorUtility.DisplayDialog(
            "Day Cycle Validation",
            failures == 0
                ? "Validacao concluida com sucesso. Consulte o Console para os detalhes."
                : $"A validacao encontrou {failures} falha(s). Consulte o Console.",
            "OK");
    }

    public static void ValidateCommandLine()
    {
        EditorApplication.Exit(Validate() == 0 ? 0 : 1);
    }

    private static int Validate()
    {
        int failures = 0;
        Debug.Log("[DAY-CYCLE-VALIDATION] Starting validation.");

        EditorSceneManager.OpenScene(DevelopmentScenePath, OpenSceneMode.Single);
        ValidateSceneConfiguration(ref failures);
        ValidateFunctionalScenarios(ref failures);

        Debug.Log($"[DAY-CYCLE-VALIDATION] Completed with {failures} failure(s).");
        return failures;
    }

    private static void ValidateSceneConfiguration(ref int failures)
    {
        WorldInfoSystem[] clocks = UnityEngine.Object.FindObjectsByType<WorldInfoSystem>(FindObjectsInactive.Include);
        GameplayDayCycleController[] dayCycles =
            UnityEngine.Object.FindObjectsByType<GameplayDayCycleController>(FindObjectsInactive.Include);
        TimeOfDayLightingController[] lightingControllers =
            UnityEngine.Object.FindObjectsByType<TimeOfDayLightingController>(FindObjectsInactive.Include);
        Light2D[] globalLights = UnityEngine.Object.FindObjectsByType<Light2D>(FindObjectsInactive.Include)
            .Where(light => light.lightType == Light2D.LightType.Global)
            .ToArray();

        Check(clocks.Length == 1, "Scene has exactly one authoritative WorldInfoSystem.", ref failures);
        Check(dayCycles.Length == 1, "Scene has exactly one GameplayDayCycleController.", ref failures);
        Check(lightingControllers.Length == 1, "Scene has exactly one TimeOfDayLightingController.", ref failures);
        Check(globalLights.Length == 1, "Scene has exactly one Global Light 2D.", ref failures);

        if (clocks.Length == 1 && dayCycles.Length == 1 && lightingControllers.Length == 1)
        {
            Check(dayCycles[0].gameObject == clocks[0].gameObject,
                "Gameplay day controller extends the authoritative clock object.", ref failures);
            Check(lightingControllers[0].gameObject == clocks[0].gameObject,
                "Lighting controller is driven from the authoritative clock object.", ref failures);
        }

        InventoryDebugInput debugInput = UnityEngine.Object.FindAnyObjectByType<InventoryDebugInput>(FindObjectsInactive.Include);
        if (debugInput != null)
        {
            SerializedObject debugInputObject = new(debugInput);
            SerializedProperty dayCycleReference = debugInputObject.FindProperty("gameplayDayCycleController");
            Check(dayCycleReference?.objectReferenceValue != null,
                "NumPad 1 debug caller is linked to the centralized day cycle.", ref failures);
        }
        else
        {
            Check(false, "InventoryDebugInput is available for the temporary sleep shortcut.", ref failures);
        }

        if (globalLights.Length == 1)
        {
            int sortingLayerCount = SortingLayer.layers.Length;
            Check(globalLights[0].targetSortingLayers?.Length == sortingLayerCount,
                "Global Light 2D affects every configured sorting layer.", ref failures);
        }
    }

    private static void ValidateFunctionalScenarios(ref int failures)
    {
        GameObject testObject = new("DayCycleValidation_Temporary");
        testObject.SetActive(false);
        GameplayDayCycleController dayCycle = null;

        try
        {
            WorldInfoSystem world = testObject.AddComponent<WorldInfoSystem>();
            dayCycle = testObject.AddComponent<GameplayDayCycleController>();
            Light2D light = testObject.AddComponent<Light2D>();
            TimeOfDayLightingController lighting = testObject.AddComponent<TimeOfDayLightingController>();

            SerializedObject worldObject = new(world);
            worldObject.FindProperty("autoAdvanceTime").boolValue = false;
            worldObject.ApplyModifiedPropertiesWithoutUndo();

            testObject.SetActive(true);
            lighting.enabled = false;
            lighting.enabled = true;

            // MonoBehaviours sem ExecuteAlways nao recebem OnEnable em Edit Mode.
            // Reproduz explicitamente a inscricao que o Play Mode executa.
            InvokeLifecycle(dayCycle, "OnDisable");
            InvokeLifecycle(dayCycle, "OnEnable");

            int transitionStartedCount = 0;
            int newDayStartedCount = 0;
            int forcedTransitionCount = 0;
            int sleepRequestedCount = 0;
            int reentrantSleepAcceptedCount = 0;
            dayCycle.SleepRequested += () =>
            {
                sleepRequestedCount++;
                if (dayCycle.RequestSleep())
                    reentrantSleepAcceptedCount++;
            };
            dayCycle.DayTransitionStarted += transition =>
            {
                transitionStartedCount++;
                if (transition.Reason == GameplayDayCycleController.DayEndReason.ForcedAtEndOfDay)
                    forcedTransitionCount++;
            };
            dayCycle.NewDayStarted += _ => newDayStartedCount++;

            ValidateVoluntaryBeforeMidnight(world, dayCycle, light, 22, 0, "22:00", ref failures);
            ValidateVoluntaryBeforeMidnight(world, dayCycle, light, 23, 59, "23:59", ref failures);
            ValidateAfterMidnight(world, dayCycle, 1, 8, "00:00", ref failures);
            ValidateAfterMidnight(world, dayCycle, 4, 8, "00:30", ref failures);
            ValidateAfterMidnight(world, dayCycle, 10, 10, "01:30", ref failures);
            ValidateForcedEnd(world, light, ref failures);
            ValidateLightingTimeline(world, light, ref failures);

            Check(transitionStartedCount == 6,
                "Exactly one transition starts for each of the five sleeps and the forced end.", ref failures);
            Check(newDayStartedCount == transitionStartedCount,
                "Every started day transition completes with NewDayStarted.", ref failures);
            Check(forcedTransitionCount == 1,
                "The 02:00 forced transition fires exactly once.", ref failures);
            Check(sleepRequestedCount == 5,
                "SleepRequested fires once for each voluntary sleep only.", ref failures);
            Check(reentrantSleepAcceptedCount == 0,
                "Reentrant sleep requests are rejected while a transition is active.", ref failures);
        }
        finally
        {
            if (dayCycle != null)
                InvokeLifecycle(dayCycle, "OnDisable");

            UnityEngine.Object.DestroyImmediate(testObject);
        }
    }

    private static void ValidateVoluntaryBeforeMidnight(
        WorldInfoSystem world,
        GameplayDayCycleController dayCycle,
        Light2D light,
        int hour,
        int minute,
        string label,
        ref int failures)
    {
        SetKnownCalendar(world);
        world.SetTime(hour, minute);
        CalendarStamp before = new(world);
        bool accepted = dayCycle.RequestSleep();
        CalendarStamp after = new(world);

        Check(accepted, $"Sleep at {label} is accepted.", ref failures);
        Check(world.CurrentHour24 == 6 && world.CurrentMinute == 0,
            $"Sleep at {label} wakes at 06:00.", ref failures);
        Check(after.Equals(GetExpectedNextDay(before)),
            $"Sleep at {label} advances the calendar exactly once.", ref failures);
        Check(light.color.b > light.color.r && Approximately(light.intensity, 0.68f),
            $"Sleep at {label} immediately applies early-morning lighting.", ref failures);
    }

    private static void ValidateAfterMidnight(
        WorldInfoSystem world,
        GameplayDayCycleController dayCycle,
        int stepsAfter2350,
        int expectedWakeHour,
        string label,
        ref int failures)
    {
        SetKnownCalendar(world);
        world.SetTime(23, 50);
        world.AdvanceTimeSteps(stepsAfter2350);
        CalendarStamp afterCivilMidnight = new(world);

        bool accepted = dayCycle.RequestSleep();
        CalendarStamp afterSleep = new(world);

        Check(accepted, $"Sleep at {label} is accepted.", ref failures);
        Check(world.CurrentHour24 == expectedWakeHour && world.CurrentMinute == 0,
            $"Sleep at {label} wakes at {expectedWakeHour:00}:00.", ref failures);
        Check(afterSleep.Equals(afterCivilMidnight),
            $"Sleep at {label} does not advance the calendar a second time.", ref failures);
    }

    private static void ValidateForcedEnd(WorldInfoSystem world, Light2D light, ref int failures)
    {
        SetKnownCalendar(world);
        world.SetTime(23, 50);
        world.AdvanceTimeSteps(12);
        CalendarStamp beforeForcedEnd = new(world);

        world.AdvanceTimeSteps(1);
        CalendarStamp afterForcedEnd = new(world);

        Check(world.CurrentHour24 == 10 && world.CurrentMinute == 0,
            "Reaching 02:00 forces a wake-up at 10:00.", ref failures);
        Check(afterForcedEnd.Equals(beforeForcedEnd),
            "The forced 02:00 transition does not duplicate the midnight calendar advance.", ref failures);
        Check(light.intensity > 0.9f,
            "The forced transition immediately synchronizes daytime lighting.", ref failures);

        world.AdvanceTimeSteps(1);
        Check(world.CurrentHour24 == 10 && world.CurrentMinute == 10,
            "Normal time progression resumes after the forced transition.", ref failures);
    }

    private static void ValidateLightingTimeline(WorldInfoSystem world, Light2D light, ref int failures)
    {
        world.SetTime(18, 0);
        Color sunset = light.color;
        Check(sunset.r > sunset.g && sunset.g > sunset.b,
            "18:00 lighting has a warm orange/golden balance.", ref failures);

        world.SetTime(23, 0);
        Color night = light.color;
        Check(night.b > night.g && night.g > night.r && light.intensity < 0.5f,
            "23:00 lighting is darker and blue-toned immediately after a time jump.", ref failures);

        world.SetTime(0, 30);
        Color lateNight = light.color;
        Check(lateNight.b > lateNight.r && light.intensity < 0.45f,
            "00:30 lighting uses the deeper late-night phase.", ref failures);
    }

    private static void SetKnownCalendar(WorldInfoSystem world)
    {
        SerializedObject serializedWorld = new(world);
        serializedWorld.FindProperty("currentSeason").enumValueIndex = (int)WorldInfoSystem.Season.Summer;
        serializedWorld.FindProperty("currentYear").intValue = 3;
        serializedWorld.FindProperty("currentDayOfSeason").intValue = 10;
        serializedWorld.FindProperty("currentWeekDay").enumValueIndex = (int)WorldInfoSystem.WeekDay.Tuesday;
        serializedWorld.FindProperty("weatherDayIndex").intValue = 20;
        serializedWorld.ApplyModifiedPropertiesWithoutUndo();
    }

    private static CalendarStamp GetExpectedNextDay(CalendarStamp current)
    {
        int day = current.Day + 1;
        int year = current.Year;
        WorldInfoSystem.Season season = current.Season;
        WorldInfoSystem.WeekDay weekDay =
            (WorldInfoSystem.WeekDay)(((int)current.WeekDay + 1) % Enum.GetValues(typeof(WorldInfoSystem.WeekDay)).Length);

        if (day > WorldInfoSystem.DaysPerSeason)
        {
            day = 1;
            season = (WorldInfoSystem.Season)(((int)season + 1) % Enum.GetValues(typeof(WorldInfoSystem.Season)).Length);
            if (season == WorldInfoSystem.Season.Spring)
                year++;
        }

        return new CalendarStamp(season, year, day, weekDay);
    }

    private static bool Approximately(float left, float right)
    {
        return Mathf.Abs(left - right) < 0.001f;
    }

    private static void Check(bool condition, string message, ref int failures)
    {
        if (condition)
        {
            Debug.Log($"[DAY-CYCLE-VALIDATION][PASS] {message}");
            return;
        }

        failures++;
        Debug.LogError($"[DAY-CYCLE-VALIDATION][FAIL] {message}");
    }

    private static void InvokeLifecycle(MonoBehaviour target, string methodName)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method?.Invoke(target, null);
    }

}
