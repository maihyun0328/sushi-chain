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
}
[Serializable]
public class SongList
{
    public List<SongData> Songs = new List<SongData>();
}
public class SaveManager : MonoBehaviour
{
    public static SaveManager i;
    public void InitSongsData()
    {
        ScoreList list = new ScoreList();
        Score song1 = new Score();
        song1.score = 0;
        song1.Perfect = 0;
        song1.Great = 0;
        song1.Good = 0;
        song1.Bad = 0;
        song1.Miss = 0;

        list.Scores.Add(song1);
        string json = JsonUtility.ToJson(list, true);
        File.WriteAllText(Application.persistentDataPath + "/songs.json", json);
    }
    void Awake()
    {
        i = this;
        string path = Application.persistentDataPath + "/songs.json";
        Debug.Log(Application.persistentDataPath);
        if(!File.Exists(path))
        {
            InitSongsData();
        }
        Debug.Log(File.ReadAllText("Assets/Resources/Songs.json"));
        Debug.Log(File.ReadAllText(Application.persistentDataPath + "/songs.json"));
        string json = File.ReadAllText("Assets/Resources/Songs.json");
        List<SongData> list = JsonUtility.FromJson<List<SongData>>(json);
        Debug.Log(list);
        Debug.Log(TakeSongData(0));
    }

    public Score TakeScoreData(int index)
    {
        string json = File.ReadAllText(Application.persistentDataPath + "/songs.json");
        ScoreList list = JsonUtility.FromJson<ScoreList>(json);
        return list.Scores[index];
    }
    public SongData TakeSongData(int index)
    {
        string json = File.ReadAllText("Assets/Resources/Songs.json");
        SongList list = JsonUtility.FromJson<SongList>(json);
        return list.Songs[index];
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
