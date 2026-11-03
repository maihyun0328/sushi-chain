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
    public double InputCalibration = 0;
    // 현재 플레이 중인 커스텀 곡 (내장곡이면 null)
    public CustomSongMeta CurrentCustom { get; private set; }
    AudioClip customClip; // 런타임 로드한 커스텀 곡 오디오 (다음 시작 시 해제)
    void Awake()
    {
        i = this;
    }
    void Update()
    {
        if(!CustomSongInputLock.IsLocked && Input.GetKeyDown(KeyCode.W)) AudioManager.i.StopSong();
        if(!IsGameStart) return;
        if(Life <= 0) 
        {
            GameOver();
            return;
        }
        // 예약 재생 중(시작 전)에는 isPlaying만으로 끝을 판단하지 않는다
        if(SongClock.i.SongTime > 0.5 && !AudioManager.i.music.isPlaying) UIManager.i.GameClear();
    }
    public void GameStart(int index)
    {
        GameIndex = index;
        SongList list = JsonManager.i.TakeSongData();
        NoteList notes = JsonManager.i.TakeSongNoteData(list.Songs[index].Name);
        GameStart(notes, AudioManager.i.songs[index], null);
    }
    // 내장곡/커스텀곡 공통 진입점
    public void GameStart(NoteList notes, AudioClip clip, CustomSongMeta custom)
    {
        CurrentCustom = custom;
        Life = 3;
        JsonManager.i.CurrentSongNote = notes;
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
        AudioManager.i.ChangeSong(clip);
        if(customClip != null && customClip != clip) Destroy(customClip);
        customClip = custom != null ? clip : null;
        AudioManager.i.music.loop = false;
        // dsp 시간축에 예약 재생한다. 첫 노트가 스폰 위치에서 출발할 수 있을 만큼 앞에 여유를 둔다.
        SongClock.i.StartSong(AudioManager.i.music, LeadIn(notes));
        IsGameOver = false;
        IsGameClear = false;
        IsGameStart = true;
        GamePlay.SetActive(true);
    }
    double LeadIn(NoteList notes)
    {
        double first = double.MaxValue;
        if(notes != null && notes.Notes != null)
            foreach(NoteData n in notes.Notes) if(n.StartTime < first) first = n.StartTime;
        if(first == double.MaxValue) first = 0;
        double moveTime = JsonManager.i.MoveTime;
        return System.Math.Min(moveTime, System.Math.Max(0.1, moveTime - first - InputCalibration));
    }
    public void GameOver()
    {
        IsGameStart = false;
        IsGameOver = true;
        SongClock.i.Stop();
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
        IsGameStart = false;
        IsGameClear = true;
        SongClock.i.Stop();
        SushiPoolComponent.i.ReturnAllSushi();
        AudioManager.i.ChangeSong(AudioManager.i.BGM);
        AudioManager.i.music.loop = true;
        AudioManager.i.PlaySong();
        if(CurrentCustom != null) CustomSongStore.SaveScore(CurrentCustom.id, PointManager.i.GetScore());
        else JsonManager.i.SavePoint(PointManager.i.GetScore(), GameIndex);
    }
}
