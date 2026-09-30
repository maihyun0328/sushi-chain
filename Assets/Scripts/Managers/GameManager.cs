using DG.Tweening;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager i;
    [SerializeField] public GameObject GamePlay;
    public GAME_DIFFICULTY GameDifficulty = GAME_DIFFICULTY.EASY;
    public bool IsGameStart {get; private set;}
    public bool IsGameOver {get; private set;}
    public bool IsGameClear {get; private set;}
    public int Life;
    public Camera camera;
    public ParticleSystem GameOverVFX;
    void Awake()
    {
        i = this;
    }
    void Update()
    {
        if(!IsGameStart) return;
        if(Life <= 0) 
        {
            GameOver();
            return;
        }
        if(!AudioManager.i.music.isPlaying) GameClear();
    }
    public void GameStart(int index)
    {
        Life = 3;
        SongList list = JsonManager.i.TakeSongData();
        JsonManager.i.CurrentSongNote = JsonManager.i.TakeSongNoteData(list.Songs[index].Name);
        JsonManager.i.NoteIndex = 0;
        PointManager.i.SetPoint();
        UIManager.i.PointUIInitialization();
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
        IsGameOver = false;
        IsGameClear = false;
        IsGameStart = true;
        GamePlay.SetActive(true);
    }
    public void GameOver()
    {
        IsGameStart = false;
        IsGameOver = true;
        AudioManager.i.StopSong();
        camera.orthographic = false;
        DOVirtual.DelayedCall(1.0f, () =>
        {
            camera.transform.DOMoveZ(-26f, 1f);
            DOVirtual.DelayedCall(0.75f, () =>
            {
                SushiPoolComponent.i.ReturnAllSushi();
                GamePlay.SetActive(false);
                UIManager.i.GamePlay.SetActive(false);
                GameOverVFX.Play();
                camera.transform.DOShakePosition(1f, 6f, 30, 180f, false, true);
            });
        });
    }
    public void GameClear()
    {
        IsGameStart = false;
        IsGameClear = true;
        AudioManager.i.ChangeSong(AudioManager.i.BGM);
        AudioManager.i.music.loop = true;
        AudioManager.i.PlaySong();
    }
}
