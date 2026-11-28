using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Batch check of the crash intro. Run Unity with -batchmode -projectPath &lt;project&gt; -executeMethod
/// ShallowsCrashIntroValidation.Run, without -nographics so it can render. It plays the Shallows, runs the intro as the
/// Preview menu does (no new game, so the save file is left alone), saves a still of each beat to the folder in
/// LOW_SIGNAL_PREVIEW_DIR (or Logs/CrashIntroPreviews), then checks that everything the intro touched was handed back:
/// the wreck where it lies, the robot at the start with its controls, physics and HUD, and play opening on the view the
/// intro ended on. Exits 0 after logging CRASH_INTRO_PASS, or 1 on any failure or error logged meanwhile.
/// </summary>
[InitializeOnLoad]
public static class ShallowsCrashIntroValidation
{
    const string Key = "LowSignal.CrashIntroValidation";
    const string ScenePath = "Assets/_LowSignal/Scenes/Shallows.unity";
    static readonly string[] StageOrder = { "Waiting", "Crash", "Reboot", "Emerge", "Done" };
    static readonly (string stage, float clock, string name)[] Stills =
    {
        ("Crash", 1.0f, "01 calm surface"),
        ("Crash", 2.0f, "02 breaking the surface"),
        ("Crash", 3.4f, "03 diving"),
        ("Crash", 4.55f, "04 about to hit"),
        ("Crash", 5.2f, "05 impact"),
        ("Crash", 6.3f, "06 skidding"),
        ("Crash", 7.4f, "07 at rest"),
        ("Crash", 9.3f, "08 closing on the hatch"),
        ("Reboot", 3.8f, "09 reboot readout"),
        ("Reboot", 5.3f, "10 signal low"),
        ("Emerge", 0.5f, "11 view cracks open"),
        ("Emerge", 1.3f, "12 rising from the hatch"),
        ("Emerge", 3.0f, "13 backing away"),
        ("Emerge", 4.6f, "14 nearly at the start"),
    };

    static bool begun, finished, batched;
    static int next, robotRenderers, wreckRenderers;
    static double startedAt, doneAt = -1;
    static Vector3 shipPosition, robotStart;
    static Quaternion shipRotation;
    static readonly List<string> errors = new List<string>();

    static ShallowsCrashIntroValidation()
    {
        EditorApplication.playModeStateChanged += Changed;
        EditorApplication.update += Tick;
    }

    public static void Run()
    {
        EditorSceneManager.OpenScene(ScenePath);
        SessionState.SetBool(Key, true);
        SessionState.SetFloat(Key + ".Started", (float)EditorApplication.timeSinceStartup);
        EditorApplication.isPlaying = true;
    }

