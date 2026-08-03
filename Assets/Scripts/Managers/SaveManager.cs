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
    public List<Score> songs = new List<Score>();
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

        list.songs.Add(song1);
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
    }

    public Score TakeSongData(int index)
    {
        string json = File.ReadAllText(Application.persistentDataPath + "/songs.json");
        ScoreList list = JsonUtility.FromJson<ScoreList>(json);
        return list.songs[index];
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
