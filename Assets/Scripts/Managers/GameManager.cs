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
    public Camera GameCamera;
    public ParticleSystem GameOverVFX;
    public int GameIndex;
    void Awake()
    {
        i = this;
    }
    void Update()
    {
        if(Input.GetKeyDown(KeyCode.W)) AudioManager.i.StopSong();
        if(!IsGameStart) return;
        if(Life <= 0) 
        {
            GameOver();
            return;
        }
        if(!AudioManager.i.music.isPlaying) UIManager.i.GameClear();
    }
    public void GameStart(int index)
    {
        Life = 3;
        GameIndex = index;
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
        GameCamera.orthographic = false;
        DOVirtual.DelayedCall(1.0f, () =>
        {
            GameCamera.transform.DOMoveZ(-26f, 1f);
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
        IsGameStart = false;
        IsGameClear = true;
        SushiPoolComponent.i.ReturnAllSushi();
        AudioManager.i.ChangeSong(AudioManager.i.BGM);
        AudioManager.i.music.loop = true;
        AudioManager.i.PlaySong();
        JsonManager.i.SavePoint(PointManager.i.GetScore(), GameIndex);
    }
}
