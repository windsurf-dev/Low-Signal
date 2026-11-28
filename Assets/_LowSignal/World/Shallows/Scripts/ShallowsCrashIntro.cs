using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static HudStyle;

/// <summary>
/// The new-game intro in the Shallows. It plays once, right after Start Game (never on Load Game), and can't be skipped;
/// the pause menu still works.
///  1. Crash: looking up from the start, the robot's scout ship punches through the surface and dives, trailing bubbles
///     and its sputtering engine, then slams into the seabed and skids into the wreck the level is dressed with (built
///     by ShallowsCrashSite), throwing up sand and plating. Its distress beacon starts blinking.
///  2. Reboot: on black, the robot restarts in the HUD's style, line by line, and ends on SIGNAL: LOW.
///  3. Emerge: its view flickers open on the robot rising out of the ship's open hatch. It backs away to the start, and
///     play begins on exactly the view the game would have opened on.
/// It adds itself when a new game's first area holds the crash site, so the scene needs no setup. The ship's path,
/// which avoids scenery, and every camera shot are worked out from where the wreck and the player's start are. To tune
/// it, add this component to any object in the Shallows: the intro then uses that one, with its settings.
/// The ship's parts must not be static-batched to move; ShallowsCrashIntroBuild clears that flag when the scene is
/// built or played. Low Signal > Shallows > Preview crash intro plays it without starting a new game.
/// </summary>
public class ShallowsCrashIntro : MonoBehaviour
{
    public const string CrashSiteName = "Crash site - scout ship";
    public const string ShipName = "Scout ship";
    // ShallowsCrashSite builds the ship from its tail at the origin, its nose 6 m along +Y and its top along +X.
    const float ShipLength = 6f;
    static readonly Vector3 EngineLocal = new Vector3(-0.1f, -0.7f, 0.62f);
    static readonly Color DustColor = new Color(0.8f, 0.76f, 0.62f, 0.42f);
    static readonly Color BubbleColor = new Color(0.85f, 0.97f, 1f, 0.75f);

    public enum BootState { Ok, Warning, Fault }

    [System.Serializable]
    public class BootLine
    {
        public string label;
        public string status;
        public BootState state;
        public BootLine() { }
        public BootLine(string label, string status, BootState state) { this.label = label; this.status = status; this.state = state; }
    }

    /// <summary>True from the moment the intro covers the screen until play begins.</summary>
    public static bool IsPlaying { get; private set; }
    /// <summary>For tests and previews: Crash, Reboot, Emerge, then Done; and seconds into that stage.</summary>
    public static string Stage { get; private set; } = "";
    public static float Clock { get; private set; }
    /// <summary>For tests: the robot-to-camera offset play opens on, which the intro ends on.</summary>
    public static Vector3 HandoverOffset { get; private set; }
    /// <summary>For tests: the dive angle chosen and the camera spots, written once they are worked out.</summary>
    public static string Summary { get; private set; } = "";
    /// <summary>For tests: draws the overlay through the player's camera, so renders of that camera include it.</summary>
    public static bool OverlayOnCamera { get; set; }

    [Header("Crash")]
    [Tooltip("Seconds looking up at the calm surface before the ship breaks through it.")]
    [Min(0f)] public float calmSeconds = 1.6f;
    [Tooltip("Seconds from breaking the surface to hitting the seabed.")]
    [Min(0.5f)] public float fallSeconds = 3.2f;
    [Tooltip("Dive angles below horizontal to try, in order; the first whose path is clear of scenery is used.")]
    public float[] diveAngles = { 40f, 50f, 60f, 70f, 80f };
    [Tooltip("How far the nose skids along the seabed before stopping where the wreck lies. The sand's skid marks run about 10 m.")]
    [Min(1f)] public float skidLength = 10f;
    [Min(0.3f)] public float skidSeconds = 2.2f;
    [Tooltip("Seconds on the settled wreck, closing in on its hatch, before the screen goes black.")]
    [Min(1.5f)] public float aftermathSeconds = 3.4f;
    [Tooltip("Strongest camera shake when the ship hits, in metres.")]
    [Min(0f)] public float impactShake = 0.35f;

    [Header("Reboot")]
    public string bootTitle = "EMERGENCY REBOOT";
    public BootLine[] bootLines =
    {
        new BootLine("IMPACT DETECTED", "HULL BREACHED", BootState.Fault),
        new BootLine("POWER CORE", "ONLINE", BootState.Ok),
        new BootLine("THRUSTERS", "ONLINE", BootState.Ok),
        new BootLine("OPTICS", "RESTORED", BootState.Ok),
        new BootLine("UPLINK", "NO CARRIER", BootState.Warning),
    };
    public string signalText = "SIGNAL: LOW";

    [Header("Robot")]
    [Tooltip("The ship's open hatch in the ship's own frame: +Y runs from tail to nose, +X is its top.")]
    public Vector3 hatchLocal = new Vector3(0.68f, 2.55f, 0f);
    [Tooltip("Seconds for the robot to rise out of the hatch.")]
    [Min(0.3f)] public float riseSeconds = 1.5f;
    [Tooltip("Seconds for the robot to back away from the wreck to the start.")]
    [Min(0.5f)] public float driftSeconds = 3.2f;

    // The scene.
    Transform crashSite, ship, view;
    UnderwaterSwimController player;
    Rigidbody body;
    float surfaceY;
    // The wreck at rest and the ship's way there. The path is the nose's: it ends buried where the wreck's nose is.
    Vector3 restPosition, restNose, noseAxis, travel, touchNose, entryNose, dive;
    Quaternion restRotation, diveRotation;
    float noseLift, diveLength, diveAngle;
    // Where play starts, and the view it opens on.
    Vector3 start, cameraOffset;
    Quaternion startRotation, cameraRotation;
    // Camera spots and what they look at.
    Vector3 shotSurface, shotImpact, shotHatch, surfaceLook, wreckLook, hatchLook;

