using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace NodeReferencesEditor
{
    public struct TreeNode
    {
        public string gameObjectName;
        public Vector2 textDimensionWidth;
        public int index;
        public int childCount;

        public Color color;

        public int componentsStartingIndex;
        public int componentsEndingIndex;

        // Updated each frame
        //public Rect boundingRect;
        public Bbox bbox;

        public bool draw;
    }

    public struct Bbox
    {
        public float left;
        public float top;
        public float right;
        public float bot;

        public Bbox(float left, float top, float right, float bot)
        {
            this.left = left;
            this.top = top;
            this.right = right;
            this.bot = bot;
        }
    }

    public class NodeReferencesEditor : EditorWindow
    {
        // Modify if there's an issue with text not being visible (kept low to avoid clutter)
        public const int windowWidthDefault = 130;
        public const int inputFieldsWidth = 45;
        //
        public float centerOffset = 200.0f;
        public const float marginPerObject = 15.0f;
        public const float hueJump = 0.33f;
        public const int perFieldHeight = 19;
        public const int perFieldOffset = 19 + 7;
        public const int leftPodOffset = 10;
        private Bbox fullBbox = new Bbox(9999, 9999, -99999, -99999);

        // private fields that hold references
        List<TreeNode> tree;
        //
        SerializedObject[] serializedStates;
        string[] serializedStatesNames;
        Rect[] windows;
        //
        List<SerializedProperty>[] fields;
        List<Rect>[] fieldsBoxes;
        List<string>[] fieldsNames;
        // pods
        List<SerializedProperty>[] podFields;
        List<string>[] podFieldsNames;
        List<Rect>[] podFieldsBoxes;
        //
        List<(int, int, int)> references;
        List<(int, int)> nullReferences;
        List<(int, int)> externalReferences;

        //
        GUIStyle labelStyle;
        GUIStyle boxStyle;
        GUIStyle windowStyle;
        GUIStyle numberFieldStyle;
        //
        GUIStyle inputOptionStyle;
        GUIStyle toggleOptionStyle;
        //
        GUIStyle gameNameStyle;
        private Texture2D texture;

        // fields with "state" based values
        public GameObject activeGameObject;

        bool lockGameObject = true;
        bool drawOptions = true;
        private System.Type nodeType = typeof(MonoBehaviour);
        private string inheritanceRequirement = "MonoBehaviour";
        private string _inheritanceRequirement = "MONOBEHAVIOUR";

        string referencesTypeFilter = "State";
        string _referencesTypeFilter = "STATE";
        //
        string componentsTypeFilter = "State";
        string _componentsTypeFilter = "STATE";
        //
        bool useRecursiveSearch = false;
        bool drawPODs = false;
        bool drawDragCurve = false;
        bool useList = false;
        //
        Vector2 startPos;
        Rect startRect;
        //
        (int, int) startFieldIndices;
        int endWindowIndex;
        int hoveringWindowIndex;

        // Recursion
        int recursionDepth = 1;

        // List related
        int listElementsCount = 0;
        UnityEngine.GameObject[] gameObjects;

        // init
        bool stylesInitialized = false;

        void LoadSettings()
        {
            NodeReferencesEditorSettings instance = NodeReferencesEditorSettings.instance;

            inheritanceRequirement = instance.inheritanceRequirement;
            _inheritanceRequirement = inheritanceRequirement.ToUpper();
            UpdateInheritanceType();

            componentsTypeFilter = instance.componentsTypeFilter;
            _componentsTypeFilter = componentsTypeFilter.ToUpper();

            referencesTypeFilter = instance.referencesTypeFilter;
            _referencesTypeFilter = referencesTypeFilter.ToUpper();

            lockGameObject = instance.lockGameObject;

            useRecursiveSearch = instance.useRecursiveSearch;
            recursionDepth = Mathf.Min(10, Mathf.Max(0, instance.recursionDepth));

            drawPODs = instance.drawPODs;
            useList = instance.useList;
        }

        // Unity functions
        [MenuItem("GameObject/Custom node editor", false, 13)]
        static void ShowEditor()
        {
            if (Selection.activeGameObject == null)
            {
                Debug.LogWarning("No selected object - cannot use node reference editor");
                return;
            }

            NodeReferencesEditor editor = EditorWindow.GetWindow<NodeReferencesEditor>();
            editor.LoadSettings();
            editor.activeGameObject = Selection.activeGameObject;
            editor.UpdateStates();
            editor.wantsMouseMove = true;
        }

        [MenuItem("CONTEXT/Transform/Custom node editor", false, 1011)]
        static void ShowEditorAlt(MenuCommand command)
        {
            if (Selection.activeGameObject == null)
            {
                Debug.LogWarning("No selected object - cannot use node reference editor");
                return;
            }

            Transform t = (Transform)command.context;
            if (t == null)
            {
                Debug.LogWarning("Failed getting Transform component");
                return;
            }

            NodeReferencesEditor editor = EditorWindow.GetWindow<NodeReferencesEditor>();
            editor.LoadSettings();
            editor.activeGameObject = t.gameObject;
            editor.UpdateStates();
            editor.wantsMouseMove = true;
        }

        void OnSelectionChange()
        {
            if (!lockGameObject && !useList)
            {
                if (activeGameObject != Selection.activeGameObject && Selection.activeGameObject != null)
                {
                    activeGameObject = Selection.activeGameObject;
                    UpdateStates();
                    Repaint();
                }
            }
        }

        void OnGUI()
        {
            // UpdateStyles(); // if anything is wrong, this might help. Very laggy though.

            if (!stylesInitialized)
            {
                InitStyles();
                stylesInitialized = true;
            }

            if (useRecursiveSearch)
            {
                UpdateNodeBbox(0);
                DrawParentRelations();
            }

            Event e = Event.current;

            DragEvents(e);

            BeginWindows();
            for (int i = 0; i < serializedStates.Length; i++)
                windows[i] = GUI.Window(i, windows[i], DrawNodeWindow, serializedStatesNames[i], windowStyle);
            EndWindows();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Toggle options")) drawOptions = !drawOptions;
            lockGameObject = GUILayout.Toggle(lockGameObject, "Lock game object");
            EditorGUILayout.EndHorizontal();

            if (drawOptions)
            {
                #region OPTIONS0
                EditorGUILayout.BeginVertical(inputOptionStyle, GUILayout.MaxWidth(350.0f));

                EditorGUILayout.BeginHorizontal();
                //
                bool oldUseList = useList;
                useList = GUILayout.Toggle(useList, "Use objects from list");
                if (useList != oldUseList) UpdateStates();

                if (useList)
                {
                    EditorGUILayout.BeginVertical();

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("List size", labelStyle);
                    listElementsCount = EditorGUILayout.IntField(listElementsCount);
                    EditorGUILayout.EndHorizontal();

                    bool somethingChanged = false;
                    if (gameObjects == null || gameObjects.Length < listElementsCount)
                    {
                        somethingChanged = true;
                        gameObjects = new GameObject[listElementsCount * 4];
                    }

                    for (int i = 0; i < listElementsCount; i++)
                    {
                        GameObject oldGo = gameObjects[i];
                        gameObjects[i] = EditorGUILayout.ObjectField("Object " + i.ToString(), gameObjects[i], typeof(GameObject), true) as GameObject;
                        if (oldGo != gameObjects[i]) somethingChanged = true;
                    }
                    EditorGUILayout.EndVertical();


                    if (somethingChanged) UpdateStates();
                }
                //
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.EndVertical();
                #endregion

                #region OPTIONS1
                EditorGUILayout.BeginVertical(inputOptionStyle, GUILayout.MaxWidth(350.0f));

                string newStringInheritance = EditorGUILayout.TextField("Component inherits from", inheritanceRequirement);
                if (inheritanceRequirement != newStringInheritance)
                {
                    Debug.Log("Component inheritance requirement string changed");
                    inheritanceRequirement = newStringInheritance;
                    _inheritanceRequirement = newStringInheritance.ToUpper();
                    UpdateInheritanceType();
                    UpdateStates();
                }

                string newStringComponent = EditorGUILayout.TextField("Component filter", componentsTypeFilter);
                if (componentsTypeFilter != newStringComponent)
                {
                    Debug.Log("Component string changed");
                    componentsTypeFilter = newStringComponent;
                    _componentsTypeFilter = newStringComponent.ToUpper();
                    UpdateStates();
                }

                string newStringField = EditorGUILayout.TextField("Reference filter", referencesTypeFilter);
                if (referencesTypeFilter != newStringField)
                {
                    Debug.Log("Filter string changed");
                    referencesTypeFilter = newStringField;
                    _referencesTypeFilter = newStringField.ToUpper();
                    UpdateStates();
                }

                EditorGUILayout.EndVertical();
                #endregion

                #region OPTIONS2
                EditorGUILayout.BeginVertical(inputOptionStyle, GUILayout.MaxWidth(100.0f));

                bool oldUseRecursiveSearch = useRecursiveSearch;
                useRecursiveSearch = GUILayout.Toggle(useRecursiveSearch, "Search transform children");
                if (useRecursiveSearch != oldUseRecursiveSearch) UpdateStates();

                if (useRecursiveSearch)
                {
                    int oldDepth = recursionDepth;
                    recursionDepth = EditorGUILayout.IntField("Recursion depth ", recursionDepth);
                    if (recursionDepth < 0) recursionDepth = 0;

                    if (oldDepth != recursionDepth) UpdateStates();
                }

                bool oldDrawPODsBool = drawPODs;
                drawPODs = GUILayout.Toggle(drawPODs, "Show value type fields");
                if (drawPODs != oldDrawPODsBool) UpdateStates();

                EditorGUILayout.EndVertical();
                #endregion
            }

            DrawReferences();

            if (drawDragCurve)
            {
                DrawRect(startRect, Color.green);
                DrawNodeCurve(startPos, e.mousePosition);
                // Check if currently hovering over a field
                if (FindContainingWindow(e.mousePosition, ref hoveringWindowIndex) && (hoveringWindowIndex != startFieldIndices.Item1))
                    DrawRect(windows[hoveringWindowIndex], Color.blue);
                Repaint();
            }
        }

        // Init
        private void InitStyles()
        {
            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.label);
                labelStyle.normal.textColor = Color.black;
                labelStyle.alignment = TextAnchor.UpperRight;
                labelStyle.fontStyle = FontStyle.Bold;
            }
            if (boxStyle == null)
            {
                boxStyle = new GUIStyle(GUI.skin.box);
                boxStyle.normal.textColor = Color.black;
                //boxStyle.alignment = TextAnchor.UpperRight;
            }
            if (windowStyle == null)
            {
                windowStyle = new GUIStyle(GUI.skin.window);
                windowStyle.fontStyle = FontStyle.Bold;
            }
            if (numberFieldStyle == null)
            {
                numberFieldStyle = new GUIStyle(EditorStyles.numberField);
                numberFieldStyle.alignment = TextAnchor.MiddleCenter;
                numberFieldStyle.fixedWidth = inputFieldsWidth;
            }

            if (inputOptionStyle == null)
            {
                inputOptionStyle = new GUIStyle(EditorStyles.label);
                SetGUIStyleBackground(inputOptionStyle, new Color(0.5f, 0.5f, 0.0f, 0.5f));
            }

            if (toggleOptionStyle == null)
            {
                toggleOptionStyle = new GUIStyle(EditorStyles.label);
                SetGUIStyleBackground(toggleOptionStyle, new Color(0.0f, 1.0f, 0.0f, 0.5f));
            }

            if (gameNameStyle == null)
            {
                gameNameStyle = new GUIStyle(GUI.skin.label);
                gameNameStyle.normal.textColor = Color.black;
                gameNameStyle.alignment = TextAnchor.UpperLeft;
                gameNameStyle.fontStyle = FontStyle.Bold;
            }
        }

        // Update
        private void UpdateInheritanceType()
        {
            nodeType = typeof(MonoBehaviour);
            //
            int foundCount = 0;
            System.Type foundType = null;
            foreach (System.Reflection.Assembly a in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                System.Type[] assemblyTypes = a.GetTypes();
                for (int j = 0; j < assemblyTypes.Length; j++)
                {
                    if (assemblyTypes[j].Name.ToUpper() == _inheritanceRequirement)
                    {
                        foundCount += 1;
                        if (foundCount > 1) break;
                        foundType = assemblyTypes[j];
                    }
                }
            }
            if (foundCount != 1)
            {
                if (foundCount == 0)
                    Debug.LogError("Failed finding given type, defaulting to MonoBehaviour");
                if (foundCount > 1)
                    Debug.LogError("Found too many types with given name, defaulting to Monobehaviour");
            }
            else
            {
                nodeType = foundType;
            }
        }
        public void UpdateStates()
        {
            List<Component> components = new List<Component>();
            if (useList)
            {
                // Avoid repeating sets
                HashSet<Transform> tmpSet = new HashSet<Transform>();
                for (int i = 0; i < listElementsCount; i++)
                {
                    if (gameObjects[i] != null) tmpSet.Add(gameObjects[i].transform);
                }

                // Insert
                Transform[] rootTransform = new Transform[tmpSet.Count];
                int idx = 0;
                foreach (Transform t in tmpSet)
                {
                    rootTransform[idx] = t;
                    idx += 1;
                }

                //
                tree = new List<TreeNode>();
                Recursion(rootTransform, tree, components, recursionDepth);
            }
            else
            {
                GameObject currentGameObject = activeGameObject;
                if (useRecursiveSearch)
                {
                    Transform[] rootTransform = { currentGameObject.transform };
                    tree = new List<TreeNode>();

                    Recursion(rootTransform, tree, components, recursionDepth);
                }
                else
                {
                    Component[] temp = currentGameObject.GetComponents(nodeType);

                    // Filter
                    for (int i = 0; i < temp.Length; i++)
                    {
                        if (temp[i].GetType().Name.ToUpper().Contains(_componentsTypeFilter)) components.Add(temp[i]);
                    }
                }
            }

            // update all found states
            UpdateStatesFromSerializedObjects(components.ToArray());
        }
        public void Recursion(Transform[] t, List<TreeNode> tree, List<Component> components, int maxDepthToUse)
        {
            // Create stack for breadth-first recursion
            Stack<(Transform currentTransform, int depth, Color color)> stack = new Stack<(Transform, int, Color)>();

            Color baseColor = Color.red;
            if (t.Length > 1)
            {
                TreeNode node = new TreeNode();
                node.index = 0;
                node.color = Color.white;
                node.childCount = t.Length;
                node.gameObjectName = "All objects in list";
                node.textDimensionWidth = GUI.skin.label.CalcSize(new GUIContent(node.gameObjectName));
                tree.Add(node);

                for (int i = 0; i < t.Length; i++)
                {
                    stack.Push((t[i], 1, baseColor));
                    baseColor = UpdateHue(baseColor, 1);
                }
            }
            else
            {
                for (int i = 0; i < t.Length; i++)
                {
                    stack.Push((t[i], 0, baseColor));
                    baseColor = UpdateHue(baseColor, 0);
                }
            }

            while (stack.Count != 0)
            {
                // Pop
                var element = stack.Pop();
                TreeNode node = new TreeNode();

                // Set tree node values
                node.index = tree.Count;
                node.color = element.color;
                node.childCount = element.currentTransform.childCount;
                node.gameObjectName = element.currentTransform.name;
                node.textDimensionWidth = GUI.skin.label.CalcSize(new GUIContent(node.gameObjectName));

                // Find all components, add them, set references
                node.componentsStartingIndex = components.Count;
                Component[] temp = element.currentTransform.GetComponents(nodeType);
                for (int i = 0; i < temp.Length; i++)
                    if (temp[i].GetType().Name.ToUpper().Contains(_componentsTypeFilter)) components.Add(temp[i]);
                node.componentsEndingIndex = components.Count;

                // Add tree node
                if (element.depth >= maxDepthToUse) node.childCount = 0;
                tree.Add(node);

                //
                if (element.depth >= maxDepthToUse) continue;

                Color childColor = LowerAlpha(element.color);
                for (int i = 0; i < node.childCount; i++)
                {
                    childColor = UpdateHue(childColor, element.depth + 1);
                    Transform childTransform = element.currentTransform.GetChild(i);
                    //
                    stack.Push((childTransform, element.depth + 1, childColor));
                }
            }
        }
        public void UpdateStatesFromSerializedObjects(Component[] states)
        {
            // init arrays
            serializedStates = new SerializedObject[states.Length];
            serializedStatesNames = new string[states.Length];
            //
            fields = new List<SerializedProperty>[states.Length];
            fieldsNames = new List<string>[states.Length];
            fieldsBoxes = new List<Rect>[states.Length];
            //
            if (drawPODs)
            {
                podFields = new List<SerializedProperty>[states.Length];
                podFieldsNames = new List<string>[states.Length];
                podFieldsBoxes = new List<Rect>[states.Length];
            }
            //
            windows = new Rect[states.Length];

            // windows will be drawn in circle, determine "center" using perimeter, offset by a small amount
            float windowHalf = windowWidthDefault / (float)2;
            float perimeter = (states.Length + 3) * windowWidthDefault;
            float radius = perimeter / (Mathf.PI * 2.0f);
            float center = radius + windowHalf + centerOffset;

            // iterate across states
            for (int i = 0; i < states.Length; i++)
            {
                serializedStates[i] = new SerializedObject(states[i]);
                fields[i] = new List<SerializedProperty>();
                fieldsNames[i] = new List<string>();
                fieldsBoxes[i] = new List<Rect>();

                if (drawPODs)
                {
                    podFields[i] = new List<SerializedProperty>();
                    podFieldsNames[i] = new List<string>();
                    podFieldsBoxes[i] = new List<Rect>();
                }

                // insert all props
                var serializedProperty = serializedStates[i].GetIterator();
                serializedProperty.NextVisible(true);
                // Rest of scan stays at same level
                do
                {
                    if (serializedProperty.propertyType == SerializedPropertyType.ObjectReference && serializedProperty.type.ToUpper().Contains(_referencesTypeFilter))
                    {
                        fields[i].Add(serializedProperty.Copy());
                        fieldsNames[i].Add(serializedProperty.displayName);
                    }
                    if (drawPODs)
                    {
                        if (serializedProperty.propertyType == SerializedPropertyType.Float ||
                            serializedProperty.propertyType == SerializedPropertyType.Integer)
                        {
                            podFields[i].Add(serializedProperty.Copy());
                            podFieldsNames[i].Add(serializedProperty.displayName);
                        }
                    }
                }
                while (serializedProperty.NextVisible(false));

                // determine window sizes and positions
                float ratio = i / (float)states.Length;
                ratio = ratio + (3.0f / 8.0f);
                float ratioInRadians = Mathf.PI * 2.0f * ratio;

                float cos = Mathf.Cos(ratioInRadians);
                float sin = Mathf.Sin(ratioInRadians);

                float column = center + radius * cos - windowHalf;
                float row = center + radius * sin - windowHalf;

                int fieldsTotalCount = fields[i].Count;
                if (drawPODs) fieldsTotalCount += podFields[i].Count;

                windows[i] = new Rect(column, row, windowWidthDefault, (fieldsTotalCount + 1) * perFieldOffset);
                serializedStatesNames[i] = serializedStates[i].targetObject.GetType().ToString();

                int offset = perFieldOffset;
                for (int j = 0; j < fields[i].Count; j++)
                {
                    fieldsBoxes[i].Add(new Rect(7, offset, windowWidthDefault - 7 - 7, perFieldHeight));
                    offset += perFieldOffset;
                }
                if (drawPODs)
                {
                    for (int j = 0; j < podFields[i].Count; j++)
                    {
                        podFieldsBoxes[i].Add(new Rect(leftPodOffset, offset, windowWidthDefault, perFieldHeight));
                        offset += perFieldOffset;
                    }
                }
            }

            StoreComponentReferences();
        }
        private void StoreComponentReferences()
        {
            if (references == null) references = new List<(int, int, int)>();
            if (nullReferences == null) nullReferences = new List<(int, int)>();
            if (externalReferences == null) externalReferences = new List<(int, int)>();
            references.Clear();
            nullReferences.Clear();
            externalReferences.Clear();
            for (int i = 0; i < serializedStates.Length; i++)
            {
                for (int j = 0; j < fields[i].Count; j++)
                {
                    UnityEngine.Object currentFieldReference = fields[i][j].objectReferenceValue;

                    int foundIdx = -1;
                    for (int k = 0; k < serializedStates.Length; k++)
                    {
                        // skip if reference is to this component itself
                        if (i == k) continue;
                        UnityEngine.Object currentObj = serializedStates[k].targetObject;
                        if (currentObj == currentFieldReference) foundIdx = k;
                    }

                    if (foundIdx >= 0) references.Add((i, j, foundIdx));
                    else
                    {
                        if (currentFieldReference == null) nullReferences.Add((i, j));
                        else externalReferences.Add((i, j));
                    }
                }
            }
        }

        // Dragging event logic
        private void DragEvents(Event e)
        {
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (FindContainingField(e.mousePosition, ref startFieldIndices))
                {
                    startPos = e.mousePosition;
                    drawDragCurve = true;
                    startPos = GetFieldOutPort(startFieldIndices.Item1, startFieldIndices.Item2);
                    startRect = GetFieldRect(startFieldIndices.Item1, startFieldIndices.Item2);
                    e.Use();
                }
            }
            if (e.type == EventType.MouseUp && e.button == 0 && drawDragCurve)
            {
                // if cursor inside a window, assign it if it's a different window than the field window
                if (FindContainingWindow(e.mousePosition, ref endWindowIndex))
                {
                    if (endWindowIndex != startFieldIndices.Item1)
                        AssignReference(startFieldIndices, endWindowIndex);
                }
                // if outside any window, attempt to null it
                else AssignNullReference(startFieldIndices);
                //
                StoreComponentReferences();
                drawDragCurve = false;
                e.Use();
            }
        }
        private void AssignReference((int, int) a, int b)
        {
            // if current window different than what is already assigned, assign it
            if (fields[a.Item1][a.Item2].objectReferenceValue != serializedStates[b].targetObject)
            {
                fields[a.Item1][a.Item2].objectReferenceValue = serializedStates[b].targetObject;
                //
                if (fields[a.Item1][a.Item2].objectReferenceValue != serializedStates[b].targetObject)
                    Debug.LogWarning("Reference assignment failed. Check if the object can be assigned to this field");
                else
                    Debug.Log("Reference assigned");
            }
            serializedStates[a.Item1].ApplyModifiedProperties();
            serializedStates[a.Item1].Update();
        }
        private void AssignNullReference((int, int) a)
        {
            // if field is not null, log that we've nulled it
            if (fields[a.Item1][a.Item2].objectReferenceValue != null)
            {
                fields[a.Item1][a.Item2].objectReferenceValue = null;
                Debug.Log("Reference nulled");
            }
            serializedStates[a.Item1].ApplyModifiedProperties();
            serializedStates[a.Item1].Update();
        }

        // Find fields/windows
        private bool FindContainingField(Vector2 pos, ref (int, int) indices)
        {
            for (int i = 0; i < serializedStates.Length; i++)
            {
                for (int j = 0; j < fieldsBoxes[i].Count; j++)
                {
                    Rect relativeFieldBox = GetFieldRect(i, j);
                    if (!relativeFieldBox.Contains(pos)) continue;
                    else
                    {
                        indices.Item1 = i;
                        indices.Item2 = j;
                        return true;
                    }
                }
            }
            return false;
        }
        private bool FindContainingWindow(Vector2 pos, ref int index)
        {
            for (int i = 0; i < serializedStates.Length; i++)
            {
                Rect relativeFieldBox = windows[i];
                if (!relativeFieldBox.Contains(pos)) continue;
                else
                {
                    index = i;
                    return true;
                }
            }
            return false;
        }

        // Get positions/rects
        private Rect GetFieldRect(int i, int j)
        {
            Rect currentWindowRect = windows[i];
            Rect relativeFieldBox = fieldsBoxes[i][j];

            // Deduce starting rect to draw
            relativeFieldBox.x = relativeFieldBox.x + currentWindowRect.x;
            relativeFieldBox.y = relativeFieldBox.y + currentWindowRect.y;
            return relativeFieldBox;
        }
        private Vector2 GetFieldOutPort(int i, int j)
        {
            Rect fieldRect = GetFieldRect(i, j);

            // Deduce starting position of curve
            float posX = fieldRect.x + fieldRect.width;
            float posY = fieldRect.y + fieldRect.height - 5;
            return new Vector2(posX, posY);
        }
        private Vector2 GetWindowInPort(int i)
        {
            Rect fieldRect = windows[i];

            float posX = fieldRect.x;
            float posY = fieldRect.y + fieldRect.height - 5;
            return new Vector2(posX, posY);
        }

        // Drawing
        void DrawNodeWindow(int id)
        {
            bool somethingChanged = false;
            // sizeMultiplier = EditorGUI.DelayedFloatField("Increase scale by:", sizeMultiplier);

            List<string> currentFieldNames = fieldsNames[id];
            List<Rect> currentFieldBoxes = fieldsBoxes[id];

            //
            int offset = perFieldOffset;
            for (int j = 0; j < currentFieldNames.Count; j++)
            {
                //Rect fieldRect = new Rect(7, offset, windowWidthDefault-7-7, perFieldHeight);
                Rect fieldRect = currentFieldBoxes[j];
                GUI.Box(fieldRect, currentFieldNames[j], boxStyle);
                offset += perFieldOffset;
            }

            if (drawPODs)
            {
                List<SerializedProperty> currentPodField = podFields[id];
                List<string> currentPodFieldNames = podFieldsNames[id];
                List<Rect> currentPodFieldsBoxes = podFieldsBoxes[id];

                for (int j = 0; j < currentPodField.Count; j++)
                {
                    Rect podFieldRect = currentPodFieldsBoxes[j];
                    SerializedProperty sp = currentPodField[j];

                    GUI.Label(podFieldRect, currentPodFieldNames[j]);
                    Rect inputFieldRect = podFieldRect;
                    inputFieldRect.x = windows[id].width - 50;
                    inputFieldRect.width = 50;

                    switch (sp.propertyType)
                    {
                        case SerializedPropertyType.Float:
                            float oldValueFloat = sp.floatValue;
                            float newValueFloat = EditorGUI.DelayedFloatField(inputFieldRect, GUIContent.none, oldValueFloat, numberFieldStyle);
                            if (oldValueFloat != newValueFloat)
                            {
                                somethingChanged = true;
                                sp.floatValue = newValueFloat;
                                Debug.Log("Pod value changed");
                            }
                            break;
                        case SerializedPropertyType.Integer:
                            int oldValueInt = sp.intValue;
                            int newValueInt = EditorGUI.DelayedIntField(inputFieldRect, GUIContent.none, oldValueInt, numberFieldStyle);
                            if (oldValueInt != newValueInt)
                            {
                                somethingChanged = true;
                                sp.intValue = newValueInt;
                                Debug.Log("Pod value changed");
                            }
                            break;
                    }

                    offset += perFieldOffset;
                }
            }

            if (somethingChanged)
            {
                serializedStates[id].ApplyModifiedProperties();
                serializedStates[id].Update();
            }
            if (!drawDragCurve) GUI.DragWindow();
        }
        void DrawParentRelations()
        {
            int length = tree.Count;
            //for (int i = length-1; i>= 0; i--)
            for (int i = 0; i < length; i++)
            {
                TreeNode node = tree[i];
                if (node.draw)
                {
                    Rect nodeRect = Bbox2Rect(node.bbox);

                    Rect nameRect = nodeRect;
                    nameRect.y = nameRect.y - 15;
                    nameRect.width = tree[i].textDimensionWidth.x + 5;
                    nameRect.height = 15;

                    EditorGUI.DrawRect(nameRect, Color.white);
                    GUI.Label(nameRect, node.gameObjectName, gameNameStyle);

                    EditorGUI.DrawRect(nodeRect, node.color);
                }
            }
        }
        void UpdateNodeBbox(int index)
        {
            Bbox bbox = fullBbox;
            bool draw = false;
            TreeNode currentNode = tree[index];


            // Update by children
            int start = currentNode.index + 1;
            int end = currentNode.index + 1 + currentNode.childCount;
            for (int i = start; i < end; i++)
            {
                UpdateNodeBbox(i);
                if (tree[i].draw)
                {
                    draw = true;
                    UpdateBbox(ref bbox, tree[i].bbox);
                }
            }

            // Update by components
            start = currentNode.componentsStartingIndex;
            end = currentNode.componentsEndingIndex;
            for (int i = start; i < end; i++)
            {
                draw = true;
                UpdateBbox(ref bbox, Rect2Bbox(windows[i]));
            }

            // Add margins
            bbox.left += -marginPerObject;
            bbox.top += -marginPerObject;
            bbox.right += marginPerObject;
            bbox.bot += marginPerObject;

            //
            currentNode.bbox = bbox;
            currentNode.draw = draw;

            tree[index] = currentNode;

          /*  Debug.Log("/////");
            Debug.Log("index: " + index);
            Debug.Log(currentNode.bbox.left);
            Debug.Log(currentNode.bbox.top);
            Debug.Log(currentNode.bbox.right);
            Debug.Log(currentNode.bbox.bot);
            Debug.Log(currentNode.draw);*/
        }
        void UpdateBbox(ref Bbox a, Bbox b)
        {
            if (b.left < a.left) a.left = b.left;
            if (b.top < a.top) a.top = b.top;

            if (b.right > a.right) a.right = b.right;
            if (b.bot > a.bot) a.bot = b.bot;
        }
        //
        void DrawReferences()
        {
            for (int i = 0; i < references.Count; i++)
            {
                var reference = references[i];
                Vector2 startPos = GetFieldOutPort(reference.Item1, reference.Item2);
                Vector2 endPos = GetWindowInPort(reference.Item3);
                // DrawNodeRefLine(startPos, endPos, Color.magenta);
                DrawNodeRefLine(startPos, endPos, Color.black);
                DrawCircle(startPos, 2.0f, Color.green);
            }

            for (int i = 0; i < nullReferences.Count; i++)
            {
                var reference = nullReferences[i];
                Vector2 pos = GetFieldOutPort(reference.Item1, reference.Item2);
                DrawCircle(pos, 2.5f, Color.red);
            }

            for (int i = 0; i < externalReferences.Count; i++)
            {
                var reference = externalReferences[i];
                Vector2 pos = GetFieldOutPort(reference.Item1, reference.Item2);
                DrawCircle(pos, 2.5f, Color.yellow);
            }
        }
        void DrawCircle(Vector2 pos, float radius, Color color)
        {
            Vector3 center = new Vector3(pos.x, pos.y, 0);
            Vector3 normal = new Vector3(0, 0, 1);

            Handles.color = color;
            Handles.DrawSolidDisc(center, normal, radius);
        }
        void DrawRect(Rect rect, Color color)
        {
            /*        Uncomment and use if DrawLine allows thickness param - 2020.3+ does.
             *        Vector3 upperLeft = new Vector3(rect.x, rect.y, 0);
                    Vector3 upperRight = new Vector3(rect.x + rect.width, rect.y, 0);
                    //
                    Vector3 bottomLeft = new Vector3(rect.x, rect.y + rect.height, 0);
                    Vector3 bottomRight = new Vector3(rect.x + rect.width, rect.y + rect.height, 0);

                    Handles.color = color;

                    Handles.DrawLine(upperLeft, upperRight);
                    Handles.DrawLine(upperRight, bottomRight);
                    Handles.DrawLine(bottomRight, bottomLeft);
                    Handles.DrawLine(bottomLeft, upperLeft);*/

            Vector2 upperLeft = new Vector3(rect.x + 1, rect.y + 1);
            Vector2 upperRight = new Vector3(rect.x + rect.width - 1, rect.y + 1);
            //
            Vector2 bottomLeft = new Vector3(rect.x + 1, rect.y + rect.height - 1);
            Vector2 bottomRight = new Vector3(rect.x + rect.width - 1, rect.y + rect.height - 1);

            DrawNodeRefLineFlat(upperLeft, upperRight, color);
            DrawNodeRefLineFlat(upperRight, bottomRight, color);
            DrawNodeRefLineFlat(bottomRight, bottomLeft, color);
            DrawNodeRefLineFlat(bottomLeft, upperLeft, color);
        }
        void DrawNodeCurve(Vector2 start, Vector2 end)
        {
            Vector3 startPos = new Vector3(start.x, start.y, 0);
            Vector3 endPos = new Vector3(end.x, end.y, 0);
            Vector3 startTan = startPos + Vector3.right * 50;
            Vector3 endTan = endPos + Vector3.left * 50;
            Color shadowCol = new Color(0, 0, 0, 0.06f);

            for (int i = 0; i < 3; i++)
                Handles.DrawBezier(startPos, endPos, startTan, endTan, shadowCol, null, (i + 1) * 5);

            Handles.DrawBezier(startPos, endPos, startTan, endTan, Color.black, null, 3.5f);
        }
        void DrawNodeRefLine(Vector2 start, Vector2 end, Color color)
        {
            Vector3 startPos = new Vector3(start.x, start.y, 0);
            Vector3 endPos = new Vector3(end.x, end.y, 0);
            Vector3 startTan = startPos + Vector3.right * 50;
            Vector3 endTan = endPos + Vector3.left * 50;

            Handles.DrawBezier(startPos, endPos, startTan, endTan, color, null, 3.5f);
        }
        void DrawNodeRefLineFlat(Vector2 start, Vector2 end, Color color)
        {
            Vector3 startPos = new Vector3(start.x, start.y, 0);
            Vector3 endPos = new Vector3(end.x, end.y, 0);
            Vector3 startTan = endPos;
            Vector3 endTan = startPos;

            Handles.DrawBezier(startPos, endPos, startTan, endTan, color, null, 2.5f);
        }

        // Auxiliary
        private Color LowerAlpha(Color color)
        {
            return new Color(color.r, color.g, color.b, Mathf.Max(color.a - 0.05f, 0.5f));
        }
        private Color UpdateHue(Color color, int depth)
        {
            Color.RGBToHSV(color, out float hue, out float sat, out float brightness);

            // at depth 1 depthRatio is 1.0
            //float depthRatio = 1.0f / (depth * depth);
            float depthRatio = 1.0f / (depth + 1);
            hue = (hue + depthRatio * hueJump) % 1.0f;
            Color newColor = Color.HSVToRGB(hue, sat, brightness);

            newColor.a = color.a;
            return newColor;
        }
        public GUIStyle SetGUIStyleBackground(GUIStyle style, Color color)
        {
            texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            style.normal.background = texture;
            return style;
        }

        Rect Bbox2Rect(Bbox bbox)
        {
            return new Rect(bbox.left, bbox.top, bbox.right - bbox.left, bbox.bot - bbox.top);
        }
        Bbox Rect2Bbox(Rect rect)
        {
            return new Bbox(rect.x, rect.y, rect.x + rect.width, rect.y + rect.height);
        }
    }
}