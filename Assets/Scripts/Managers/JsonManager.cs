using UnityEngine;
using System;
using System.IO;
using System.Collections.Generic;

[Serializable]
public class Score
{
    public int score;
    public int Perfect;
    public int Great;
    public int Good;
    public int Bad;
    public int Miss;
}
[Serializable]
public class ScoreList
{
    public List<Score> Scores = new List<Score>();
}
[Serializable]
public class SongData
{
    public string Name;
    public int Difficulty;
    public int Vol;
    public string Album;
    public string Credit;
}
[Serializable]
public class NoteData
{
    public NOTE_DIRECT dir;
    public SUSHI_TYPE type;
    public float StartTime;
    public float EndTime;
}

[Serializable]
public class NoteList
{
    public List<NoteData> Notes = new List<NoteData>();
}
[Serializable]
public class SongList
{
    public List<SongData> Songs = new List<SongData>();
}
public class JsonManager : MonoBehaviour
{
    public static JsonManager i;
    public NoteList CurrentSongNote;
    public int NoteIndex;
    public float speed;
    // 스폰 위치(SushiPoolComponent L1Vector 등, x = ±11)에서 판정선(x = ±1.33)까지의 거리
    public const float SpawnDistance = 9.67f;
    public void InitScoresData()
    {
        ScoreList list = new ScoreList();
        for(int i = 0; i < 1; i++)
        {
            Score song1 = new Score();
            song1.score = 0;
            song1.Perfect = 0;
            song1.Great = 0;
            song1.Good = 0;
            song1.Bad = 0;
            song1.Miss = 0;
            list.Scores.Add(song1);
        }
        string json = JsonUtility.ToJson(list, true);
        File.WriteAllText(Application.persistentDataPath + "/Scores.json", json);
    }
    void Awake()
    {
        i = this;
        string path = Application.persistentDataPath + "/Scores.json";
        if(!File.Exists(path))
        {
            InitScoresData();
        }
        Debug.Log(Application.persistentDataPath);
    }

    public ScoreList TakeScoreData()
    {
        string json = File.ReadAllText(Application.persistentDataPath + "/Scores.json");
        ScoreList list = JsonUtility.FromJson<ScoreList>(json);
        return list;
    }
    public NoteList TakeSongNoteData(string song)
    {
        TextAsset file = Resources.Load<TextAsset>(song);
        NoteList list = JsonUtility.FromJson<NoteList>(file.text);
        return list;
    }
    public SongList TakeSongData()
    {
        TextAsset file = Resources.Load<TextAsset>("Songs");
        SongList list = JsonUtility.FromJson<SongList>(file.text);
        return list;
    }
    void Update()
    {
        if(!GameManager.i.IsGameStart) return;
        SpawnCheck();
    }
    // 판정선에 도착하기 MoveTime초 전에 스폰한다. 같은 프레임에 스폰할 노트(동시치기 등)는 모두 스폰한다.
    // 노트 위치는 시계로 다시 계산하므로, 스폰이 한 프레임 늦어도 판정 시각은 밀리지 않는다.
    void SpawnCheck()
    {
        if(!SongClock.i.IsRunning) return;
        double now = SongClock.i.GameTime;
        double moveTime = MoveTime;
        while(NoteIndex < CurrentSongNote.Notes.Count)
        {
            NoteData note = CurrentSongNote.Notes[NoteIndex];
            if(note.StartTime - now > moveTime) break;
            GameObject obj;
            if(note.EndTime == 0) obj = SushiPoolComponent.i.UseSushi(note.type, NOTE_TYPE.SHORT);
            else
            {
                float length = (note.EndTime - note.StartTime) * speed;
                obj = SushiPoolComponent.i.UseSushi(note.type, NOTE_TYPE.LONG, length);
            }
            if(obj != null) obj.GetComponent<INote>()?.Init(note);
            NoteIndex++;
        }
    }
    // 스폰 위치(x = ±11)에서 판정선(x = ±1.33)까지 가는 시간
    public double MoveTime => SpawnDistance / speed;
    public void SavePoint(Score score, int num)
    {
        ScoreList list = TakeScoreData();
        if(list == null) list = new ScoreList();
        // 곡 수보다 기록이 적으면 빈 기록으로 채운다
        while(list.Scores.Count <= num) list.Scores.Add(new Score());
        list.Scores[num] = score;
        string json = JsonUtility.ToJson(list, true);
        File.WriteAllText(Application.persistentDataPath + "/Scores.json", json);
    }
}