    // What the intro switched off or hid, to give back.
    readonly List<Behaviour> paused = new List<Behaviour>();
    readonly List<Canvas> hiddenCanvases = new List<Canvas>();
    readonly List<Renderer> robotRenderers = new List<Renderer>();
    readonly List<Light> robotLights = new List<Light>();
    readonly List<Renderer> shipRenderers = new List<Renderer>();
    readonly List<Light> shipLights = new List<Light>();
    readonly List<Renderer> leftovers = new List<Renderer>();
    readonly List<Collider> shipColliders = new List<Collider>();
    bool began, hostCreated, tookControl, tookWreck, robotHidden, lookWasBlocked;
    bool bodyWasKinematic;
    RigidbodyInterpolation bodyInterpolation;
    CollisionDetectionMode bodyDetection;
    CrashBeacon beacon;
    Light beaconLight;
    ParticleSystem leak;

    // Effects, all emitted by hand.
    Transform effects;
    ParticleSystem bubbles, dust, embers;
    Material bubbleMaterial, glowMaterial, hullMaterial, scorchedMaterial;
    Light flash;
    float flashAge, trailDebt, emberDebt, dustDebt;
    readonly List<Chunk> chunks = new List<Chunk>();

    class Chunk
    {
        public Transform transform;
        public Vector3 velocity, spin, size;
        public float age, landedAt = -1f;
    }

    // The overlay: fades, the eyelids the view opens through, and the reboot readout.
    GameObject overlay;
    Canvas overlayCanvas;
    Image fade;
    RectTransform topLid, bottomLid;
    RawImage scanlines;
    CanvasGroup bootGroup, linesGroup, signalGroup;
    Text titleText, signal, signalGhost;
    Text[] labelTexts, statusTexts;

    // ---------- Starting ----------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Register()
    {
        IsPlaying = false;
        OverlayOnCamera = false;
        Stage = "";
        Clock = 0f;
        Summary = "";
        SceneManager.sceneLoaded -= SceneLoaded;
        SceneManager.sceneLoaded += SceneLoaded;
    }

