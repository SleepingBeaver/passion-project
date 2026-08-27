using System;
using System.Globalization;
using System.Linq;
using Unity.Profiling;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class ProjectPerformanceValidator
{
    // Configuracao da amostra e chaves usadas para atravessar a troca Edit/Play Mode.
    private const string DevelopmentScenePath = "Assets/Scenes/SampleScene.unity";
    private const string RequestKey = "PassionProject.Performance.Request";
    private const string StopKey = "PassionProject.Performance.Stop";
    private const string ResultKey = "PassionProject.Performance.Result";
    private const int SceneWarmupFrames = 120;
    private const int UiWarmupFrames = 60;
    private const int SampleFrames = 300;
    private const int InventoryExerciseInterval = 30;

    private static ProfilerRecorder mainThreadRecorder;
    private static ProfilerRecorder gcAllocationRecorder;
    private static ProfilerRecorder drawCallsRecorder;
    private static ProfilerRecorder setPassRecorder;

    private static int lastGameFrame = -1;
    private static int observedFrames;
    private static int sampledFrames;
    private static double deltaTimeTotal;
    private static long mainThreadNanosecondsTotal;
    private static long mainThreadNanosecondsMax;
    private static long gcBytesTotal;
    private static long gcBytesMax;
    private static long drawCallsTotal;
    private static long setPassTotal;
    private static long workloadTicksTotal;
    private static long workloadTicksMax;
    private static long workloadGcBytesTotal;
    private static long workloadGcBytesMax;
    private static int workloadSamples;
    private static int successfulInventoryMutations;
    private static bool craftingTransactionPassed;
    private static InventorySystem inventorySystem;
    private static ItemData exerciseItem;
    private static CraftingUI craftingUI;

    // Retoma automaticamente uma medicao pendente depois do domain reload.
    static ProjectPerformanceValidator()
    {
        if (SessionState.GetBool(RequestKey, false))
            EditorApplication.delayCall += ResumeProfiling;
    }

    // Entrada de CI e preparacao controlada da sessao de profiling.
    public static void ProfilePlayModeCommandLine()
    {
        EditorSceneManager.OpenScene(DevelopmentScenePath, OpenSceneMode.Single);
        SessionState.SetBool(RequestKey, true);
        SessionState.SetBool(StopKey, false);
        SessionState.EraseString(ResultKey);
        ResetCounters();
        HookUpdate();
        Debug.Log("[PERFORMANCE-VALIDATION] Entering Play Mode for a controlled performance sample.");
        EditorApplication.isPlaying = true;
    }

    private static void ResumeProfiling()
    {
        if (!SessionState.GetBool(RequestKey, false))
            return;

        if (SessionState.GetBool(StopKey, false) && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            FinishProfiling();
            return;
        }

        HookUpdate();
    }

    private static void HookUpdate()
    {
        EditorApplication.update -= UpdateProfiling;
        EditorApplication.update += UpdateProfiling;
    }

    private static void UpdateProfiling()
    {
        if (!SessionState.GetBool(RequestKey, false))
            return;

        if (!EditorApplication.isPlaying)
        {
            if (SessionState.GetBool(StopKey, false) && !EditorApplication.isPlayingOrWillChangePlaymode)
                FinishProfiling();

            return;
        }

        if (Time.frameCount == lastGameFrame)
            return;

        lastGameFrame = Time.frameCount;
        observedFrames++;

        if (observedFrames == SceneWarmupFrames)
            PrepareUiWorkload();

        if (observedFrames < SceneWarmupFrames + UiWarmupFrames)
            return;

        if (sampledFrames == 0)
            StartRecorders();

        sampledFrames++;
        CaptureFrameSample();

        if (sampledFrames % InventoryExerciseInterval == 0)
            ExerciseInventoryAndUi();

        if (sampledFrames < SampleFrames)
            return;

        craftingTransactionPassed = ValidateCraftingTransaction();
        SaveResult();
        DisposeRecorders();

        if (craftingUI != null && craftingUI.IsOpen)
            craftingUI.Close();

        SessionState.SetBool(StopKey, true);
        EditorApplication.isPlaying = false;
    }

    // Carga funcional representativa de inventario, UI e crafting.
    private static void PrepareUiWorkload()
    {
        inventorySystem = UnityEngine.Object.FindAnyObjectByType<InventorySystem>();
        craftingUI = CraftingUI.Instance != null ? CraftingUI.Instance : CraftingUI.GetOrCreate();

        if (inventorySystem != null)
        {
            exerciseItem = inventorySystem.Slots
                .FirstOrDefault(slot => slot != null && !slot.IsEmpty && slot.item != null && !slot.item.isUnique)?.item;

            exerciseItem ??= inventorySystem.Slots
                .FirstOrDefault(slot => slot != null && !slot.IsEmpty)?.item;

            if (exerciseItem == null || exerciseItem.isUnique)
            {
                exerciseItem = Resources.FindObjectsOfTypeAll<ItemData>()
                    .FirstOrDefault(item => item != null && !item.isUnique);
            }
        }

        bool craftingOpened = craftingUI != null && craftingUI.Open();
        Debug.Log($"[PERFORMANCE-VALIDATION] UI workload ready. CraftingOpen={craftingOpened}, " +
                  $"ExerciseItem={exerciseItem?.name ?? "none"}, Unique={exerciseItem != null && exerciseItem.isUnique}.");
    }

    private static void ExerciseInventoryAndUi()
    {
        if (inventorySystem == null || exerciseItem == null)
            return;

        long gcBytesBefore = GC.GetAllocatedBytesForCurrentThread();
        long ticksBefore = System.Diagnostics.Stopwatch.GetTimestamp();

        inventorySystem.AddItem(exerciseItem, 1, out int addedAmount);
        if (addedAmount > 0)
        {
            successfulInventoryMutations++;
            inventorySystem.RemoveItem(exerciseItem, addedAmount);
        }

        long elapsedTicks = System.Diagnostics.Stopwatch.GetTimestamp() - ticksBefore;
        long allocatedBytes = Math.Max(0L, GC.GetAllocatedBytesForCurrentThread() - gcBytesBefore);
        workloadTicksTotal += elapsedTicks;
        workloadTicksMax = Math.Max(workloadTicksMax, elapsedTicks);
        workloadGcBytesTotal += allocatedBytes;
        workloadGcBytesMax = Math.Max(workloadGcBytesMax, allocatedBytes);
        workloadSamples++;
    }

    private static bool ValidateCraftingTransaction()
    {
        CraftingSystem craftingSystem = UnityEngine.Object.FindAnyObjectByType<CraftingSystem>();
        CraftingRecipeDefinition recipe = craftingSystem?.Recipes.FirstOrDefault(candidate => candidate != null && candidate.IsValid);
        if (craftingSystem == null || inventorySystem == null || recipe == null)
        {
            Debug.LogError("[PERFORMANCE-VALIDATION][FAIL] A valid crafting transaction could not be prepared.");
            return false;
        }

        CraftingIngredientRequirement[] ingredients = recipe.Ingredients;
        for (int i = 0; i < ingredients.Length; i++)
        {
            CraftingIngredientRequirement ingredient = ingredients[i];
            if (ingredient == null || ingredient.Item == null || ingredient.Amount <= 0)
                continue;

            if (!inventorySystem.AddItem(ingredient.Item, ingredient.Amount, out int addedAmount) ||
                addedAmount != ingredient.Amount)
            {
                Debug.LogError("[PERFORMANCE-VALIDATION][FAIL] Crafting ingredients could not be staged.");
                return false;
            }
        }

        bool crafted = craftingSystem.TryCraft(recipe, out string resultMessage);
        if (crafted)
            Debug.Log($"[PERFORMANCE-VALIDATION][PASS] Crafting transaction completed: {resultMessage}");
        else
            Debug.LogError($"[PERFORMANCE-VALIDATION][FAIL] Crafting transaction failed: {resultMessage}");

        return crafted;
    }

    // Captura sem alocacao dos marcadores selecionados do Unity Profiler.
    private static void StartRecorders()
    {
        mainThreadRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
        gcAllocationRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
        drawCallsRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count", 1);
        setPassRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count", 1);
    }

    private static void CaptureFrameSample()
    {
        deltaTimeTotal += Time.unscaledDeltaTime;

        long mainThreadValue = ReadNonNegativeValue(mainThreadRecorder);
        mainThreadNanosecondsTotal += mainThreadValue;
        mainThreadNanosecondsMax = Math.Max(mainThreadNanosecondsMax, mainThreadValue);

        long gcValue = ReadNonNegativeValue(gcAllocationRecorder);
        gcBytesTotal += gcValue;
        gcBytesMax = Math.Max(gcBytesMax, gcValue);

        drawCallsTotal += ReadNonNegativeValue(drawCallsRecorder);
        setPassTotal += ReadNonNegativeValue(setPassRecorder);
    }

    private static long ReadNonNegativeValue(ProfilerRecorder recorder)
    {
        return recorder.Valid ? Math.Max(0L, recorder.LastValue) : 0L;
    }

    // Consolidacao do resultado e limpeza garantida dos recorders/estado de sessao.
    private static void SaveResult()
    {
        double averageFrameMilliseconds = sampledFrames > 0
            ? deltaTimeTotal * 1000d / sampledFrames
            : 0d;
        double averageMainThreadMilliseconds = sampledFrames > 0
            ? mainThreadNanosecondsTotal / 1_000_000d / sampledFrames
            : 0d;
        double maximumMainThreadMilliseconds = mainThreadNanosecondsMax / 1_000_000d;
        double averageGcBytes = sampledFrames > 0 ? gcBytesTotal / (double)sampledFrames : 0d;
        double averageDrawCalls = sampledFrames > 0 ? drawCallsTotal / (double)sampledFrames : 0d;
        double averageSetPassCalls = sampledFrames > 0 ? setPassTotal / (double)sampledFrames : 0d;
        double averageWorkloadMilliseconds = workloadSamples > 0
            ? workloadTicksTotal * 1000d / System.Diagnostics.Stopwatch.Frequency / workloadSamples
            : 0d;
        double maximumWorkloadMilliseconds = workloadTicksMax * 1000d / System.Diagnostics.Stopwatch.Frequency;
        double averageWorkloadGcBytes = workloadSamples > 0
            ? workloadGcBytesTotal / (double)workloadSamples
            : 0d;

        string result = string.Join(";", new[]
        {
            $"frames={sampledFrames}",
            $"avgFrameMs={Format(averageFrameMilliseconds)}",
            $"avgMainThreadMs={Format(averageMainThreadMilliseconds)}",
            $"maxMainThreadMs={Format(maximumMainThreadMilliseconds)}",
            $"avgGcBytes={Format(averageGcBytes)}",
            $"maxGcBytes={gcBytesMax}",
            $"avgDrawCalls={Format(averageDrawCalls)}",
            $"avgSetPassCalls={Format(averageSetPassCalls)}",
            $"workloadSamples={workloadSamples}",
            $"successfulMutations={successfulInventoryMutations}",
            $"craftingTransactionPassed={craftingTransactionPassed}",
            $"avgWorkloadMs={Format(averageWorkloadMilliseconds)}",
            $"maxWorkloadMs={Format(maximumWorkloadMilliseconds)}",
            $"avgWorkloadGcBytes={Format(averageWorkloadGcBytes)}",
            $"maxWorkloadGcBytes={workloadGcBytesMax}"
        });

        SessionState.SetString(ResultKey, result);
    }

    private static string Format(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static void FinishProfiling()
    {
        EditorApplication.update -= UpdateProfiling;
        DisposeRecorders();

        string result = SessionState.GetString(ResultKey, "No result was produced.");
        Debug.Log($"[PERFORMANCE-VALIDATION][RESULT] {result}");

        SessionState.EraseBool(RequestKey);
        SessionState.EraseBool(StopKey);
        SessionState.EraseString(ResultKey);
        EditorApplication.Exit(result.StartsWith("frames=", StringComparison.Ordinal) ? 0 : 1);
    }

    private static void ResetCounters()
    {
        lastGameFrame = -1;
        observedFrames = 0;
        sampledFrames = 0;
        deltaTimeTotal = 0d;
        mainThreadNanosecondsTotal = 0L;
        mainThreadNanosecondsMax = 0L;
        gcBytesTotal = 0L;
        gcBytesMax = 0L;
        drawCallsTotal = 0L;
        setPassTotal = 0L;
        workloadTicksTotal = 0L;
        workloadTicksMax = 0L;
        workloadGcBytesTotal = 0L;
        workloadGcBytesMax = 0L;
        workloadSamples = 0;
        successfulInventoryMutations = 0;
        craftingTransactionPassed = false;
        inventorySystem = null;
        exerciseItem = null;
        craftingUI = null;
    }

    private static void DisposeRecorders()
    {
        if (mainThreadRecorder.Valid)
            mainThreadRecorder.Dispose();
        if (gcAllocationRecorder.Valid)
            gcAllocationRecorder.Dispose();
        if (drawCallsRecorder.Valid)
            drawCallsRecorder.Dispose();
        if (setPassRecorder.Valid)
            setPassRecorder.Dispose();
    }
}