    static void Changed(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode) Begin();
    }

    static void Begin()
    {
        if (begun || finished || !SessionState.GetBool(Key, false)) return;
        try
        {
            Application.logMessageReceived += Count;
            Scene scene = SceneManager.GetActiveScene();
            Transform ship = FindShip(scene);
            Require(ship != null, "The Shallows has no crashed scout ship");
            shipPosition = ship.position;
            shipRotation = ship.rotation;
            Transform fuselage = ship.Find("Fuselage");
            batched = fuselage != null && fuselage.GetComponent<MeshRenderer>().isPartOfStaticBatch;
            var player = UnityEngine.Object.FindFirstObjectByType<UnderwaterSwimController>();
            Require(player != null, "The Shallows has no player");
            robotStart = player.transform.position;
            robotRenderers = CountEnabled(player.GetComponentsInChildren<Renderer>());
            wreckRenderers = CountEnabled(ship.parent.GetComponentsInChildren<Renderer>());
            ShallowsCrashIntro.OverlayOnCamera = true;
            Require(ShallowsCrashIntro.PlayIn(scene), "The intro didn't start");
            begun = true;
            startedAt = EditorApplication.timeSinceStartup;
        }
        catch (Exception e) { Fail(e); }
    }

    static void Tick()
    {
        if (finished || !SessionState.GetBool(Key, false)) return;
        // However it goes, a batch run must end.
        if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Key + ".Started", 0f) > 600)
        {
            Fail(new Exception("The check didn't finish within ten minutes of starting"));
            return;
        }
        // Normally started on entering Play mode; this catches a run where that event didn't arrive.
        if (!begun && EditorApplication.isPlaying && Time.frameCount > 5) Begin();
        if (!begun || !EditorApplication.isPlaying) return;
        try
        {
            Require(EditorApplication.timeSinceStartup - startedAt < 120, "The intro didn't finish within two minutes");
            while (next < Stills.Length && Reached(Stills[next].stage, Stills[next].clock)) Capture(Stills[next++].name);
            if (ShallowsCrashIntro.Stage != "Done") return;
            if (doneAt < 0) { doneAt = EditorApplication.timeSinceStartup; return; }
            if (EditorApplication.timeSinceStartup - doneAt < 1.0) return;
            Capture("15 play begins");
            Check();
            finished = true;
            Debug.Log($"CRASH_INTRO_PASS: {ShallowsCrashIntro.Summary}; took {doneAt - startedAt:0.0} s");
            Exit(0);
        }
        catch (Exception e) { Fail(e); }
    }

    static bool Reached(string stage, float clock)
    {
        int now = Array.IndexOf(StageOrder, ShallowsCrashIntro.Stage), wanted = Array.IndexOf(StageOrder, stage);
        return now > wanted || (now == wanted && ShallowsCrashIntro.Clock >= clock);
    }

    static void Check()
    {
        Transform ship = FindShip(SceneManager.GetActiveScene());
        var player = UnityEngine.Object.FindFirstObjectByType<UnderwaterSwimController>();
        var body = player.GetComponent<Rigidbody>();
        Transform view = player.GetComponentInChildren<Camera>().transform;
        Require(!batched, "The scout ship's parts were static-batched in Play mode, so they can't move");
        Require(!ShallowsCrashIntro.IsPlaying, "The intro still claims to be playing");
        Require(Vector3.Distance(ship.position, shipPosition) < 0.001f && Quaternion.Angle(ship.rotation, shipRotation) < 0.05f,
            "The ship didn't end where the wreck lies");
        Require(CountEnabled(ship.parent.GetComponentsInChildren<Renderer>()) == wreckRenderers, "Some of the wreck, its sand or debris stayed hidden");
        foreach (Collider collider in ship.GetComponentsInChildren<Collider>(true))
            Require(collider.enabled, "The ship's collider on " + collider.name + " stayed off");
        Require(player.enabled && !player.LookBlocked, "The robot's controls weren't handed back");
        Require(Vector3.Distance(player.transform.position, robotStart) < 0.05f,
            $"The robot ended at {player.transform.position}, not the start {robotStart}");
        Require(body == null || (!body.isKinematic && body.interpolation == RigidbodyInterpolation.Interpolate
            && body.collisionDetectionMode == CollisionDetectionMode.ContinuousDynamic), "The robot's physics weren't restored");
        Require(CountEnabled(player.GetComponentsInChildren<Renderer>()) >= robotRenderers, "The robot stayed hidden");
        Vector3 offset = view.position - player.transform.position;
        Require(Vector3.Distance(offset, ShallowsCrashIntro.HandoverOffset) < 0.05f,
            $"Play opened on a camera offset {offset}, not the {ShallowsCrashIntro.HandoverOffset} the intro ended on");
        foreach (Canvas canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            if (canvas.name == "Robot HUD") Require(canvas.enabled, "The robot HUD stayed hidden");
        Require(errors.Count == 0, errors.Count + " errors were logged during the intro. First: " + (errors.Count > 0 ? errors[0] : ""));
    }

    static Transform FindShip(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root.name == ShallowsCrashIntro.CrashSiteName) return root.transform.Find(ShallowsCrashIntro.ShipName);
        return null;
    }

    static int CountEnabled(Renderer[] renderers)
    {
        int count = 0;
        foreach (Renderer renderer in renderers) if (renderer.enabled) count++;
        return count;
    }

    static void Capture(string name)
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
        var player = UnityEngine.Object.FindFirstObjectByType<UnderwaterSwimController>();
        Camera camera = player != null ? player.GetComponentInChildren<Camera>() : Camera.main;
        if (camera == null) return;
        string folder = Environment.GetEnvironmentVariable("LOW_SIGNAL_PREVIEW_DIR");
        if (string.IsNullOrEmpty(folder)) folder = "Logs/CrashIntroPreviews";
        Directory.CreateDirectory(folder);
        var target = new RenderTexture(1280, 720, 24);
        RenderTexture previousTarget = camera.targetTexture, previousActive = RenderTexture.active;
        camera.targetTexture = target;
        Canvas.ForceUpdateCanvases();
        camera.Render();
        RenderTexture.active = target;
        var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        image.Apply();
        File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());
        RenderTexture.active = previousActive;
        camera.targetTexture = previousTarget;
        UnityEngine.Object.DestroyImmediate(image);
        target.Release();
        UnityEngine.Object.DestroyImmediate(target);
    }

    static void Count(string message, string stackTrace, LogType type)
    {
        if (begun && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)) errors.Add(message);
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    static void Fail(Exception e)
    {
        finished = true;
        Debug.LogException(e);
        Debug.Log("CRASH_INTRO_FAIL: " + e.Message);
        Exit(1);
    }

    static void Exit(int code)
    {
        SessionState.EraseBool(Key);
        Application.logMessageReceived -= Count;
        EditorApplication.Exit(code);
    }
}
