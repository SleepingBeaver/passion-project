using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class RecoverEmptySceneOnLoad
{
    private const string DevelopmentScenePath = "Assets/Scenes/SampleScene.unity";
    private const string SessionCheckKey = "PassionProject.RecoverEmptySceneOnLoad.Checked";

    static RecoverEmptySceneOnLoad()
    {
        EditorApplication.delayCall += RecoverDevelopmentSceneWhenStartupSceneIsEmpty;
    }

    [MenuItem("Tools/Project Recovery/Open Development Scene", priority = 100)]
    private static void OpenDevelopmentSceneFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        OpenDevelopmentScene();
    }

    [MenuItem("Tools/Project Recovery/Restore Essential Editor Windows", priority = 101)]
    private static void RestoreEssentialEditorWindows()
    {
        OpenEditorWindow("Window/General/Scene");
        OpenEditorWindow("Window/General/Game");
        OpenEditorWindow("Window/General/Hierarchy");
        OpenEditorWindow("Window/General/Project");
        OpenEditorWindow("Window/General/Inspector");
        OpenEditorWindow("Window/General/Console");
    }

    public static void RestoreEssentialEditorWindowsCommandLine()
    {
        RestoreEssentialEditorWindows();
        EditorApplication.Exit(0);
    }

    private static void RecoverDevelopmentSceneWhenStartupSceneIsEmpty()
    {
        if (Application.isBatchMode ||
            EditorApplication.isPlayingOrWillChangePlaymode ||
            SessionState.GetBool(SessionCheckKey, false))
        {
            return;
        }

        SessionState.SetBool(SessionCheckKey, true);

        if (EditorSceneManager.sceneCount != 1)
            return;

        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid() || activeScene.path == DevelopmentScenePath)
            return;

        bool discardableUntitledScene = string.IsNullOrEmpty(activeScene.path) && !activeScene.isDirty;
        bool emptySavedScene = !string.IsNullOrEmpty(activeScene.path) &&
                               activeScene.rootCount == 0 &&
                               !activeScene.isDirty;

        if (!discardableUntitledScene && !emptySavedScene)
            return;

        if (OpenDevelopmentScene())
            OpenEditorWindow("Window/General/Hierarchy");
    }

    private static bool OpenDevelopmentScene()
    {
        SceneAsset developmentScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(DevelopmentScenePath);
        if (developmentScene == null)
        {
            Debug.LogError($"Development scene was not found at '{DevelopmentScenePath}'.");
            return false;
        }

        EditorSceneManager.OpenScene(DevelopmentScenePath, OpenSceneMode.Single);
        return true;
    }

    private static void OpenEditorWindow(string menuPath)
    {
        if (!EditorApplication.ExecuteMenuItem(menuPath))
            Debug.LogWarning($"Could not open the editor window from menu item '{menuPath}'.");
    }
}
