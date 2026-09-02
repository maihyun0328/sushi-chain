using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

public class KeyboardScrollComponent : MonoBehaviour
{
    int MinimumIndex = 0;
    int index = 0;
    int MaximumIndex = 5;
    int SongsCount;
    SongList SongsInfo;
    ScoreList ScoreInfo;
    float speed = 5f;
    [SerializeField] Text SongName;
    [SerializeField] Text Point;
    [SerializeField] Text Album;
    [SerializeField] Text Credit;
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
            Image[] Stars = new Image[5];
            Songs.Add(Song);
            Song.transform.GetChild(0).GetComponent<Text>().text = SongsInfo.Songs[i].Name;
            for(int j = 1; j < 6; j++)
            {
                Stars[j - 1] = Song.transform.GetChild(j).GetComponent<Image>();
                if(j > SongsInfo.Songs[i].Difficulty) Stars[j - 1].color = Color.black;
            }
        }
        ArrowTargetPosition = Arrow.transform.position;
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
        if((Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow)) && index != SongsCount - 1)
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
        if(Input.GetKeyDown(KeyCode.Return))
        {
            GameManager.i.GameStart();
            UIManager.i.GameStart();
            UIManager.i.Songs.SetActive(false);
        }
    }
    void SetSong()
    {
        SongName.text = SongsInfo.Songs[index].Name;
        Point.text = ScoreInfo.Scores[index].score.ToString();
        Album.text = SongsInfo.Songs[index].Album;
        Credit.text = SongsInfo.Songs[index].Credit;
    }
}
