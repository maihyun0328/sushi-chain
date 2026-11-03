using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class KeyboardScrollComponent : MonoBehaviour
{
    int MinimumIndex = 0;
    int index = 0;
    int MaximumIndex = 4;
    int SongsCount;
    SongList SongsInfo;
    ScoreList ScoreInfo;
    float speed = 5f;
    [SerializeField] Text SongName;
    [SerializeField] Text Point;
    [SerializeField] GameObject SongPrefab;
    [SerializeField] GameObject Arrow;
    [SerializeField] GameObject UpArrow;
    [SerializeField] GameObject DownArrow;
    Vector2 ContentTargetPosition = Vector2.zero;
    Vector2 ArrowTargetPosition;
    [SerializeField] List<GameObject> Songs = new List<GameObject>();
    // 추가: Scroll View의 ScrollRect
    [SerializeField]
    ScrollRect scrollRect;

    // 추가: Scroll View/Viewport
    [SerializeField]
    RectTransform viewport;

    // 추가: Scroll View/Viewport/Content
    [SerializeField]
    RectTransform content;
    // 내장곡 + 커스텀곡 통합 목록 항목
    class SongEntry
    {
        public string Name;
        public int Difficulty;
        public int Score;
        public bool IsCustom;
        public int BuiltinIndex;     // 내장곡일 때 Songs.json 인덱스
        public CustomSongMeta Meta;  // 커스텀곡일 때만 유효
    }
    readonly List<SongEntry> entries = new List<SongEntry>();
    readonly List<GameObject> spawnedItems = new List<GameObject>();
    const int VisibleCount = 5;
    const float RowHeight = 130f;
    Vector2 ArrowBasePosition;
    bool started = false;
    // 커스텀 곡 오디오 로드 중이면 true. BackComponent가 이 동안 Esc를 무시한다
    public static bool IsLoadingCustom { get; private set; }
    Coroutine loadRoutine;

    void Start()
    {
        ArrowBasePosition = Arrow.transform.position;
        ArrowTargetPosition = ArrowBasePosition;
        started = true;
        ReloadSongs();
    }
    void OnEnable()
    {
        // 최초 활성화 시에는 OnEnable이 Start보다 먼저 호출된다. 목록 생성은 Start에 맡긴다.
        IsLoadingCustom = false;
        if(!started) return;
        ReloadSongs();
    }
    // 곡 목록 UI를 다시 만든다 (커스텀 곡 추가 후, 점수 갱신 시 호출)
    public void ReloadSongs()
    {
        if(!started) return;
        BuildEntries();
        foreach(GameObject item in spawnedItems)
        {
            if(item == null) continue;
            Songs.Remove(item);
            item.SetActive(false);
            item.transform.SetParent(null, false);
            Destroy(item);
        }
        spawnedItems.Clear();
        SongsCount = entries.Count;
        for(int i = 0; i < SongsCount; i++)
        {
            GameObject Song = Instantiate(SongPrefab, content);
            spawnedItems.Add(Song);
            Songs.Add(Song);
            Song.transform.GetChild(0).GetComponent<Text>().text = entries[i].Name;
            for(int j = 1; j < 6; j++)
            {
                Image star = Song.transform.GetChild(j).GetComponent<Image>();
                if(j > entries[i].Difficulty) star.color = Color.black;
            }
        }
        ClampIndex();
        SetSong();
    }
    void BuildEntries()
    {
        entries.Clear();
        SongsInfo = JsonManager.i.TakeSongData();
        ScoreInfo = JsonManager.i.TakeScoreData();
        int scoreCount = (ScoreInfo != null && ScoreInfo.Scores != null) ? ScoreInfo.Scores.Count : 0;
        for(int i = 0; i < SongsInfo.Songs.Count; i++)
        {
            // Scores.json 기록이 곡 수보다 적으면 0점으로 취급
            Score sc = i < scoreCount ? ScoreInfo.Scores[i] : null;
            entries.Add(new SongEntry
            {
                Name = SongsInfo.Songs[i].Name,
                Difficulty = SongsInfo.Songs[i].Difficulty,
                Score = sc != null ? sc.score : 0,
                IsCustom = false,
                BuiltinIndex = i,
            });
        }
        List<CustomSongMeta> customs = null;
        try { customs = CustomSongStore.GetSongs(); }
        catch(System.Exception e) { Debug.LogError("[KeyboardScroll] 커스텀 곡 목록 로드 실패: " + e.Message); }
        if(customs == null) return;
        foreach(CustomSongMeta meta in customs)
        {
            if(meta == null || string.IsNullOrEmpty(meta.id)) continue;
            entries.Add(new SongEntry
            {
                Name = string.IsNullOrEmpty(meta.title) ? meta.id : meta.DisplayName,
                Difficulty = ChartDifficulty.ToStars(meta.difficulty),
                Score = meta.score != null ? meta.score.score : 0,
                IsCustom = true,
                BuiltinIndex = -1,
                Meta = meta,
            });
        }
    }
    // 목록 크기가 바뀌었을 때 index / 스크롤 창 / 화살표 위치를 다시 맞춘다
    void ClampIndex()
    {
        index = Mathf.Clamp(index, 0, Mathf.Max(0, SongsCount - 1));
        if(index < MinimumIndex) MinimumIndex = index;
        if(index > MinimumIndex + VisibleCount - 1) MinimumIndex = index - (VisibleCount - 1);
        // 목록이 줄어 아래가 비면 창을 위로 당긴다
        while(MinimumIndex > 0 && MinimumIndex + VisibleCount - 1 > SongsCount - 1) MinimumIndex -= 1;
        MaximumIndex = MinimumIndex + VisibleCount - 1;
        ContentTargetPosition = new Vector2(0, MinimumIndex * RowHeight);
        ArrowTargetPosition = ArrowBasePosition - new Vector2(0, (index - MinimumIndex) * RowHeight);
    }
    void Update()
    {
        Move();
        ArrowCheck();
        if(CustomSongInputLock.IsLocked) return; // URL 입력 중에는 키 입력 무시
        GameStartCheck();
        if((Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow)) && index != 0)
        {
            if(index == MinimumIndex)
            {
                MinimumIndex -= 1;
                MaximumIndex -= 1;
                ScrollMove(-130f);
            }
            else
            {
                ArrowTargetPosition += new Vector2(0, 130f);
            }
            index -= 1;
            SetSong();
        }
        if((Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow)) && index < SongsCount - 1)
        {
            if(index == MaximumIndex)
            {
                MinimumIndex += 1;
                MaximumIndex += 1;
                ScrollMove(130);
            }
            else
            {
                ArrowTargetPosition -= new Vector2(0, 130f);
            }
            index += 1;
            SetSong();
        }
    }
    void ScrollMove(float ScrollAmount)
    {
        ContentTargetPosition += new Vector2(0, ScrollAmount);
    }
    void Move()
    {
        if(Vector2.Distance(content.anchoredPosition, ContentTargetPosition) < 0.1f) 
        {
            content.anchoredPosition = ContentTargetPosition;
        }
        else 
        {
            content.anchoredPosition = Vector2.Lerp(content.anchoredPosition, ContentTargetPosition, Time.deltaTime * speed);
        }
        if(Vector2.Distance(Arrow.transform.position, ArrowTargetPosition) < 0.1f)
        {
            Arrow.transform.position = ArrowTargetPosition;
        }
        else
        {
            Arrow.transform.position = Vector2.Lerp(Arrow.transform.position, ArrowTargetPosition, Time.deltaTime * speed);
        }
    }
    void ArrowCheck()
    {
        if(MinimumIndex == 0)
        {
            UpArrow.SetActive(false);
        }
        else
        {
            UpArrow.SetActive(true);
        }
        if(MaximumIndex == SongsCount - 1 || SongsCount < 7)
        {
            DownArrow.SetActive(false);
        }
        else
        {
            DownArrow.SetActive(true);
        }
    }
    void GameStartCheck()
    {
        if(!Input.GetKeyDown(KeyCode.Return)) return;
        if(IsLoadingCustom || UIManager.i.IsInTransition) return; // 중복 시작 방지
        if(index < 0 || index >= entries.Count) return;
        SongEntry entry = entries[index];
        if(!entry.IsCustom)
        {
            UIManager.i.GameStart(entry.BuiltinIndex);
            return;
        }
        StartCustomSong(entry.Meta);
    }
    void StartCustomSong(CustomSongMeta meta)
    {
        NoteList chart = null;
        try { chart = CustomSongStore.LoadChart(meta); }
        catch(System.Exception e) { Debug.LogError("[KeyboardScroll] 채보 로드 실패: " + meta.id + " / " + e.Message); }
        if(chart == null || chart.Notes == null || chart.Notes.Count == 0)
        {
            Debug.LogError("[KeyboardScroll] 채보가 비어 있음: " + meta.id);
            return;
        }
        IsLoadingCustom = true;
        loadRoutine = StartCoroutine(CustomSongStore.LoadAudio(meta, clip =>
        {
            IsLoadingCustom = false;
            loadRoutine = null;
            if(clip == null)
            {
                Debug.LogError("[KeyboardScroll] 오디오가 null: " + meta.id);
                return;
            }
            // 화면이 이미 닫혔거나 시작이 거부되면 clip을 직접 해제한다 (누수 방지)
            if(!isActiveAndEnabled || !UIManager.i.GameStartCustom(meta, chart, clip))
            {
                Destroy(clip);
            }
        }, err =>
        {
            IsLoadingCustom = false;
            loadRoutine = null;
            Debug.LogError("[KeyboardScroll] 오디오 로드 실패: " + meta.id + " / " + err);
        }));
    }
    void OnDisable()
    {
        // 로드 중에 화면이 꺼지면 콜백이 오지 않도록 중단한다
        if(loadRoutine != null)
        {
            StopCoroutine(loadRoutine);
            loadRoutine = null;
        }
        IsLoadingCustom = false;
    }
    void SetSong()
    {
        if(index < 0 || index >= entries.Count)
        {
            SongName.text = "";
            Point.text = "0";
            return;
        }
        SongName.text = entries[index].Name;
        Point.text = entries[index].Score.ToString("N0");
    }
}
