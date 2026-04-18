using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PauseMenuUI))]
public class PauseMenuUIEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.HelpBox(
            "Use os botoes abaixo para reconstruir o layout e alternar o preview entre o menu principal e a aba de opcoes diretamente no editor.",
            MessageType.Info);

        DrawPreviewButtons();
        EditorGUILayout.Space();
        DrawDefaultInspector();

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawPreviewButtons()
    {
        PauseMenuUI pauseMenuUI = (PauseMenuUI)target;

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Reconstruir + Main"))
                pauseMenuUI.BuildLayoutForEditorPreview(showOptionsPanel: false);

            if (GUILayout.Button("Preview Opcoes"))
                pauseMenuUI.BuildLayoutForEditorPreview(showOptionsPanel: true);
        }

        if (GUILayout.Button("Esconder Preview"))
            pauseMenuUI.HideLayoutPreviewInEditor();
    }
}