    static void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Start Game marks the new game; the first area to load takes the mark, whether or not it has a crash site.
        if (mode == LoadSceneMode.Single && SaveGameService.Instance.TakeNewGameIntro()) PlayIn(scene);
    }

    /// <summary>Plays the intro in a loaded scene that holds the crash site. False if it has none.</summary>
    public static bool PlayIn(Scene scene)
    {
        if (IsPlaying || FindCrashSite(scene) == null) return false;
        ShallowsCrashIntro intro = null;
        foreach (var candidate in FindObjectsByType<ShallowsCrashIntro>(FindObjectsSortMode.None))
            if (candidate.gameObject.scene == scene) { intro = candidate; break; }
        bool created = intro == null;
        if (created)
        {
            var host = new GameObject("Crash intro");
            SceneManager.MoveGameObjectToScene(host, scene);
            intro = host.AddComponent<ShallowsCrashIntro>();
        }
        intro.Begin(created);
        return true;
    }

    static Transform FindCrashSite(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded) return null;
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root.name == CrashSiteName) return root.transform;
        return null;
    }

    void Begin(bool createdHost)
    {
        if (began) return;
        began = true;
        hostCreated = createdHost;
        IsPlaying = true;
        Stage = "Waiting";
        Clock = 0f;
        // Black straight away, so the start never shows before the intro.
        BuildOverlay();
        StartCoroutine(Play());
    }

    IEnumerator Play()
    {
        // A new game autosaves on arriving here, which needs the robot's controls on, so let that go first.
        for (float waited = 0f; SaveGameService.Instance.ArrivalSavePending && waited < 3f; waited += Time.unscaledDeltaTime)
            yield return null;
        // By the next frame the robot's camera sits where play would open.
        yield return null;
        if (!FindScene())
        {
            Debug.LogWarning("Crash intro skipped: it needs the crash site's scout ship and an active player with a camera.", this);
            Finish();
            yield break;
        }
        TakeControl();
        TakeWreck();
        PlanShots();
        Stage = "Crash";
        yield return Crash();
        Stage = "Reboot";
        Clock = 0f;
        yield return Reboot();
        Stage = "Emerge";
        Clock = 0f;
        yield return Emerge();
        ReturnControl();
        IsPlaying = false;
        Stage = "Done";
        yield return FlickerHud();
        Finish();
    }

    bool FindScene()
    {
        crashSite = FindCrashSite(gameObject.scene);
        ship = crashSite != null ? crashSite.Find(ShipName) : null;
        player = FindFirstObjectByType<UnderwaterSwimController>();
        Camera camera = player != null ? player.GetComponentInChildren<Camera>() : null;
        if (ship == null || player == null || !player.enabled || camera == null) return false;
        view = camera.transform;
        body = player.GetComponent<Rigidbody>();
        return true;
    }

    // ---------- Taking and giving back ----------

    void TakeControl()
    {
        start = player.transform.position;
        startRotation = player.transform.rotation;
        // The view play opens on, kept relative to the robot; the intro ends on exactly this.
        cameraOffset = view.position - start;
        cameraRotation = view.rotation;
        HandoverOffset = cameraOffset;

        paused.Clear();
        foreach (Behaviour behaviour in new Behaviour[] { player, player.GetComponent<AbilityWheel>() })
            if (behaviour != null && behaviour.enabled) { behaviour.enabled = false; paused.Add(behaviour); }
        // Blocking the look also keeps the pickup from taking input, even if the pause menu relocks the cursor.
        lookWasBlocked = player.LookBlocked;
        player.LookBlocked = true;
        Cursor.visible = false;
        if (body != null)
        {
            bodyWasKinematic = body.isKinematic;
            bodyInterpolation = body.interpolation;
            bodyDetection = body.collisionDetectionMode;
            if (!body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            // Moved by hand meanwhile, through the hull and out of the hatch.
            body.interpolation = RigidbodyInterpolation.None;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.isKinematic = true;
        }
        HideCanvases();
        SetRobotVisible(false);
        if (OverlayOnCamera)
        {
            overlayCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            overlayCanvas.worldCamera = view.GetComponent<Camera>();
            overlayCanvas.planeDistance = overlayCanvas.worldCamera.nearClipPlane + 0.02f;
        }
        tookControl = true;
    }

    void ReturnControl()
    {
        if (!tookControl) return;
        tookControl = false;
        if (player != null)
        {
            PlaceRobot(start);
            SetRobotVisible(true);
            player.LookBlocked = lookWasBlocked;
            if (body != null)
            {
                body.isKinematic = bodyWasKinematic;
                body.collisionDetectionMode = bodyDetection;
                body.interpolation = bodyInterpolation;
                if (!body.isKinematic) body.linearVelocity = Vector3.zero;
            }
        }
        foreach (Behaviour behaviour in paused)
            if (behaviour != null) behaviour.enabled = true;
        paused.Clear();
        // Disabling the swim controller freed the cursor; play keeps it locked.
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void HideCanvases()
    {
        hiddenCanvases.Clear();
        // The pause menu (sorting order 100 and up) stays available.
        foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            if (canvas.isRootCanvas && canvas.enabled && canvas.gameObject != overlay && canvas.sortingOrder < 100)
            { canvas.enabled = false; hiddenCanvases.Add(canvas); }
    }

    void SetCanvases(bool on)
    {
        foreach (Canvas canvas in hiddenCanvases)
            if (canvas != null) canvas.enabled = on;
    }

    void SetRobotVisible(bool visible)
    {
        if (!visible)
        {
            if (robotHidden) return;
            robotHidden = true;
            foreach (Renderer renderer in player.GetComponentsInChildren<Renderer>())
                if (renderer.enabled) { renderer.enabled = false; robotRenderers.Add(renderer); }
            foreach (Light light in player.GetComponentsInChildren<Light>())
                if (light.enabled) { light.enabled = false; robotLights.Add(light); }
            return;
        }
        robotHidden = false;
        foreach (Renderer renderer in robotRenderers)
            if (renderer != null) renderer.enabled = true;
        foreach (Light light in robotLights)
            if (light != null) light.enabled = true;
        robotRenderers.Clear();
        robotLights.Clear();
    }

    void PlaceRobot(Vector3 position)
    {
        player.transform.SetPositionAndRotation(position, startRotation);
        if (body != null) { body.position = position; body.rotation = startRotation; }
    }

    void TakeWreck()
    {
        restPosition = ship.position;
        restRotation = ship.rotation;
        noseAxis = restRotation * Vector3.up;
        travel = Flat(noseAxis);
        restNose = restPosition + noseAxis * ShipLength;
        noseLift = restNose.y - Ground(restNose);
        surfaceY = FindSurface();
        // It touches down a skid's length back along the trail, its nose as deep in the sand as the wreck's is.
        touchNose = restNose - travel * skidLength;
        touchNose.y = Ground(touchNose) + noseLift;
        ChooseDive();

        foreach (CrashBeacon pulse in ship.GetComponentsInChildren<CrashBeacon>())
            if (pulse.name == "Distress beacon") beacon = pulse;
        if (beacon != null)
        {
            // Dark until the ship has come to rest.
            beacon.enabled = false;
            beaconLight = beacon.GetComponentInChildren<Light>();
            if (beaconLight != null) beaconLight.enabled = false;
            var lamp = beacon.GetComponent<Renderer>();
            if (lamp != null)
            {
                var block = new MaterialPropertyBlock();
                lamp.GetPropertyBlock(block);
                block.SetColor("_EmissionColor", Color.black);
                lamp.SetPropertyBlock(block);
            }
        }
        foreach (Renderer renderer in ship.GetComponentsInChildren<Renderer>())
            if (renderer.enabled && !(renderer is ParticleSystemRenderer)) shipRenderers.Add(renderer);
        foreach (Light light in ship.GetComponentsInChildren<Light>())
            if (light.enabled && light != beaconLight) shipLights.Add(light);
        foreach (Collider collider in ship.GetComponentsInChildren<Collider>())
            if (collider.enabled) { collider.enabled = false; shipColliders.Add(collider); }
        // The skid's sand and the strewn debris appear under the dust when it hits.
        foreach (string name in new[] { "Shoved-up sand", "Debris" })
        {
            Transform part = crashSite.Find(name);
            if (part == null) continue;
            foreach (Renderer renderer in part.GetComponentsInChildren<Renderer>())
                if (renderer.enabled) { renderer.enabled = false; leftovers.Add(renderer); }
        }
        Transform leaking = ship.Find("Leaking bubbles");
        leak = leaking != null ? leaking.GetComponent<ParticleSystem>() : null;
        if (leak != null) leak.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        hullMaterial = MaterialOf("Wings, fin and engines");
        scorchedMaterial = MaterialOf("Scorched plating");
        tookWreck = true;
        SetShipVisible(false);
        PoseShip(0f);
        BuildEffects();
    }

    void GiveBackWreck()
    {
        if (!tookWreck) return;
        tookWreck = false;
        if (ship != null) ship.SetPositionAndRotation(restPosition, restRotation);
        SetShipVisible(true);
        foreach (Renderer renderer in leftovers)
            if (renderer != null) renderer.enabled = true;
        foreach (Collider collider in shipColliders)
            if (collider != null) collider.enabled = true;
        leftovers.Clear();
        shipColliders.Clear();
        shipRenderers.Clear();
        shipLights.Clear();
        BeaconOn();
        if (leak != null && !leak.isPlaying) leak.Play(true);
    }

    void SetShipVisible(bool visible)
    {
        foreach (Renderer renderer in shipRenderers)
            if (renderer != null) renderer.enabled = visible;
        foreach (Light light in shipLights)
            if (light != null) light.enabled = visible;
    }

    void BeaconOn()
    {
        if (beaconLight != null) beaconLight.enabled = true;
        if (beacon != null) beacon.enabled = true;
    }

    void Finish()
    {
        Cleanup();
        Stage = "Done";
        // Let the last bubbles and dust drift away before the effects go.
        if (hostCreated) Destroy(gameObject, 8f);
        else if (effects != null) Destroy(effects.gameObject, 8f);
    }

    void Cleanup()
    {
        GiveBackWreck();
        ReturnControl();
        SetCanvases(true);
        hiddenCanvases.Clear();
        if (overlay != null) Destroy(overlay);
        overlay = null;
        if (flash != null) Destroy(flash.gameObject);
        IsPlaying = false;
        began = false;
    }

    void OnDisable()
    {
        // Leaving the scene mid-intro: hand everything back.
        if (!began) return;
        StopAllCoroutines();
        Cleanup();
    }

    // ---------- Planning ----------

    void ChooseDive()
    {
        // It starts just above the surface, so it breaks through at speed.
        float top = surfaceY + 3f;
        foreach (float angle in diveAngles)
            if (TryDive(angle, top, true)) return;
        TryDive(85f, top, false);
    }

    bool TryDive(float angle, float top, bool mustBeClear)
    {
        float radians = Mathf.Clamp(angle, 10f, 89f) * Mathf.Deg2Rad;
        Vector3 direction = (travel * Mathf.Cos(radians) - Vector3.up * Mathf.Sin(radians)).normalized;
        if (mustBeClear)
        {
            // The hull trails the nose down the same line, so a ship-wide tube along it must be clear, from a little
            // above the seabed to just under the surface (whose invisible ceiling is not scenery anyway).
            float toSurface = (surfaceY - 0.5f - touchNose.y) / Mathf.Sin(radians) - 2f;
            if (Blocked(touchNose - direction * 2f, -direction, toSurface, 1.6f)) return false;
        }
        dive = direction;
        diveAngle = angle;
        diveLength = Mathf.Max(2f, (top - touchNose.y) / Mathf.Sin(radians));
        entryNose = touchNose - direction * diveLength;
        diveRotation = Quaternion.FromToRotation(noseAxis, direction) * restRotation;
        return true;
    }

    void PlanShots()
    {
        Vector3 middle = restPosition + noseAxis * (ShipLength * 0.5f);
        Vector3 toStart = Flat(start - middle);
        // 1: from the start, looking up the dive at where the ship will break the surface, just under it.
        shotSurface = start + cameraOffset + Vector3.up * 0.5f;
        Vector3 breakPoint = entryNose + dive * ((surfaceY - entryNose.y) / dive.y);
        surfaceLook = breakPoint + dive * 3f;
        // 2: on the start's side of the skid, square on to it, high enough to see over the dunes.
        Vector3 skidMiddle = middle - travel * (skidLength * 0.5f);
        shotImpact = ClearSpot(skidMiddle, toStart, new[] { 8f, 10f, 6.5f, 12f }, 2.4f, 0.4f) ?? shotSurface;
        // 3: closing in on the hatch from the same side.
        Vector3 hatch = restPosition + restRotation * hatchLocal;
        shotHatch = ClearSpot(hatch, toStart, new[] { 4.5f, 5.5f, 7f }, 1.2f, 0.6f) ?? shotImpact;
        wreckLook = middle;
        hatchLook = hatch;
        Summary = $"dive {diveAngle:0}° over {diveLength:0.0} m from {entryNose}; touchdown {touchNose}; surface y {surfaceY:0.0}; " +
            $"shots {shotSurface} / {shotImpact} / {shotHatch}";
    }

    Vector3? ClearSpot(Vector3 target, Vector3 away, float[] distances, float lift, float rise)
    {
        foreach (float distance in distances)
        {
            Vector3 spot = target + away * distance;
            spot.y = Mathf.Max(Ground(spot) + lift, target.y + rise);
            Vector3 toTarget = target - spot;
            if (Occupied(spot, 0.7f) || Blocked(spot, toTarget.normalized, toTarget.magnitude - 1f, 0.25f)) continue;
            return spot;
        }
        return null;
    }

    // Scenery that would block the ship or a camera: not the wreck, the robot, or invisible limits like the ceiling
    // under the surface.
    bool Counts(Collider collider)
    {
        Transform node = collider.transform;
        if ((crashSite != null && node.IsChildOf(crashSite)) || (player != null && node.IsChildOf(player.transform))) return false;
        return collider is TerrainCollider || collider.GetComponentInChildren<Renderer>() != null;
    }

    bool Blocked(Vector3 origin, Vector3 direction, float distance, float radius)
    {
        if (distance <= 0f) return false;
        foreach (RaycastHit hit in Physics.SphereCastAll(origin, radius, direction, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (hit.distance > 0f && Counts(hit.collider)) return true;
        return false;
    }

    bool Occupied(Vector3 point, float radius)
    {
        foreach (Collider collider in Physics.OverlapSphere(point, radius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (Counts(collider)) return true;
        return false;
    }

    float FindSurface()
    {
        foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            foreach (Transform node in root.GetComponentsInChildren<Transform>(true))
                if (node.name == "Water Surface") return node.position.y;
        return restNose.y + 20f;
    }

    static float Ground(Vector3 point)
    {
        foreach (Terrain terrain in Terrain.activeTerrains)
        {
            Vector3 local = point - terrain.transform.position, size = terrain.terrainData.size;
            if (local.x >= 0f && local.z >= 0f && local.x <= size.x && local.z <= size.z)
                return terrain.SampleHeight(point) + terrain.transform.position.y;
        }
        if (Physics.Raycast(point + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 60f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return hit.point.y;
        return point.y;
    }

    // ---------- 1: The crash ----------

    IEnumerator Crash()
    {
        float fallStart = calmSeconds, touch = fallStart + fallSeconds, rest = touch + skidSeconds;
        float end = rest + aftermathSeconds, cutToImpact = touch - 0.45f, cutToHatch = rest + 0.8f;
        bool shown = false, splashed = false, landed = false, revealed = false, settled = false, lit = false;
        int shot = -1;
        Vector3 look = surfaceLook, lookVelocity = Vector3.zero;
        for (float t = 0f; t < end; t += Time.deltaTime)
        {
            Clock = t;
            PoseShip(t);
            Vector3 nose = ship.position + ship.up * ShipLength, middle = ship.position + ship.up * (ShipLength * 0.5f);
            if (!shown && t >= fallStart) { shown = true; SetShipVisible(true); }
            if (!splashed && shown && nose.y <= surfaceY) { splashed = true; Splash(new Vector3(nose.x, surfaceY - 0.3f, nose.z)); }
            if (!landed && t >= touch) { landed = true; Impact(); }
            if (!revealed && t >= touch + 0.35f)
            {
                revealed = true;
                foreach (Renderer renderer in leftovers) if (renderer != null) renderer.enabled = true;
            }
            if (!settled && t >= rest)
            {
                settled = true;
                foreach (Collider collider in shipColliders) if (collider != null) collider.enabled = true;
            }
            if (settled && leak != null && !leak.isPlaying && t >= rest + 0.6f) leak.Play(true);
            if (!lit && t >= rest + 1.1f) { lit = true; BeaconOn(); }
            EmitTrails(t, nose, fallStart, touch, rest);
            UpdateChunks();
            UpdateFlash();

            // Three shots with cuts between; the look trails the ship a little.
            int now = t < cutToImpact ? 0 : t < cutToHatch ? 1 : 2;
            Vector3 position, target;
            if (now == 0)
            {
                position = shotSurface + Vector3.up * (0.4f * t / cutToImpact);
                target = t < fallStart ? surfaceLook : middle;
            }
            else if (now == 1)
            {
                position = shotImpact;
                target = middle;
            }
            else
            {
                float u = Smooth((t - cutToHatch) / (end - cutToHatch));
                position = Vector3.Lerp(shotImpact, shotHatch, u);
                target = Vector3.Lerp(wreckLook, hatchLook, u);
            }
            if (now != shot) { shot = now; look = target; lookVelocity = Vector3.zero; }
            else look = Vector3.SmoothDamp(look, target, ref lookVelocity, 0.22f, Mathf.Infinity, Time.deltaTime);
            position += Shake(t - touch);
            if ((look - position).sqrMagnitude > 1e-4f) view.SetPositionAndRotation(position, Quaternion.LookRotation(look - position));
            SetFade(t < 1f ? 1f - t : Window(end - 0.9f, end, t));
            yield return null;
        }
        SetFade(1f);
        GiveBackWreck();
    }

    void PoseShip(float t)
    {
        float fallStart = calmSeconds, touch = fallStart + fallSeconds, rest = touch + skidSeconds;
        Vector3 nose;
        Quaternion rotation;
        if (t < touch)
        {
            float u = Mathf.Clamp01((t - fallStart) / fallSeconds);
            // Fastest through the surface, slowed by the water, still moving when it hits. It rolls level on the way.
            const float carry = 0.55f;
            nose = entryNose + dive * (diveLength * (carry * u + (1f - carry) * (1f - (1f - u) * (1f - u))));
            float roll = Mathf.Lerp(-40f, 0f, Smooth(u)) + Mathf.Sin(u * 9f) * 5f * (1f - u);
            rotation = Quaternion.AngleAxis(roll, dive) * diveRotation;
        }
        else if (t < rest)
        {
            float u = (t - touch) / skidSeconds;
            nose = touchNose + travel * (skidLength * (1f - (1f - u) * (1f - u)));
            // It bounces off the seabed at first, then ploughs in, ending exactly where the wreck lies.
            float bounce = 0.5f * Mathf.Abs(Mathf.Sin(u * Mathf.PI * 2.5f)) * (1f - u) * (1f - u);
            nose.y = Mathf.Lerp(Ground(nose) + noseLift + bounce, restNose.y, Window(0.75f, 1f, u));
            float wobble = Mathf.Sin(u * 14f) * 7f * (1f - u);
            rotation = Quaternion.AngleAxis(wobble, travel) * Quaternion.Slerp(diveRotation, restRotation, Window(0f, 0.7f, u));
        }
        else
        {
            // Rocks on its buried nose and settles.
            float since = t - rest;
            float rock = 2.5f * Mathf.Exp(-4f * since) * Mathf.Sin(since * 11f);
            nose = restNose;
            rotation = Quaternion.AngleAxis(rock, Vector3.Cross(Vector3.up, travel)) * restRotation;
        }
        ship.SetPositionAndRotation(nose - rotation * Vector3.up * ShipLength, rotation);
    }

    Vector3 Shake(float since)
    {
        if (since < 0f || impactShake <= 0f) return Vector3.zero;
        float strength = impactShake * Mathf.Exp(-2.6f * since), f = since * 22f;
        return new Vector3(Mathf.PerlinNoise(f, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, f) - 0.5f, Mathf.PerlinNoise(f, f + 5f) - 0.5f) * (2f * strength);
    }

    // ---------- Effects ----------

    void BuildEffects()
    {
        effects = new GameObject("Crash intro effects").transform;
        effects.SetParent(transform, false);
        bubbleMaterial = leak != null ? leak.GetComponent<ParticleSystemRenderer>().sharedMaterial : null;
        if (bubbleMaterial == null) bubbleMaterial = FallbackParticleMaterial();
        foreach (ParticleSystemRenderer renderer in player.GetComponentsInChildren<ParticleSystemRenderer>(true))
            if (renderer.sharedMaterial != null && renderer.sharedMaterial.name.Contains("Glow")) { glowMaterial = renderer.sharedMaterial; break; }
        if (glowMaterial == null) glowMaterial = bubbleMaterial;
        bubbles = MakeSystem("Bubbles", bubbleMaterial, 3000, -0.12f, 0.35f, 0.8f, 1f);
        dust = MakeSystem("Sand cloud", bubbleMaterial, 800, -0.01f, 0.4f, 1.2f, 2.2f);
        embers = MakeSystem("Engine sputter", glowMaterial, 400, -0.05f, 0.6f, 2f, 0.3f);
    }

    ParticleSystem MakeSystem(string name, Material material, int max, float gravity, float noise, float drag, float growTo)
    {
        var host = new GameObject(name);
        host.SetActive(false);
        host.transform.SetParent(effects, false);
        var system = host.AddComponent<ParticleSystem>();
        var main = system.main;
        main.playOnAwake = false;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = max;
        main.gravityModifier = gravity;
        main.startSpeed = 0f;
        var emission = system.emission;
        emission.enabled = false;
        var shape = system.shape;
        shape.enabled = false;
        var wander = system.noise;
        wander.enabled = true;
        wander.strength = noise;
        wander.frequency = 0.5f;
        wander.scrollSpeed = 0.3f;
        var limit = system.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.limit = 1000f;
        limit.drag = drag;
        var colour = system.colorOverLifetime;
        colour.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
        colour.color = gradient;
        var size = system.sizeOverLifetime;
        size.enabled = true;
        size.size = growTo >= 1f
            ? new ParticleSystem.MinMaxCurve(growTo, AnimationCurve.Linear(0f, 1f / growTo, 1f, 1f))
            : new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, growTo));
        var renderer = host.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        host.SetActive(true);
        system.Play();
        return system;
    }

    static Material FallbackParticleMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        return shader != null ? new Material(shader) : null;
    }

    Material MaterialOf(string partName)
    {
        Transform part = ship.Find(partName);
        Renderer renderer = part != null ? part.GetComponent<Renderer>() : null;
        return renderer != null ? renderer.sharedMaterial : null;
    }

    static void Emit(ParticleSystem system, Vector3 at, Vector3 velocity, float size, float life, Color color)
    {
        var particle = new ParticleSystem.EmitParams
        {
            position = at, velocity = velocity, startSize = size, startLifetime = life, startColor = color,
            applyShapeToPosition = false,
        };
        system.Emit(particle, 1);
    }

    void Bubble(Vector3 at, Vector3 velocity, float size, float life) => Emit(bubbles, at, velocity, size, life, BubbleColor);

    void Dust(Vector3 at, Vector3 velocity) => Emit(dust, at, velocity, Random.Range(1.4f, 3.2f), Random.Range(4f, 7f), DustColor);

    void EmitTrails(float t, Vector3 nose, float fallStart, float touch, float rest)
    {
        float dt = Time.deltaTime;
        bool falling = t >= fallStart && t < touch, skidding = t >= touch && t < rest;
        if (falling || skidding)
        {
            // A wake of bubbles off the hull, and the live engine spitting embers.
            for (trailDebt += (falling ? 90f : 35f) * dt; trailDebt >= 1f; trailDebt--)
                Bubble(Vector3.Lerp(ship.position, nose, Random.value) + Random.insideUnitSphere * 0.7f,
                    Random.insideUnitSphere * 0.6f + Vector3.up * 0.4f, Random.Range(0.05f, 0.22f), Random.Range(2.5f, 5f));
            Vector3 engine = ship.TransformPoint(EngineLocal);
            for (emberDebt += (falling ? 40f : 15f) * dt; emberDebt >= 1f; emberDebt--)
                Emit(embers, engine + Random.insideUnitSphere * 0.2f, -ship.up * Random.Range(0.5f, 1.5f) + Random.insideUnitSphere * 0.4f,
                    Random.Range(0.15f, 0.45f), Random.Range(0.4f, 0.9f), new Color(1f, 0.55f, 0.18f, 0.9f));
        }
        if (skidding)
        {
            // Sand thrown up by the nose as it ploughs.
            float u = (t - touch) / skidSeconds;
            for (dustDebt += Mathf.Lerp(70f, 10f, u) * dt; dustDebt >= 1f; dustDebt--)
                Dust(nose + Random.insideUnitSphere * 1.2f, (Random.insideUnitSphere + Vector3.up * 0.6f) * Random.Range(0.8f, 2.6f) + travel * (1.5f * (1f - u)));
        }
    }

    void Splash(Vector3 at)
    {
        // A burst of bubbles driven down with the ship, foam spreading under the surface, and a flash of light.
        for (int i = 0; i < 240; i++)
        {
            Vector3 push = Random.insideUnitSphere;
            push.y = -Mathf.Abs(push.y) * 1.5f;
            Bubble(at + Random.insideUnitSphere * 1.5f, push * Random.Range(1f, 6f) + dive * 4f, Random.Range(0.08f, 0.4f), Random.Range(2f, 4.5f));
        }
        // Big enough to read from the start, 40 m away.
        for (int i = 0; i < 60; i++)
        {
            Vector2 spread = Random.insideUnitCircle;
            Emit(bubbles, at + new Vector3(spread.x, 0f, spread.y) * 2f, new Vector3(spread.x, 0f, spread.y) * 3f,
                Random.Range(1.5f, 3.5f), Random.Range(1.5f, 2.8f), new Color(1f, 1f, 1f, 0.6f));
        }
        flash = new GameObject("Splash flash").AddComponent<Light>();
        flash.transform.SetParent(effects, false);
        flash.transform.position = at;
        flash.type = LightType.Point;
        flash.color = new Color(0.75f, 0.95f, 1f);
        flash.range = 30f;
        flash.shadows = LightShadows.None;
        flashAge = 0f;
        UpdateFlash();
    }

    void UpdateFlash()
    {
        if (flash == null) return;
        flash.intensity = 14f * Mathf.Min(flashAge / 0.05f, Mathf.Exp(-6f * flashAge));
        flashAge += Time.deltaTime;
        if (flashAge > 1f) { Destroy(flash.gameObject); flash = null; }
    }

    void Impact()
    {
        for (int i = 0; i < 70; i++)
            Dust(touchNose + Random.insideUnitSphere * 1.8f, (Random.insideUnitSphere + Vector3.up * 0.8f) * Random.Range(1.5f, 4.5f) + travel * 2f);
        for (int i = 0; i < 80; i++)
            Bubble(touchNose + Random.insideUnitSphere * 1.5f, (Random.insideUnitSphere + Vector3.up) * Random.Range(0.8f, 3f),
                Random.Range(0.05f, 0.25f), Random.Range(2f, 5f));
        // Torn plating flung off the hull. Each sinks into the sand under the dust; the wreck's own debris lies there.
        for (int i = 0; i < 12; i++)
        {
            Vector3 throwing = (Random.insideUnitSphere + Vector3.up * 1.2f + travel * 0.8f).normalized * Random.Range(3f, 6.5f);
            ThrowChunk(touchNose + Vector3.up * 0.5f + Random.insideUnitSphere * 0.8f, throwing, i % 3 == 0 ? scorchedMaterial : hullMaterial);
        }
    }

    void ThrowChunk(Vector3 at, Vector3 velocity, Material material)
    {
        if (material == null) return;
        var piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(piece.GetComponent<Collider>());
        piece.name = "Flung plating";
        piece.transform.SetParent(effects, true);
        Vector3 size = new Vector3(Random.Range(0.15f, 0.5f), Random.Range(0.02f, 0.04f), Random.Range(0.12f, 0.4f));
        piece.transform.SetPositionAndRotation(at, Random.rotation);
        piece.transform.localScale = size;
        var renderer = piece.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        chunks.Add(new Chunk { transform = piece.transform, velocity = velocity, spin = Random.insideUnitSphere * 400f, size = size });
    }

    void UpdateChunks()
    {
        float dt = Time.deltaTime;
        for (int i = chunks.Count - 1; i >= 0; i--)
        {
            Chunk chunk = chunks[i];
            if (chunk.transform == null) { chunks.RemoveAt(i); continue; }
            chunk.age += dt;
            if (chunk.landedAt < 0f)
            {
                // Slowed by the water and sinking.
                chunk.velocity = (chunk.velocity + Vector3.down * (4f * dt)) * Mathf.Exp(-1.5f * dt);
                Vector3 position = chunk.transform.position + chunk.velocity * dt;
                float ground = Ground(position) + 0.03f;
                if (position.y <= ground || chunk.age > 3f) { position.y = Mathf.Max(position.y, ground); chunk.landedAt = chunk.age; }
                chunk.transform.SetPositionAndRotation(position, Quaternion.Euler(chunk.spin * dt) * chunk.transform.rotation);
                continue;
            }
            float sinking = (chunk.age - chunk.landedAt) / 0.6f;
            if (sinking >= 1f) { Destroy(chunk.transform.gameObject); chunks.RemoveAt(i); continue; }
            chunk.transform.localScale = chunk.size * (1f - sinking);
        }
    }

    // ---------- 2: The reboot ----------

    IEnumerator Reboot()
    {
        SetFade(1f);
        bootGroup.alpha = 1f;
        linesGroup.alpha = 1f;
        yield return Wait(0.4f);
        yield return Type(titleText, bootTitle, 0.4f);
        yield return Wait(0.3f);
        for (int i = 0; i < bootLines.Length; i++)
        {
            yield return Type(labelTexts[i], "> " + bootLines[i].label, 0.25f);
            yield return Wait(0.12f);
            statusTexts[i].text = bootLines[i].status;
            statusTexts[i].color = StateColor(bootLines[i].state);
            yield return Wait(0.2f);
        }
        yield return Wait(0.5f);
        for (float t = 0f; t < 0.3f; t += Time.deltaTime)
        {
            linesGroup.alpha = 1f - t / 0.3f;
            Clock += Time.deltaTime;
            yield return null;
        }
        linesGroup.alpha = 0f;
        yield return Glitch(1.6f);
        bootGroup.alpha = 0f;
    }

    static Color StateColor(BootState state) => state == BootState.Fault ? Red : state == BootState.Warning ? Amber : Cyan;

    IEnumerator Wait(float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            Clock += Time.deltaTime;
            yield return null;
        }
    }

    IEnumerator Type(Text text, string value, float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            int shown = Mathf.Clamp(Mathf.CeilToInt(value.Length * t / seconds), 0, value.Length);
            text.text = value.Substring(0, shown) + "_";
            Clock += Time.deltaTime;
            yield return null;
        }
        text.text = value;
    }

    // SIGNAL: LOW stutters in, a red ghost of it shaking beside it, then steadies.
    IEnumerator Glitch(float seconds)
    {
        signal.text = signalGhost.text = signalText;
        float next = 0f;
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            float calm = Window(0.4f, 0.8f, t / seconds);
            if (t >= next)
            {
                next = t + Random.Range(0.03f, 0.08f);
                float jitter = (1f - calm) * 14f;
                signal.rectTransform.anchoredPosition = new Vector2(Random.Range(-jitter, jitter), Random.Range(-jitter, jitter) * 0.3f);
                signalGhost.rectTransform.anchoredPosition = new Vector2(Random.Range(-1f, 1f) * (4f + jitter), 0f);
                signalGhost.color = WithAlpha(Red, (1f - calm) * Random.Range(0.25f, 0.7f));
                signal.color = WithAlpha(Amber, Random.value < (1f - calm) * 0.35f ? 0.25f : 1f);
            }
            signalGroup.alpha = Mathf.Clamp01(Mathf.Min(t / 0.12f, (seconds - t) / 0.3f));
            Clock += Time.deltaTime;
            yield return null;
        }
        signalGroup.alpha = 0f;
    }

    // ---------- 3: The robot emerges ----------

    IEnumerator Emerge()
    {
        Vector3 hatch = ship.TransformPoint(hatchLocal), top = ship.right;
        Vector3 inside = hatch - top * 0.7f, outside = hatch + top * 1.6f + Vector3.up * 0.4f;
        Vector3 bend = Vector3.Lerp(outside, start, 0.5f) + Vector3.up * 0.8f;
        PlaceRobot(inside);
        SetRobotVisible(true);
        // It pushes out in a rush of bubbles.
        for (int i = 0; i < 70; i++)
            Bubble(hatch + Random.insideUnitSphere * 0.5f, (top * 1.5f + Random.insideUnitSphere) * Random.Range(0.5f, 2f),
                Random.Range(0.05f, 0.2f), Random.Range(2f, 4f));
        SetLids(0.5f);
        SetFade(0f);
        float total = riseSeconds + driftSeconds;
        for (float t = 0f; t < total; t += Time.deltaTime)
        {
            Clock = t;
            SetLids(Lids(t));
            scanlines.color = WithAlpha(Color.white, 0.5f * (1f - Window(0.3f, 2f, t)));
            Vector3 position = t < riseSeconds
                ? Vector3.Lerp(inside, outside, EaseOut(t / riseSeconds))
                : Bezier(outside, bend, start, Smooth((t - riseSeconds) / driftSeconds));
            PlaceRobot(position);
            view.SetPositionAndRotation(position + cameraOffset, cameraRotation);
            yield return null;
        }
        PlaceRobot(start);
        view.SetPositionAndRotation(start + cameraOffset, cameraRotation);
        SetLids(0f);
        scanlines.color = WithAlpha(Color.white, 0f);
    }

    // The view opens a crack, shuts, then opens fully.
    static float Lids(float t)
    {
        if (t < 0.25f) return 0.5f;
        if (t < 0.45f) return Mathf.Lerp(0.5f, 0.4f, Smooth((t - 0.25f) / 0.2f));
        if (t < 0.6f) return Mathf.Lerp(0.4f, 0.5f, Smooth((t - 0.45f) / 0.15f));
        return Mathf.Lerp(0.5f, 0f, EaseOut((t - 0.6f) / 0.9f));
    }

    // The HUD flickers on as play begins.
    IEnumerator FlickerHud()
    {
        bool on = false;
        foreach (float hold in new[] { 0.06f, 0.05f, 0.07f, 0.04f })
        {
            on = !on;
            SetCanvases(on);
            for (float t = 0f; t < hold; t += Time.deltaTime) yield return null;
        }
        SetCanvases(true);
    }

    // ---------- Overlay ----------

    // Above the HUD and below the pause menu: scanlines the view opens through, the eyelids, a full-screen fade, and
    // the reboot readout on top of it.
    void BuildOverlay()
    {
        overlay = CreateCanvas("Crash Intro", new Vector2(1280f, 720f), 90);
        // Built while the scene is switching over, so make sure it belongs to the new one.
        SceneManager.MoveGameObjectToScene(overlay, gameObject.scene);
        overlayCanvas = overlay.GetComponent<Canvas>();
        Font heading = LoadFont("Michroma-Regular", this), bodyFont = LoadFont("TitilliumWeb-SemiBold", this);

        scanlines = Stretch(CreateRect("Scanlines", overlay.transform)).gameObject.AddComponent<RawImage>();
        scanlines.raycastTarget = false;
        scanlines.gameObject.AddComponent<HudBackdrop>();
        scanlines.color = WithAlpha(Color.white, 0f);
        topLid = Lid("Top lid");
        bottomLid = Lid("Bottom lid");
        SetLids(0f);
        fade = AddImage(Stretch(CreateRect("Fade", overlay.transform)), null);
        SetFade(1f);

        RectTransform boot = CreateRect("Reboot", overlay.transform);
        boot.anchorMin = boot.anchorMax = boot.pivot = new Vector2(0.5f, 0.5f);
        boot.sizeDelta = new Vector2(620f, 260f);
        bootGroup = boot.gameObject.AddComponent<CanvasGroup>();
        bootGroup.alpha = 0f;
        RectTransform lines = Stretch(CreateRect("Lines", boot));
        linesGroup = lines.gameObject.AddComponent<CanvasGroup>();
        titleText = AddText(Place(CreateRect("Title", lines), 0f, 0f, 620f, 30f), heading, 20, "", Cyan, TextAnchor.MiddleLeft);
        AddImage(Place(CreateRect("Rule", lines), 0f, 38f, 620f, 2f), null).color = WithAlpha(Cyan, 0.5f);
        labelTexts = new Text[bootLines.Length];
        statusTexts = new Text[bootLines.Length];
        for (int i = 0; i < bootLines.Length; i++)
        {
            float y = 54f + i * 34f;
            labelTexts[i] = AddText(Place(CreateRect("Line " + (i + 1), lines), 0f, y, 400f, 30f), bodyFont, 21, "", Ice, TextAnchor.MiddleLeft);
            statusTexts[i] = AddText(Place(CreateRect("Status " + (i + 1), lines), 400f, y, 220f, 30f), heading, 15, "", Cyan, TextAnchor.MiddleRight);
        }

        RectTransform readout = CreateRect("Signal", overlay.transform);
        readout.anchorMin = readout.anchorMax = readout.pivot = new Vector2(0.5f, 0.5f);
        readout.sizeDelta = new Vector2(900f, 120f);
        signalGroup = readout.gameObject.AddComponent<CanvasGroup>();
        signalGroup.alpha = 0f;
        signalGhost = AddText(Stretch(CreateRect("Ghost", readout)), heading, 46, "", Red, TextAnchor.MiddleCenter);
        signal = AddText(Stretch(CreateRect("Readout", readout)), heading, 46, "", Amber, TextAnchor.MiddleCenter);
    }

    RectTransform Lid(string name)
    {
        RectTransform lid = CreateRect(name, overlay.transform);
        AddImage(lid, null).color = Color.black;
        return lid;
    }

    // Each lid covers this fraction of the screen from its edge: 0.5 shuts the view, 0 opens it.
    void SetLids(float closed)
    {
        topLid.anchorMin = new Vector2(0f, 1f - closed);
        topLid.anchorMax = Vector2.one;
        bottomLid.anchorMin = Vector2.zero;
        bottomLid.anchorMax = new Vector2(1f, closed);
        topLid.offsetMin = topLid.offsetMax = bottomLid.offsetMin = bottomLid.offsetMax = Vector2.zero;
    }

    void SetFade(float alpha)
    {
        if (fade != null) fade.color = new Color(0f, 0f, 0f, alpha);
    }

    // ---------- Maths ----------

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude < 1e-6f ? Vector3.forward : v.normalized;
    }

    static float Smooth(float x)
    {
        x = Mathf.Clamp01(x);
        return x * x * (3f - 2f * x);
    }

    static float Window(float from, float to, float x) => Smooth((x - from) / (to - from));

    static float EaseOut(float x)
    {
        x = 1f - Mathf.Clamp01(x);
        return 1f - x * x * x;
    }

    static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t) => Vector3.Lerp(Vector3.Lerp(a, b, t), Vector3.Lerp(b, c, t), t);
}
