using UnityEngine;
using UnityEditor;
using System.Collections.Generic;


public class QuickAccessWindow : EditorWindow
{
    [System.Serializable]
    public class FavoriteFolder
    {
        public string path;
        public string name;
        public string guid; 
    }

    [System.Serializable]
    public class Category
    {
        public string title = "New Category";
        public bool isExpanded = true;
        public List<FavoriteFolder> folders = new List<FavoriteFolder>();
    }

    private List<Category> categories = new List<Category>();
    private Vector2 scrollPosition;

    private const string DragFolderKey = "QuickAccessFolder";
    private const string DragCategoryKey = "QuickAccessCategory";
    
    [MenuItem("Tools/Quick Folder")]
    public static void ShowWindow()
    {
        GetWindow<QuickAccessWindow>("Quick Folder");
    }

    private void OnEnable()
    {
        LoadData();
    }

    private void OnDisable()
    {
        SaveData();
    }

    private void OnGUI()
    {
        DrawToolbar();

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        // Loop through Categories
        for (int i = 0; i < categories.Count; i++)
        {
            DrawCategory(categories[i], i);
            EditorGUILayout.Space(2);
        }

        DrawGlobalDropArea();

        EditorGUILayout.EndScrollView();
    }

    // --- DRAWING LOGIC ---

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        if (GUILayout.Button("Add Category", EditorStyles.toolbarButton))
        {
            categories.Add(new Category { title = "New Category", isExpanded = true });
            SaveData();
        }
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Save", EditorStyles.toolbarButton)) SaveData();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawCategory(Category cat, int catIndex)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        Rect headerRect = EditorGUILayout.GetControlRect(false, 24);
        
        HandleCategoryReordering(headerRect, catIndex);

        Rect foldoutRect = new Rect(headerRect.x, headerRect.y, 20, headerRect.height);
        cat.isExpanded = EditorGUI.Foldout(foldoutRect, cat.isExpanded, "");

        Rect titleRect = new Rect(headerRect.x + 20, headerRect.y + 2, headerRect.width - 60, 20);
        string newTitle = EditorGUI.TextField(titleRect, cat.title, EditorStyles.boldLabel);
        if (newTitle != cat.title) { cat.title = newTitle; SaveData(); }

        Rect deleteRect = new Rect(headerRect.x + headerRect.width - 25, headerRect.y + 2, 25, 20);
        if (GUI.Button(deleteRect, "X"))
        {
            if (EditorUtility.DisplayDialog("Delete Category", "Delete this category and all its links?", "Yes", "No"))
            {
                categories.RemoveAt(catIndex);
                SaveData();
                GUIUtility.ExitGUI();
            }
        }

        HandleAssetDropOnCategory(headerRect, cat);

