using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The crash intro (ShallowsCrashIntro) flies the scout ship in, but ShallowsCrashSite builds the ship's parts static,
/// and static-batched meshes can't move. Whenever a scene is built or played, this takes the batching and occlusion
/// flags (both assume an object never moves) off the ship's parts only. The scene file itself is left as it is, so the
/// parts still show as static in the editor. The sand and debris stay static: the intro only hides and shows them.
/// </summary>
class ShallowsCrashIntroBuild : IProcessSceneWithReport
{
    const StaticEditorFlags Moving = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic;

    public int callbackOrder => 0;

    public void OnProcessScene(Scene scene, BuildReport report)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name != ShallowsCrashIntro.CrashSiteName) continue;
            Transform ship = root.transform.Find(ShallowsCrashIntro.ShipName);
            if (ship == null) continue;
            foreach (Transform part in ship.GetComponentsInChildren<Transform>(true))
            {
                StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(part.gameObject);
                if ((flags & Moving) != 0) GameObjectUtility.SetStaticEditorFlags(part.gameObject, flags & ~Moving);
            }
        }
    }
}

/// <summary>
/// Low Signal > Shallows > Preview crash intro plays the Shallows with its crash intro, without starting a new game, so
/// the save file is left alone. In Play mode it replays the intro from wherever the robot is.
/// </summary>
[InitializeOnLoad]
static class ShallowsCrashIntroPreview
{
    const string ScenePath = "Assets/_LowSignal/Scenes/Shallows.unity";
    const string Key = "LowSignal.CrashIntroPreview";
    const string StartSceneKey = "LowSignal.CrashIntroPreview.StartScene";

    static ShallowsCrashIntroPreview()
    {
        EditorApplication.playModeStateChanged += Changed;
    }

    [MenuItem("Low Signal/Shallows/Preview crash intro")]
    static void Preview()
    {
        if (EditorApplication.isPlaying)
        {
            if (!ShallowsCrashIntro.PlayIn(SceneManager.GetActiveScene()))
                Debug.LogError("The crash intro needs the Shallows, with its crashed scout ship, and can't play over itself.");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (SceneManager.GetActiveScene().path != ScenePath) EditorSceneManager.OpenScene(ScenePath);
        // Play this scene rather than the title screen; PlayFromTitle's choice is put back afterwards.
        SceneAsset startScene = EditorSceneManager.playModeStartScene;
        SessionState.SetString(StartSceneKey, startScene != null ? AssetDatabase.GetAssetPath(startScene) : "");
        EditorSceneManager.playModeStartScene = null;
        SessionState.SetBool(Key, true);
        EditorApplication.isPlaying = true;
    }

    static void Changed(PlayModeStateChange change)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            if (!ShallowsCrashIntro.PlayIn(SceneManager.GetActiveScene()))
                Debug.LogError("Couldn't preview the crash intro: the Shallows has no crashed scout ship.");
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.EraseBool(Key);
            string path = SessionState.GetString(StartSceneKey, "");
            SessionState.EraseString(StartSceneKey);
            EditorSceneManager.playModeStartScene = path.Length > 0 ? AssetDatabase.LoadAssetAtPath<SceneAsset>(path) : null;
        }
    }
}
