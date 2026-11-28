using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>One save slot, initialized for both title-screen starts and direct Editor play.</summary>
public sealed class SaveGameService : MonoBehaviour
{
    private static SaveGameService instance;
    public static SaveGameService Instance
    {
        get
        {
            if (instance == null)
            {
                instance = new GameObject("Save Game Service").AddComponent<SaveGameService>();
                DontDestroyOnLoad(instance.gameObject);
            }
            return instance;
        }
    }

    public static bool IsPaused { get; private set; }
    private static int pauseChangedFrame = -1;
    public static bool InputBlockedThisFrame => IsPaused || pauseChangedFrame == Time.frameCount;
    public bool IsLoading { get; private set; }
    public string SavePath => Store.FilePath;
    public bool HasSaveFiles => File.Exists(Store.FilePath) || File.Exists(Store.BackupPath);
    /// <summary>True until the autosave made on arriving in an area is written, so intros can wait for it.</summary>
    public bool ArrivalSavePending => saveOnArrival;
    public string LastMessage { get; private set; }
    public event Action<string> Message;
    private SaveGameStore store;
    private SaveGameStore Store => store ?? (store = new SaveGameStore(Application.persistentDataPath));
    private SaveGameData session;
    private SaveGameData pendingLoad;
    private PlayerSaveData travelingPlayer;
    private bool sessionActive;
    private bool saveOnArrival;
    private bool newGameIntroPending;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { instance = null; IsPaused = false; pauseChangedFrame = -1; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap() { _ = Instance; }

    private void Awake() => SceneManager.sceneLoaded += SceneLoaded;

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= SceneLoaded;
        if (instance == this) instance = null;
    }

    public bool CanLoad(out string information)
    {
        if (!Store.TryRead(out var data, out information)) return false;
        if (!Application.CanStreamedLevelBeLoaded(data.currentScene))
        {
            information = "The saved area is unavailable in this build.";
            return false;
        }
        return true;
    }

    public void PrepareNewGame()
    {
        session = NewSession();
        sessionActive = true;
        pendingLoad = null;
        travelingPlayer = null;
        saveOnArrival = true;
        newGameIntroPending = true;
        LastMessage = null;
    }

    /// <summary>
    /// True once after Start Game, for the first area that loads; taking it clears it, so loading a save or
    /// travelling never replays a new-game intro (see ShallowsCrashIntro).
    /// </summary>
    public bool TakeNewGameIntro()
    {
        bool pending = newGameIntroPending;
        newGameIntroPending = false;
        return pending;
    }

    private static SaveGameData NewSession() => new SaveGameData { format = SaveGameStore.Format, version = SaveGameStore.Version };

    public bool TrySave(out string information)
    {
        if (IsLoading)
        {
            information = "Wait until the area has finished loading.";
            return false;
        }
        var player = FindFirstObjectByType<UnderwaterSwimController>();
        if (player == null || !player.enabled)
        {
            information = "There is no active player to save.";
            return false;
        }
        if (session == null) session = NewSession();
        CaptureScene(player);
        session.currentScene = player.gameObject.scene.path;
        sessionActive = true;
        bool saved = Store.TryWrite(session, out information);
        if (saved) information = "Game saved.";
        Report(information);
        return saved;
    }

    public bool TryLoad(out string information)
    {
        if (IsLoading) { information = "An area is already loading."; return false; }
        if (!Store.TryRead(out var data, out information)) { Report(information); return false; }
        if (!Application.CanStreamedLevelBeLoaded(data.currentScene))
        {
            information = "The saved area is unavailable in this build.";
            Report(information);
            return false;
        }
        pendingLoad = data;
        newGameIntroPending = false;
        string recoveryMessage = information;
        if (!LoadScene(data.currentScene, out information)) { pendingLoad = null; return false; }
        information = recoveryMessage ?? "Loading saved game...";
        Report(information);
        return true;
    }

    /// <summary>Captures the departing area's puzzle state, then saves at the destination spawn.</summary>
    public bool TravelTo(string scene, out string information)
    {
        if (!Application.CanStreamedLevelBeLoaded(scene)) { information = "The destination area is unavailable."; return false; }
        if (IsLoading) { information = "An area is already loading."; return false; }
        var player = FindFirstObjectByType<UnderwaterSwimController>();
        if (player != null)
        {
            if (session == null) session = NewSession();
            CaptureScene(player);
            travelingPlayer = player.CaptureSaveState();
            player.GetComponent<AbilityWheel>()?.CaptureSaveState(travelingPlayer);
            sessionActive = true;
        }
        saveOnArrival = true;
        return LoadScene(scene, out information);
    }

    public bool ReturnToTitle(out string information)
    {
        if (!Application.CanStreamedLevelBeLoaded("TitleScreen")) { information = "TitleScreen is unavailable in this build."; return false; }
        return LoadScene("TitleScreen", out information);
    }

