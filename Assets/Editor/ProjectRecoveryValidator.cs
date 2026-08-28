using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

[InitializeOnLoad]
public static class ProjectRecoveryValidator
{
    // Cena, limites e estado persistido entre Edit Mode e Play Mode.
    private const string DevelopmentScenePath = "Assets/Scenes/SampleScene.unity";
    private const string UrpAssetPath = "Assets/URP 2D.asset";
    private const string InputActionsPath = "Assets/PlayerInputActions.inputactions";
    private const string PlayRequestKey = "PassionProject.Validation.PlayRequest";
    private const string PlayStartedKey = "PassionProject.Validation.PlayStarted";
    private const string StopRequestedKey = "PassionProject.Validation.StopRequested";
    private const string RuntimeErrorCountKey = "PassionProject.Validation.RuntimeErrorCount";
    private const string RuntimeErrorsKey = "PassionProject.Validation.RuntimeErrors";
    private const string DeadlineKey = "PassionProject.Validation.DeadlineUtcTicks";
    private const int ValidationPlayFrames = 60;
    private const int ScreenshotFrame = 30;

    private static int playFrames;
    private static bool finishingPlayValidation;

    private static string ValidationScreenshotPath =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "RecoveryPlayMode.png"));

    static ProjectRecoveryValidator()
    {
        if (SessionState.GetBool(PlayRequestKey, false))
            EditorApplication.delayCall += ResumePlayModeValidation;
    }

    [MenuItem("Tools/Project Recovery/Validate Development Scene", priority = 110)]
    // Entradas manual e de linha de comando.
    private static void ValidateDevelopmentSceneFromMenu()
    {
        int failures = ValidateEditModeState();
        EditorUtility.DisplayDialog(
            "Project Recovery Validation",
            failures == 0
                ? "Validation completed successfully. See the Console for details."
                : $"Validation found {failures} failure(s). See the Console for details.",
            "OK");
    }

    public static void ValidateEditModeCommandLine()
    {
        EditorApplication.Exit(ValidateEditModeState() == 0 ? 0 : 1);
    }

    public static void ValidatePlayModeCommandLine()
    {
        // Execute immediately: GUI automation can otherwise wait indefinitely for
        // optional Unity Cloud project initialization before processing delayCall.
        BeginPlayModeValidation();
    }

    // Auditoria estatica de cena, prefabs, materiais e referencias serializadas.
    private static int ValidateEditModeState()
    {
        int failures = 0;
        Debug.Log("[RECOVERY-VALIDATION] Starting edit-mode validation.");

        SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(DevelopmentScenePath);
        Check(sceneAsset != null, "Development scene asset exists.", ref failures);
        if (sceneAsset == null)
            return failures;

        Scene scene = EditorSceneManager.OpenScene(DevelopmentScenePath, OpenSceneMode.Single);
        Check(scene.IsValid() && scene.isLoaded, "Development scene opens successfully.", ref failures);
        Check(scene.path == DevelopmentScenePath, "Development scene is the active scene.", ref failures);
        Check(scene.rootCount > 0, "Hierarchy contains root GameObjects.", ref failures);

        GameObject[] roots = scene.GetRootGameObjects();
        Transform[] transforms = roots.SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
        Dictionary<string, GameObject> objectsByName = transforms
            .GroupBy(transform => transform.name)
            .ToDictionary(group => group.Key, group => group.First().gameObject);

        Check(roots.Length >= 10, $"Recovered hierarchy has {roots.Length} root objects.", ref failures);
        Check(transforms.Length >= 100, $"Recovered hierarchy has {transforms.Length} GameObjects.", ref failures);

        string[] requiredObjects =
        {
            "Camera", "Canvas", "CinemachineCamera", "EventSystem", "Grid", "House",
            "InventorySystem", "Player", "Tilemap_Ground", "UIManager", "WorldInfoSystem"
        };

        foreach (string requiredObject in requiredObjects)
            Check(objectsByName.ContainsKey(requiredObject), $"Required GameObject '{requiredObject}' is present.", ref failures);

        int missingScripts = transforms.Sum(transform =>
            GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject));
        Check(missingScripts == 0, "Scene has no Missing Script components.", ref failures, missingScripts);

        int missingSerializedReferences = CountMissingSerializedReferences(transforms.SelectMany(
            transform => transform.GetComponents<Component>()));
        Check(missingSerializedReferences == 0, "Scene has no broken serialized object references.",
            ref failures, missingSerializedReferences);

        Camera[] cameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include);
        Check(cameras.Length > 0, "At least one Camera component is available.", ref failures);
        Check(cameras.Any(camera => camera.CompareTag("MainCamera")), "A MainCamera-tagged Camera is available.", ref failures);

        Canvas[] canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include);
        Check(canvases.Length > 0, "UI Canvas is available.", ref failures);
        Check(UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null, "EventSystem is available.", ref failures);

        Tilemap tilemap = UnityEngine.Object.FindAnyObjectByType<Tilemap>();
        Check(tilemap != null, "Tilemap component is available.", ref failures);
        if (tilemap != null)
            Check(tilemap.GetUsedTilesCount() > 0, "Tilemap contains recovered tiles.", ref failures);
        Check(UnityEngine.Object.FindAnyObjectByType<TilemapRenderer>() != null,
            "TilemapRenderer is available.", ref failures);

        if (objectsByName.TryGetValue("House", out GameObject house))
            Check(house.GetComponent<SpriteRenderer>()?.sprite != null, "House sprite is linked.", ref failures);

        if (objectsByName.TryGetValue("Player", out GameObject player))
            Check(player.GetComponent<SpriteRenderer>()?.sprite != null, "Player sprite is linked.", ref failures);

        Check(UnityEngine.Object.FindAnyObjectByType<CinemachineCamera>() != null,
            "Cinemachine camera component is available.", ref failures);

        UniversalRenderPipelineAsset expectedUrp =
            AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpAssetPath);
        Check(expectedUrp != null, "URP 2D pipeline asset exists.", ref failures);
        Check(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset,
            "Universal Render Pipeline is active.", ref failures);

        if (expectedUrp != null)
        {
            SerializedObject pipelineAsset = new(expectedUrp);
            SerializedProperty renderers = pipelineAsset.FindProperty("m_RendererDataList");
            bool hasRenderer2D = renderers != null && renderers.arraySize > 0 &&
                                 renderers.GetArrayElementAtIndex(0).objectReferenceValue != null;
            Check(hasRenderer2D, "URP asset has a linked 2D renderer.", ref failures);
        }

        InputActionAsset inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
        Check(inputActions != null, "Input System actions asset exists.", ref failures);
        Check(inputActions != null && inputActions.actionMaps.Count > 0,
            "Input System actions contain action maps.", ref failures);

        bool sceneEnabledForBuild = EditorBuildSettings.scenes.Any(buildScene =>
            buildScene.enabled && buildScene.path == DevelopmentScenePath);
        Check(sceneEnabledForBuild, "Development scene is enabled in Build Settings/Profiles.", ref failures);

        ValidatePrefabAssets(ref failures);
        ValidateMaterials(ref failures);

        Debug.Log($"[RECOVERY-VALIDATION] Edit-mode validation completed with {failures} failure(s).");
        return failures;
    }

    private static void ValidatePrefabAssets(ref int failures)
    {
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
        int missingScripts = 0;
        int missingReferences = 0;

        foreach (string prefabGuid in prefabGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(prefabGuid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                failures++;
                Debug.LogError($"[RECOVERY-VALIDATION][FAIL] Prefab could not be loaded: {path}");
                continue;
            }

            Transform[] transforms = prefab.GetComponentsInChildren<Transform>(true);
            missingScripts += transforms.Sum(transform =>
                GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject));
            missingReferences += CountMissingSerializedReferences(transforms.SelectMany(
                transform => transform.GetComponents<Component>()));
        }

        Check(prefabGuids.Length > 0, $"Project contains {prefabGuids.Length} prefab assets.", ref failures);
        Check(missingScripts == 0, "Prefab assets have no Missing Script components.", ref failures, missingScripts);
        Check(missingReferences == 0, "Prefab assets have no broken serialized object references.",
            ref failures, missingReferences);
    }

    private static void ValidateMaterials(ref int failures)
    {
        string[] materialGuids = AssetDatabase.FindAssets("t:Material", new[] { "Assets" });
        int missingShaders = 0;

        foreach (string materialGuid in materialGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(materialGuid);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null || material.shader == null || material.shader.name == "Hidden/InternalErrorShader")
                missingShaders++;
        }

        Check(missingShaders == 0, "Materials have valid shaders.", ref failures, missingShaders);
    }

    private static int CountMissingSerializedReferences(IEnumerable<Component> components)
    {
        int missingReferences = 0;

        foreach (Component component in components)
        {
            if (component == null)
                continue;

            SerializedObject serializedObject = new(component);
            SerializedProperty property = serializedObject.GetIterator();
            bool visitChildren = true;

            while (property.NextVisible(visitChildren))
            {
                visitChildren = false;
                if (property.propertyType == SerializedPropertyType.ObjectReference &&
                    property.objectReferenceValue == null &&
                    property.objectReferenceEntityIdValue.IsValid())
                {
                    missingReferences++;
                }
            }
        }

        return missingReferences;
    }

    // Sessao de Play Mode com captura de erros e timeout para evitar CI bloqueado.
    private static void BeginPlayModeValidation()
    {
        int editModeFailures = ValidateEditModeState();
        if (editModeFailures != 0)
        {
            EditorApplication.Exit(1);
            return;
        }

        SessionState.SetBool(PlayRequestKey, true);
        SessionState.SetBool(PlayStartedKey, true);
        SessionState.SetBool(StopRequestedKey, false);
        SessionState.SetInt(RuntimeErrorCountKey, 0);
        SessionState.SetString(RuntimeErrorsKey, string.Empty);
        SessionState.SetString(DeadlineKey,
            DateTime.UtcNow.AddMinutes(3).Ticks.ToString(CultureInfo.InvariantCulture));

        if (File.Exists(ValidationScreenshotPath))
            File.Delete(ValidationScreenshotPath);

        playFrames = 0;
        HookPlayModeValidation();
        Debug.Log("[RECOVERY-VALIDATION] Entering Play Mode.");
        EditorApplication.isPlaying = true;
    }

    private static void ResumePlayModeValidation()
    {
        if (SessionState.GetBool(PlayRequestKey, false))
            HookPlayModeValidation();
    }

    private static void HookPlayModeValidation()
    {
        Application.logMessageReceived -= CaptureRuntimeError;
        Application.logMessageReceived += CaptureRuntimeError;
        EditorApplication.update -= UpdatePlayModeValidation;
        EditorApplication.update += UpdatePlayModeValidation;
    }

    private static void CaptureRuntimeError(string condition, string stackTrace, LogType type)
    {
        if (!EditorApplication.isPlaying ||
            (type != LogType.Error && type != LogType.Assert && type != LogType.Exception))
        {
            return;
        }

        int errorCount = SessionState.GetInt(RuntimeErrorCountKey, 0) + 1;
        SessionState.SetInt(RuntimeErrorCountKey, errorCount);

        string errors = SessionState.GetString(RuntimeErrorsKey, string.Empty);
        if (errors.Length < 8000)
            SessionState.SetString(RuntimeErrorsKey, errors + "\n" + condition + "\n" + stackTrace);
    }

    private static void UpdatePlayModeValidation()
    {
        if (!SessionState.GetBool(PlayRequestKey, false) || finishingPlayValidation)
            return;

        if (HasPlayModeValidationTimedOut())
        {
            SessionState.SetInt(RuntimeErrorCountKey,
                SessionState.GetInt(RuntimeErrorCountKey, 0) + 1);
            SessionState.SetString(RuntimeErrorsKey,
                SessionState.GetString(RuntimeErrorsKey, string.Empty) + "\nPlay Mode validation timed out.");
            SessionState.SetBool(StopRequestedKey, true);
            if (EditorApplication.isPlaying)
                EditorApplication.isPlaying = false;
        }

        if (EditorApplication.isPlaying)
        {
            playFrames++;
            if (playFrames == ScreenshotFrame)
                ScreenCapture.CaptureScreenshot(ValidationScreenshotPath);

            if (playFrames >= ValidationPlayFrames && !SessionState.GetBool(StopRequestedKey, false))
            {
                SessionState.SetBool(StopRequestedKey, true);
                EditorApplication.isPlaying = false;
            }

            return;
        }

        if (SessionState.GetBool(PlayStartedKey, false) &&
            SessionState.GetBool(StopRequestedKey, false) &&
            !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            FinishPlayModeValidation();
        }
    }

    private static bool HasPlayModeValidationTimedOut()
    {
        string deadlineValue = SessionState.GetString(DeadlineKey, string.Empty);
        return long.TryParse(deadlineValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out long deadline) &&
               DateTime.UtcNow.Ticks > deadline;
    }

    // Finalizacao idempotente, restaurando handlers e estado do Editor.
    private static void FinishPlayModeValidation()
    {
        finishingPlayValidation = true;
        Application.logMessageReceived -= CaptureRuntimeError;
        EditorApplication.update -= UpdatePlayModeValidation;

        int runtimeErrors = SessionState.GetInt(RuntimeErrorCountKey, 0);
        string errorDetails = SessionState.GetString(RuntimeErrorsKey, string.Empty);

        bool screenshotCreated = File.Exists(ValidationScreenshotPath) &&
                                 new FileInfo(ValidationScreenshotPath).Length > 0;
        if (screenshotCreated)
            Debug.Log($"[RECOVERY-VALIDATION][PASS] Game View screenshot captured at '{ValidationScreenshotPath}'.");
        else
        {
            runtimeErrors++;
            errorDetails += "\nGame View validation screenshot was not created.";
        }

        SessionState.EraseBool(PlayRequestKey);
        SessionState.EraseBool(PlayStartedKey);
        SessionState.EraseBool(StopRequestedKey);
        SessionState.EraseInt(RuntimeErrorCountKey);
        SessionState.EraseString(RuntimeErrorsKey);
        SessionState.EraseString(DeadlineKey);

        if (runtimeErrors == 0)
            Debug.Log("[RECOVERY-VALIDATION][PASS] Play Mode completed without runtime errors.");
        else
            Debug.LogError($"[RECOVERY-VALIDATION][FAIL] Play Mode produced {runtimeErrors} runtime error(s):{errorDetails}");

        EditorApplication.Exit(runtimeErrors == 0 ? 0 : 1);
    }

    private static void Check(bool condition, string message, ref int failures, int failureCount = 1)
    {
        if (condition)
        {
            Debug.Log($"[RECOVERY-VALIDATION][PASS] {message}");
            return;
        }

        failures += Math.Max(1, failureCount);
        Debug.LogError($"[RECOVERY-VALIDATION][FAIL] {message}");
    }
}
