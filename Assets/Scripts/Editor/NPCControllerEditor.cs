using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(NPCController))]
public class NPCControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        NPCController controller = (NPCController)target;

        GUILayout.Space(10);
        if (GUILayout.Button("Send Test Text", GUILayout.Height(40)))
        {
            if (Application.isPlaying)
            {
                controller.SendTestText();
            }
            else
            {
                Debug.LogWarning("You must be in Play Mode to test the NPC.");
            }
        }
    }
}
