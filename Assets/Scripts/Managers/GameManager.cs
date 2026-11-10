using DG.Tweening;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager i;
    [SerializeField] public GameObject GamePlay;
    public GAME_DIFFICULTY GameDifficulty = GAME_DIFFICULTY.EASY;
    public GAME_STATUS GameStatus {get; private set;} = GAME_STATUS.IDLE;
    public int Life;
    public Camera GameCamera;
    public ParticleSystem GameOverVFX;
    public int GameIndex;
    public double InputCalibration = 0;
    public bool IsPaused = false;
    void Awake()
    {
        i = this;
    }
    void Update()
    {
        if(Input.GetKeyDown(KeyCode.W)) AudioManager.i.StopSong();
        if(GameStatus != GAME_STATUS.GAMESTART && GameStatus != GAME_STATUS.TUTORIAL) return;
        if(Input.GetKeyDown(KeyCode.Escape))
        {
            switch(IsPaused)
            {
                case true:
                    ResumeGame();
                    break;
                case false:
                    PauseGame();
                    break;
            }
        }
        if(Life <= 0) 
        {
            GameOver();
            return;
        }
        if(!AudioManager.i.music.isPlaying && !IsPaused) UIManager.i.GameClear();
    }
    public void TutorialGameStart()
    {
        SushiPoolComponent.i.ReturnAllSushi();
        Life = 3;
        JsonManager.i.CurrentSongNote = JsonManager.i.TakeTutorial();
        JsonManager.i.NoteIndex = 0;
        PointManager.i.SetPoint();
        switch(GameDifficulty)
        {
            case GAME_DIFFICULTY.EASY:
                JsonManager.i.speed = 4.5f;
                break;
            case GAME_DIFFICULTY.NORMAL:
                JsonManager.i.speed = 8f;
                break;
            case GAME_DIFFICULTY.HARD:
                JsonManager.i.speed = 11.5f;
                break;
        }
        AudioManager.i.StopSong();
        AudioManager.i.ChangeSong(AudioManager.i.Tutorial);
        AudioManager.i.music.loop = false;
        AudioManager.i.PlaySong();
        GameStatus = GAME_STATUS.TUTORIAL;
        GamePlay.SetActive(true);
    }
    public void GameStart(int index)
    {
        SushiPoolComponent.i.ReturnAllSushi();
        Life = 3;
        GameIndex = index;
        SongList list = JsonManager.i.TakeSongData();
        JsonManager.i.CurrentSongNote = JsonManager.i.TakeSongNoteData(list.Songs[index].Name);
        JsonManager.i.NoteIndex = 0;
        PointManager.i.SetPoint();
        switch(GameDifficulty)
        {
            case GAME_DIFFICULTY.EASY:
                JsonManager.i.speed = 4.5f;
                break;
            case GAME_DIFFICULTY.NORMAL:
                JsonManager.i.speed = 8f;
                break;
            case GAME_DIFFICULTY.HARD:
                JsonManager.i.speed = 11.5f;
                break;
        }
        AudioManager.i.StopSong();
        AudioManager.i.ChangeSong(AudioManager.i.songs[index]);
        AudioManager.i.music.loop = false;
        AudioManager.i.PlaySong();
        GameStatus = GAME_STATUS.GAMESTART;
        GamePlay.SetActive(true);
    }
    public void GameOver()
    {
        GameStatus = GAME_STATUS.GAMEOVER;
        AudioManager.i.StopSong();
        DOVirtual.DelayedCall(1.0f, () =>
        {
            DOVirtual.DelayedCall(0.9f, () =>
            {
                SushiPoolComponent.i.ReturnAllSushi();
                GamePlay.SetActive(false);
                UIManager.i.GamePlay.SetActive(false);
                GameOverVFX.Play();
                GameCamera.transform.DOShakePosition(1f, 6f, 30, 180f, false, true).OnComplete(() =>
                {
                    UIManager.i.GameOver();
                });
            });
        });
    }
    public void GameClear()
    {
        GameStatus = GAME_STATUS.GAMECLEAR;
        SushiPoolComponent.i.ReturnAllSushi();
        AudioManager.i.ChangeSong(AudioManager.i.BGM);
        AudioManager.i.music.loop = true;
        AudioManager.i.PlaySong();
        JsonManager.i.SavePoint(PointManager.i.GetScore(), GameIndex);
    }
    public void CheckFirstLaunch()
    {
        if(JsonManager.i.HasFirstLaunch()) Tutorial();
        else UIManager.i.OnMenu();
    }
    public async void Tutorial()
    {
        YarnSpinnerManager.i.dialogueRunner.gameObject.SetActive(true);
        await YarnSpinnerManager.i.StartConversation("Tutorial");
        JsonManager.i.HasFirstLaunch();
        YarnSpinnerManager.i.dialogueRunner.gameObject.SetActive(false);
        UIManager.i.TutorialGameStart();
    }
    public void PauseGame()
    {
        UIManager.i.PauseUI.SetActive(true);
        AudioManager.i.music.Pause();
        Time.timeScale = 0f;
        IsPaused = true;
    }
    public void ResumeGame()
    {
        UIManager.i.PauseUI.SetActive(false);
        AudioManager.i.music.UnPause();
        Time.timeScale = 1f;
        IsPaused = false;
    }
    public void GameRestart()
    {
        ResumeGame();
        ResetGame();
        UIManager.i.GameStart(GameIndex);
    }
    public void ResetGame()
    {
        GameStatus = GAME_STATUS.IDLE;
    }
    public void ToMainMenu()
    {
        SushiPoolComponent.i.ReturnAllSushi();
        AudioManager.i.ChangeSong(AudioManager.i.BGM);
        ResumeGame();
        ResetGame();
        AudioManager.i.music.loop = true;
        AudioManager.i.PlaySong();
        UIManager.i.Transition(UIManager.i.GamePlay, UIManager.i.Menu, IsMainMenu : true);
    }
}
