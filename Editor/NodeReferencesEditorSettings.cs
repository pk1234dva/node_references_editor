using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace NodeReferencesEditor
{
    [FilePath("ProjectSettings/NodeReferencesSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal class NodeReferencesEditorSettings : ScriptableSingleton<NodeReferencesEditorSettings>
    {
        public string inheritanceRequirement = "MonoBehaviour";
        public string referencesTypeFilter = "State";
        public string componentsTypeFilter = "State";
        public bool lockGameObject = true;
        public bool useRecursiveSearch = false;

        [Range(0, 5)] public int recursionDepth = 1;
        public bool drawPODs = false;
        public bool useList = false;

        public void Save()
        {
            Save(true);
        }
    }

    internal class NodeReferencesEditorSettingsProvider : SettingsProvider
    {
        private SerializedObject serializedObject;
        //
        SerializedProperty inheritanceRequirement;
        SerializedProperty referencesTypeFilter;
        SerializedProperty componentsTypeFilter;
        SerializedProperty lockGameObject;
        SerializedProperty useRecursiveSearch;
        SerializedProperty recursionDepth;
        SerializedProperty drawPODs;
        SerializedProperty useList;

        public override void OnGUI(string searchContext)
        {
            if (serializedObject == null)
            {
                NodeReferencesEditorSettings.instance.hideFlags = HideFlags.DontSave;
                serializedObject = new SerializedObject(NodeReferencesEditorSettings.instance);

                lockGameObject = serializedObject.FindProperty(nameof(NodeReferencesEditorSettings.instance.lockGameObject));

                inheritanceRequirement = serializedObject.FindProperty(nameof(NodeReferencesEditorSettings.instance.inheritanceRequirement));
                referencesTypeFilter = serializedObject.FindProperty(nameof(NodeReferencesEditorSettings.instance.referencesTypeFilter));
                componentsTypeFilter = serializedObject.FindProperty(nameof(NodeReferencesEditorSettings.instance.componentsTypeFilter));

                useRecursiveSearch = serializedObject.FindProperty(nameof(NodeReferencesEditorSettings.instance.useRecursiveSearch));
                recursionDepth = serializedObject.FindProperty(nameof(NodeReferencesEditorSettings.instance.recursionDepth));
                drawPODs = serializedObject.FindProperty(nameof(NodeReferencesEditorSettings.instance.drawPODs));
                useList = serializedObject.FindProperty(nameof(NodeReferencesEditorSettings.instance.useList));
            }

            EditorGUILayout.LabelField("Default nodes references editor settings", EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(lockGameObject, new GUIContent("Lock selected game object"));

            EditorGUILayout.PropertyField(inheritanceRequirement, new GUIContent("Type name for inheritance requirement"));
            EditorGUILayout.PropertyField(componentsTypeFilter, new GUIContent("Type name filter on components"));
            EditorGUILayout.PropertyField(referencesTypeFilter, new GUIContent("Type name filter on reference fields"));

            EditorGUILayout.PropertyField(useRecursiveSearch, new GUIContent("Search children recursively"));
            EditorGUILayout.PropertyField(recursionDepth, new GUIContent("Recursion depth when show children"));
            EditorGUILayout.PropertyField(drawPODs, new GUIContent("Show floats and ints"));
            EditorGUILayout.PropertyField(useList, new GUIContent("Use list"));

            if (serializedObject.hasModifiedProperties)
            {
                serializedObject.ApplyModifiedProperties();
                NodeReferencesEditorSettings.instance.Save();
            }
        }

        [SettingsProvider]
        public static SettingsProvider CreateNodeReferencesEditorSettingsProvider()
        {
            // NodeReferencesEditorSettings.instance.Empty();
            NodeReferencesEditorSettings.instance.Save();
            return new NodeReferencesEditorSettingsProvider("Project/Node references editor", SettingsScope.Project);
        }

        public NodeReferencesEditorSettingsProvider(string path, SettingsScope scopes, IEnumerable<string> keywords = null) : base(path, scopes, keywords) { }
    }
}