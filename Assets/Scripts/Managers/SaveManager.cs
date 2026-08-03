using UnityEngine;
using System;
using System.IO;
using System.Collections.Generic;

[Serializable]
public class SongData
{
    public string Name;
    public int Difficulty;
}
[Serializable]
public class SongList
{
    public List<SongData> songs = new List<SongData>();
}

public class SaveManager : MonoBehaviour
{
    public static SaveManager i;
    public void InitSongsData()
    {
        SongList list = new SongList();
        SongData song1 = new SongData();
        song1.Name = "와";
        song1.Difficulty = 3;

        list.songs.Add(song1);
        string json = JsonUtility.ToJson(list, true);
        File.WriteAllText(Application.persistentDataPath + "/songs.json", json);
    }
    void Awake()
    {
        i = this;
        string path = Application.persistentDataPath + "/songs.json";
        if(!File.Exists(path))
        {
            InitSongsData();
        }
    }

    public SongData TakeSongData(int index)
    {
        string json = File.ReadAllText(Application.persistentDataPath + "/songs.json");
        SongList list = JsonUtility.FromJson<SongList>(json);
        return list.songs[index];
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
