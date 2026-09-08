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
    float distance = 8.67f;
    public void InitScoresData()
    {
        ScoreList list = new ScoreList();
        for(int i = 0; i < 8; i++)
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
    void SpawnCheck()
    {
        if(NoteIndex >= CurrentSongNote.Notes.Count) return;
        NoteData note = CurrentSongNote.Notes[NoteIndex];
        if(AudioManager.i.music.time >= GetSpawnTime(note))
        {
            if(note.EndTime == 0) SushiPoolComponent.i.UseSushi(note.type, NOTE_TYPE.SHORT);
            else 
            {
                float length = (CurrentSongNote.Notes[NoteIndex].EndTime - CurrentSongNote.Notes[NoteIndex].StartTime) * speed;
                SushiPoolComponent.i.UseSushi(note.type, NOTE_TYPE.LONG, length);
            }
            NoteIndex++;
        }
        
    }
    float GetSpawnTime(NoteData note)
    {
        float MoveTime = distance / speed;
        return note.StartTime - MoveTime;
    }
}
