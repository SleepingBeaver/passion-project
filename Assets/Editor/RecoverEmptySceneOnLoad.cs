using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class RecoverEmptySceneOnLoad
{
    // Executa uma vez por sessao e nunca substitui uma cena modificada pelo usuario.
    private const string DevelopmentScenePath = "Assets/Scenes/SampleScene.unity";
    private const string SessionCheckKey = "PassionProject.RecoverEmptySceneOnLoad.Checked";

    // Inicializacao adiada ate o Editor concluir a restauracao da sessao.
    static RecoverEmptySceneOnLoad()
    {
        EditorApplication.delayCall += RecoverDevelopmentSceneWhenStartupSceneIsEmpty;
    }

    // Comandos manuais para recuperar a cena e as janelas essenciais.
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

    // Recuperacao conservadora: aceita somente uma cena vazia, limpa e descartavel.
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

    // Helpers centralizam a abertura da cena e dos paineis do Editor.
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