    private bool LoadScene(string scene, out string information)
    {
        information = null;
        if (IsLoading) { information = "An area is already loading."; return false; }
        try
        {
            IsLoading = true;
            SetPaused(false);
            SceneManager.LoadSceneAsync(scene);
            return true;
        }
        catch (Exception exception)
        {
            IsLoading = false;
            information = "Could not load the area: " + exception.Message;
            Report(information);
            return false;
        }
    }

    private void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single) StartCoroutine(FinishSceneLoad(scene));
    }

    private IEnumerator FinishSceneLoad(Scene scene)
    {
        // Every Awake and Start (including lights and guardian listeners) must finish before restoration.
        yield return null;
        var player = FindFirstObjectByType<UnderwaterSwimController>();
        if (player == null || player.gameObject.scene != scene)
        {
            IsLoading = false;
            if (pendingLoad != null)
            {
                pendingLoad = null;
                Report("The saved area has no player. Your save file has been kept.");
            }
            yield break;
        }
        bool restoring = pendingLoad != null;
        if (restoring)
        {
            session = pendingLoad;
            pendingLoad = null;
            sessionActive = true;
        }
        var state = session?.scenes.Find(s => s.scenePath == scene.path);
        if (state != null) RestoreScene(player, state, restoring);
        if (!restoring && travelingPlayer != null)
        {
            player.GetComponent<PlayerEnergy>()?.RestoreSavedEnergy(travelingPlayer.energy);
            player.GetComponent<Flashlight>()?.SetOn(travelingPlayer.flashlightOn);
            player.GetComponent<AbilityWheel>()?.RestoreSaveState(travelingPlayer);
        }
        travelingPlayer = null;
        IsLoading = false;
        if (player.GetComponent<GameplaySaveMenu>() == null) player.gameObject.AddComponent<GameplaySaveMenu>();
        if (restoring)
        {
            saveOnArrival = false;
            Report("Saved game loaded.");
        }
        else if (sessionActive && saveOnArrival)
        {
            saveOnArrival = false;
            TrySave(out _);
        }
    }

    private void CaptureScene(UnderwaterSwimController player)
    {
        var scene = player.gameObject.scene;
        var snapshot = new SceneSaveData { scenePath = scene.path, player = player.CaptureSaveState() };
        player.GetComponent<AbilityWheel>()?.CaptureSaveState(snapshot.player);
        player.GetComponent<PlayerInventory>()?.CaptureSaveState(snapshot.player);
        foreach (var item in InScene<PickupItem>(scene)) snapshot.items.Add(item.CaptureSaveState());
        foreach (var guardian in InScene<ShallowsFishBoss>(scene)) snapshot.guardians.Add(guardian.CaptureSaveState());
        session.scenes.RemoveAll(s => s.scenePath == scene.path);
        session.scenes.Add(snapshot);
    }

    private void RestoreScene(UnderwaterSwimController player, SceneSaveData state, bool restorePlayer)
    {
        var scene = player.gameObject.scene;
        var items = InScene<PickupItem>(scene).ToDictionary(SaveIdentity.For);
        var guardians = InScene<ShallowsFishBoss>(scene).ToDictionary(SaveIdentity.For);
        var held = state.items.Find(i => i.held);
        foreach (var itemState in state.items)
            if (items.TryGetValue(itemState.id, out var item)) item.RestoreSaveState(itemState);
        foreach (var guardianState in state.guardians)
            if (guardians.TryGetValue(guardianState.id, out var guardian)) guardian.RestoreSaveState(guardianState);
        // The inventory holds this scene's items, so it comes back whenever the scene does, not only on a load.
        player.GetComponent<PlayerInventory>()?.RestoreSaveState(state.player, items);
        if (restorePlayer)
        {
            player.RestoreSaveState(state.player);
            Physics.SyncTransforms();
            if (held != null && items.TryGetValue(held.id, out var item))
                player.GetComponent<PlayerPickup>()?.RestoreHeldItem(item);
        }
        Physics.SyncTransforms();
    }

    private static IEnumerable<T> InScene<T>(Scene scene) where T : Component =>
        FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(c => c.gameObject.scene == scene);

    public void SetPaused(bool paused)
    {
        if (paused) FindFirstObjectByType<AbilityWheel>()?.CloseForPause();
        IsPaused = paused;
        pauseChangedFrame = Time.frameCount;
        Time.timeScale = paused ? 0f : 1f;
        Cursor.lockState = paused ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = paused;
    }

    private void Report(string information)
    {
        LastMessage = information;
        Message?.Invoke(information);
        if (!string.IsNullOrEmpty(information)) Debug.Log("Save game: " + information);
    }
}
