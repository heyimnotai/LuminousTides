using UnityEngine;
using UnityEngine.SceneManagement;

/// Owns the overall game state: the main menu, playing, paused, dead (respawning) and mission complete.
/// The game is frozen (time scale 0) and the cursor freed whenever a menu is showing.
public class GameManager : MonoBehaviour
{
    public enum GameState { Menu, Playing, Paused, Dead, Won }
    public static GameManager Instance { get; private set; }

    public PlayerController player;
    public Health playerHealth;
    public Transform spawnPoint;
    public Transform rangeSpawn, worldSpawn, rangeObjective, demoSpawn, demoObjective;      // where each way of playing begins
    /// Where the game is being played: the combat test range, or the world with its mission. Kept across restarts.
    public enum PlayMode { Range, World, Demo }
    public static PlayMode Mode = PlayMode.Range;
    public Transform objective;
    public string objectiveText = "PUSH THROUGH TO THE ARCH";
    public float respawnDelay = 3f;

    public GameState State { get; private set; } = GameState.Menu;
    public int Kills { get; private set; }
    public int PrecisionKills { get; private set; }
    public int Deaths { get; private set; }
    public int Score { get; private set; }
    public float MissionTime { get; private set; }
    /// S for a deathless run under nine minutes, then A, B, C by deaths.
    public string Rank => Deaths == 0 && MissionTime < 540f ? "S" : Deaths <= 1 ? "A" : Deaths <= 3 ? "B" : "C";
    /// Raised for things worth calling out on screen: text and the points awarded.
    public event System.Action<string, int> Callout;
    float lastKillTime = -99f;
    int streak;
    float stateTime;
    /// Set before reloading the scene to go straight back into the mission instead of the menu.
    static bool skipMenu;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        playerHealth.Died += () => { if (State == GameState.Playing) { Deaths++; SetState(GameState.Dead); } };
        // The scripted smoke test (which blocks real input) and "restart mission" go straight into play.
        if (InputGate.Blocked) Mode = PlayMode.World;          // the smoke test plays the mission
        if (skipMenu || InputGate.Blocked) { skipMenu = false; BeginPlay(); Invoke(nameof(EnterMode), 0f); }
        else EnterMenu();
    }

    public void EnterMenu()
    {
        SetState(GameState.Menu);
        Time.timeScale = 0f;
        LockCursor(false);
    }

    public void StartMission() { if (State != GameState.Menu) return; Mode = PlayMode.World; BeginPlay(); EnterMode(); }
    public void StartRange() { if (State != GameState.Menu) return; Mode = PlayMode.Range; BeginPlay(); EnterMode(); }
    public void StartDemo() { if (State != GameState.Menu) return; Mode = PlayMode.Demo; BeginPlay(); EnterMode(); }

    /// Puts the player at the start of whichever way of playing was chosen.
    void EnterMode()
    {
        var start = Mode == PlayMode.Range ? rangeSpawn : Mode == PlayMode.Demo ? demoSpawn : worldSpawn;
        if (start != null)
        {
            spawnPoint.position = start.position;
            player.Teleport(start.position, 0f);
        }
        if (Mode == PlayMode.World) return;
        if (MissionDirector.Instance != null) MissionDirector.Instance.enabled = false;      // no mission out here
        objective = Mode == PlayMode.Demo ? demoObjective : rangeObjective;
        objectiveText = Mode == PlayMode.Demo ? "REACH THE CROWN" : "COMBAT TEST RANGE";
    }

    void BeginPlay()
    {
        SetState(GameState.Playing);
        Time.timeScale = 1f;
        LockCursor(true);
    }

    public void Pause()
    {
        if (State != GameState.Playing) return;
        SetState(GameState.Paused);
        Time.timeScale = 0f;
        LockCursor(false);
    }

    public void Resume()
    {
        if (State != GameState.Paused) return;
        BeginPlay();
    }

    public void RestartMission() { Reload(true); }
    public void QuitToMenu() { Reload(false); }
    public void QuitGame() { Application.Quit(); }

    void Reload(bool straightIntoPlay)
    {
        skipMenu = straightIntoPlay;
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void RegisterKill(EnemyAI enemy = null)
    {
        Kills++;
        int points = enemy == null ? 100 : enemy.kind == EnemyKind.Boss ? 2500 : enemy.kind == EnemyKind.Champion ? 1200 : enemy.elite ? 300
                   : enemy.kind == EnemyKind.Spitter ? 120 : enemy.kind == EnemyKind.Burster ? 60 : 100;
        Score += points;
        // Kills in quick succession build a streak.
        streak = Time.time - lastKillTime < 3.2f ? streak + 1 : 1;
        lastKillTime = Time.time;
        if (streak >= 2)
        {
            int bonus = 50 * Mathf.Min(streak, 8);      // capped, so one long fight cannot swamp the score
            Score += bonus;
            Callout?.Invoke(streak == 2 ? "DOUBLE KILL" : streak == 3 ? "TRIPLE KILL" : streak == 4 ? "OVERKILL" : "RAMPAGE  x" + streak, bonus);
        }
    }

    public void RegisterPrecisionKill()
    {
        PrecisionKills++;
        Score += 50;
        Callout?.Invoke("PRECISION", 50);
    }

    public void ObjectiveReached()
    {
        if (State == GameState.Won) return;
        MissionTime = Time.timeSinceLevelLoad;
        SetState(GameState.Won);
        LockCursor(false);          // free the mouse for the mission-complete buttons
    }

    /// From the mission-complete screen: back into the world, with the mission behind you.
    public void KeepExploring()
    {
        if (State != GameState.Won) return;
        objective = null;
        objectiveText = "EXPLORE";
        BeginPlay();
    }

    void SetState(GameState s) { State = s; stateTime = Time.time; }

    static void LockCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    void OnApplicationFocus(bool focused)
    {
        // Alt-tabbing away pauses rather than leaving the player standing in a fight.
        if (!focused && !InputGate.Blocked) Pause();
    }

    void Update()
    {
        if (State == GameState.Dead && Time.time - stateTime >= respawnDelay)
        {
            player.Teleport(spawnPoint.position, spawnPoint.eulerAngles.y);
            playerHealth.ResetFull();
            SetState(GameState.Playing);
        }
        if (State == GameState.Won && Input.GetKeyDown(KeyCode.R) && !InputGate.Blocked) RestartMission();
    }
}