        if (cat.isExpanded)
        {
            if (cat.folders.Count == 0)
            {
                EditorGUILayout.LabelField("   (Drag folders here)", EditorStyles.centeredGreyMiniLabel);
            }
            else
            {
                for (int i = 0; i < cat.folders.Count; i++)
                {
                    DrawFolderItem(cat, cat.folders[i], i);
                }
            }
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawFolderItem(Category cat, FavoriteFolder item, int index)
    {
        Rect rowRect = EditorGUILayout.GetControlRect(false, 24);
        
        HandleFolderReordering(rowRect, cat, item, index);

        EditorGUILayout.BeginHorizontal();
        
        Rect btnRect = new Rect(rowRect.x + 15, rowRect.y, rowRect.width - 40, rowRect.height);
        Rect closeRect = new Rect(rowRect.x + rowRect.width - 25, rowRect.y, 25, rowRect.height);

        GUIContent content = new GUIContent(" " + item.name, EditorGUIUtility.IconContent("Folder Icon").image);
        
        if (GUI.Button(btnRect, content, EditorStyles.miniButtonLeft))
        {
            NavigateToFolder(item.path);
        }

        if (GUI.Button(closeRect, "X", EditorStyles.miniButtonRight))
        {
            cat.folders.RemoveAt(index);
            SaveData();
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawGlobalDropArea()
    {
        EditorGUILayout.Space(10);
        Rect dropRect = GUILayoutUtility.GetRect(0, 40, GUILayout.ExpandWidth(true));
        GUI.Box(dropRect, "Drop Folder Here to Create New Category", EditorStyles.helpBox);
        
        Event evt = Event.current;
        if (dropRect.Contains(evt.mousePosition))
        {
            if (evt.type == EventType.DragUpdated)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                evt.Use();
            }
            else if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                CreateCategoryFromDrop(DragAndDrop.objectReferences);
                evt.Use();
            }
        }
    }

    private void HandleFolderReordering(Rect rect, Category currentCat, FavoriteFolder item, int index)
    {
        Event evt = Event.current;
        
        if (evt.type == EventType.MouseDrag && rect.Contains(evt.mousePosition))
        {
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.SetGenericData(DragFolderKey, item);
            DragAndDrop.SetGenericData("SourceCat", currentCat);
            DragAndDrop.StartDrag("Dragging Folder");
            evt.Use();
        }

        if (evt.type == EventType.DragUpdated && rect.Contains(evt.mousePosition))
        {
            object draggedData = DragAndDrop.GetGenericData(DragFolderKey);
            if (draggedData != null) 
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                evt.Use();
            }
        }

        if (evt.type == EventType.DragPerform && rect.Contains(evt.mousePosition))
        {
            object draggedData = DragAndDrop.GetGenericData(DragFolderKey);
            Category sourceCat = DragAndDrop.GetGenericData("SourceCat") as Category;
            FavoriteFolder draggedItem = draggedData as FavoriteFolder;

            if (draggedItem != null && sourceCat != null)
            {
                DragAndDrop.AcceptDrag();

                if (sourceCat == currentCat)
                {
                    int oldIndex = sourceCat.folders.IndexOf(draggedItem);
                    sourceCat.folders.RemoveAt(oldIndex);
                    
                    int newIndex = index;
                    if (oldIndex < index) newIndex--; 
                    
                    currentCat.folders.Insert(newIndex, draggedItem);
                }
                else
                {
                    sourceCat.folders.Remove(draggedItem);
                    currentCat.folders.Insert(index, draggedItem);
                }

                SaveData();
                evt.Use();
            }
        }

        if (DragAndDrop.visualMode == DragAndDropVisualMode.Move && rect.Contains(evt.mousePosition))
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 2), Color.cyan);
        }
    }

    private void HandleCategoryReordering(Rect rect, int index)
    {
        Event evt = Event.current;

        if (evt.type == EventType.MouseDrag && rect.Contains(evt.mousePosition))
        {
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.SetGenericData(DragCategoryKey, index);
            DragAndDrop.StartDrag("Dragging Category");
            evt.Use();
        }
        else if (evt.type == EventType.DragUpdated && rect.Contains(evt.mousePosition))
        {
            if (DragAndDrop.GetGenericData(DragCategoryKey) != null)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                evt.Use();
            }
        }
        else if (evt.type == EventType.DragPerform && rect.Contains(evt.mousePosition))
        {
            object data = DragAndDrop.GetGenericData(DragCategoryKey);
            if (data != null)
            {
                int sourceIndex = (int)data;
                if (sourceIndex != index)
                {
                    DragAndDrop.AcceptDrag();
                    Category temp = categories[sourceIndex];
                    categories.RemoveAt(sourceIndex);
                    categories.Insert(index, temp);
                    SaveData();
                }
                evt.Use();
            }
        }
        
        if (DragAndDrop.visualMode == DragAndDropVisualMode.Move && rect.Contains(evt.mousePosition) && DragAndDrop.GetGenericData(DragCategoryKey) != null)
        {
             EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 2), Color.yellow);
        }
    }

    private void HandleAssetDropOnCategory(Rect rect, Category cat)
    {
        Event evt = Event.current;
        if (rect.Contains(evt.mousePosition) && DragAndDrop.objectReferences.Length > 0 && DragAndDrop.GetGenericData(DragFolderKey) == null)
        {
            if (evt.type == EventType.DragUpdated)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                evt.Use();
            }
            else if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                foreach (Object obj in DragAndDrop.objectReferences)
                {
                    string path = AssetDatabase.GetAssetPath(obj);
                    if (AssetDatabase.IsValidFolder(path))
                    {
                        if (!cat.folders.Exists(x => x.path == path))
                        {
                            cat.folders.Add(new FavoriteFolder { path = path, name = obj.name, guid = System.Guid.NewGuid().ToString() });
                        }
                    }
                }
                SaveData();
                evt.Use();
            }
        }
    }

    private void CreateCategoryFromDrop(Object[] objects)
    {
        Category newCat = new Category { title = "New Folder Group" };
        foreach (Object obj in objects)
        {
            string path = AssetDatabase.GetAssetPath(obj);
            if (AssetDatabase.IsValidFolder(path))
            {
                newCat.folders.Add(new FavoriteFolder { path = path, name = obj.name, guid = System.Guid.NewGuid().ToString() });
            }
        }
        if (newCat.folders.Count > 0)
        {
            categories.Add(newCat);
            SaveData();
        }
    }

    private void NavigateToFolder(string path)
    {
        Object obj = AssetDatabase.LoadAssetAtPath<Object>(path);
        if (obj != null)
        {
            Selection.activeObject = obj;
            EditorGUIUtility.PingObject(obj);
            EditorUtility.FocusProjectWindow();
        }
    }

    private void SaveData()
    {
        string json = JsonUtility.ToJson(new SerializationWrapper(categories));
        EditorPrefs.SetString("QuickAccess_Pro_Favorites", json);
    }

    private void LoadData()
    {
        if (EditorPrefs.HasKey("QuickAccess_Pro_Favorites"))
        {
            var wrapper = JsonUtility.FromJson<SerializationWrapper>(EditorPrefs.GetString("QuickAccess_Pro_Favorites"));
            if (wrapper != null) categories = wrapper.list;
        }
        if (categories == null) categories = new List<Category>();
    }

    [System.Serializable]
    private class SerializationWrapper
    {
        public List<Category> list;
        public SerializationWrapper(List<Category> list) { this.list = list; }
    }
}

