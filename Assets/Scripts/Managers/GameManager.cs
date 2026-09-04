using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager i;
    [SerializeField] public GameObject GamePlay;
    public GAME_DIFFICULTY GameDifficulty = GAME_DIFFICULTY.EASY;
    public bool IsGameStart {get; private set;}
    public bool IsGameOver {get; private set;}
    void Awake()
    {
        i = this;
    }
        void Start()
    {
        
    }
    public void GameStart(int index)
    {
        SongList list = JsonManager.i.TakeSongData();
        JsonManager.i.CurrentSongNote = JsonManager.i.TakeSongNoteData(list.Songs[index].Name);
        JsonManager.i.NoteIndex = 0;
        switch(GameDifficulty)
        {
            case GAME_DIFFICULTY.EASY:
                JsonManager.i.speed = 4.5f;
                break;
            case GAME_DIFFICULTY.NORMAL:
                JsonManager.i.speed = 6f;
                break;
            case GAME_DIFFICULTY.HARD:
                JsonManager.i.speed = 7.5f;
                break;
        }
        AudioManager.i.StopSong();
        AudioManager.i.ChangeSong(AudioManager.i.songs[index]);
        AudioManager.i.PlaySong();
        IsGameOver = false;
        IsGameStart = true;
        GamePlay.SetActive(true);
    }
    public void GameOver()
    {
        IsGameStart = false;
        IsGameOver = true;
        AudioManager.i.StopSong();
        AudioManager.i.ChangeSong(AudioManager.i.BGM);
        AudioManager.i.PlaySong();
    }
}
