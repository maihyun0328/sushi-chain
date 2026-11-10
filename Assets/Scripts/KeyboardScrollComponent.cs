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
    const float SPEED = 5f;
    const int SCROLL_STEP = 130;
    const float OFFSET = 0.1f;
    const int STAR_COUNT = 5;
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
    void Start()
    {
        SongsInfo = JsonManager.i.TakeSongData();
        ScoreInfo = JsonManager.i.TakeScoreData();
        SongsCount = SongsInfo.Songs.Count;
        for(int i = 0; i < SongsCount; i++)
        {
            GameObject Song = Instantiate(SongPrefab, content);
            Image[] Stars = new Image[STAR_COUNT];
            Songs.Add(Song);
            Song.transform.GetChild(0).GetComponent<Text>().text = SongsInfo.Songs[i].Name;
            for(int j = 1; j <= STAR_COUNT; j++)
            {
                Stars[j - 1] = Song.transform.GetChild(j).GetComponent<Image>();
                if(j > SongsInfo.Songs[i].Difficulty) Stars[j - 1].color = Color.black;
            }
        }
        ArrowTargetPosition = Arrow.transform.position;
        SetSong();
    }
    void OnEnable()
    {
        SongsInfo = JsonManager.i.TakeSongData();
        ScoreInfo = JsonManager.i.TakeScoreData();
        SetSong();
    }
    void Update()
    {
        GameStartCheck();
        Move();
        ArrowCheck();
        if((Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow)) && index != 0)
        {
            if(index == MinimumIndex)
            {
                MinimumIndex--;
                MaximumIndex--;
                ScrollMove(-SCROLL_STEP);
            }
            else
            {
                ArrowTargetPosition += new Vector2(0, SCROLL_STEP);
            }
            index--;
            AudioManager.i.PlayUI(AudioManager.i.SongScroll);
            SetSong();
        }
        if((Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow)) && index != SongsCount - 1)
        {
            if(index == MaximumIndex)
            {
                MinimumIndex++;
                MaximumIndex++;
                ScrollMove(SCROLL_STEP);
            }
            else
            {
                ArrowTargetPosition -= new Vector2(0, SCROLL_STEP);
            }
            index++;
            AudioManager.i.PlayUI(AudioManager.i.SongScroll);
            SetSong();
        }
    }
    void ScrollMove(float ScrollAmount)
    {
        ContentTargetPosition += new Vector2(0, ScrollAmount);
    }
    void Move()
    {
        if(Vector2.Distance(content.anchoredPosition, ContentTargetPosition) < OFFSET) 
        {
            content.anchoredPosition = ContentTargetPosition;
        }
        else 
        {
            content.anchoredPosition = Vector2.Lerp(content.anchoredPosition, ContentTargetPosition, Time.deltaTime * SPEED);
        }
        if(Vector2.Distance(Arrow.transform.position, ArrowTargetPosition) < OFFSET)
        {
            Arrow.transform.position = ArrowTargetPosition;
        }
        else
        {
            Arrow.transform.position = Vector2.Lerp(Arrow.transform.position, ArrowTargetPosition, Time.deltaTime * SPEED);
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
        if(Input.GetKeyDown(KeyCode.Return))
        {
            AudioManager.i.PlayUI(AudioManager.i.Choice);
            UIManager.i.GameStart(index);
        }
    }
    void SetSong()
    {
        SongName.text = SongsInfo.Songs[index].Name;
        Point.text = ScoreInfo.Scores[index].score.ToString("N0");
    }
}
