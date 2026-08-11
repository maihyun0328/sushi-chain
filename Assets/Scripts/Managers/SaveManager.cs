using UnityEngine;
using System;
using System.IO;
using System.Collections.Generic;
using Unity.AppUI.UI;

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
    public void InitScoresData()
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
    public SongList TakeSongData()
    {
        TextAsset file = Resources.Load<TextAsset>("Songs");
        SongList list = JsonUtility.FromJson<SongList>(file.text);
        return list;
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
