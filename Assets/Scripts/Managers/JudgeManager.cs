using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;

public class JudgeManager : MonoBehaviour
{
    public static JudgeManager i;
    int L1_Count = 0;
    int L2_Count = 0;
    int R1_Count = 0;
    int R2_Count = 0;
    KeyCode L1_Key = KeyCode.A;
    KeyCode L2_Key = KeyCode.S;
    KeyCode R1_Key = KeyCode.J;
    KeyCode R2_Key = KeyCode.K;
    [SerializeField] GameObject Active_L1_Short;
    [SerializeField] GameObject Active_L2_Short;
    [SerializeField] GameObject Active_R1_Short;
    [SerializeField] GameObject Active_R2_Short;

    [SerializeField] GameObject Active_L1_Long;
    [SerializeField] GameObject Active_L2_Long;
    [SerializeField] GameObject Active_R1_Long;
    [SerializeField] GameObject Active_R2_Long;
    [SerializeField] float LeftJudgeX;
    [SerializeField] float RightJudgeX;
    [Serializable]
    public class NoteData
    {
        public float startTime;
        public float endTime;
        public SUSHI_TYPE sushi;
        public NOTE_TYPE type;
    }

    [Serializable]
    public class NoteDataList
    {
        public List<NoteData> notes;
    }
    void Awake()
    {
        i = this;
    }
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        InputCheck();
    }
    void InputCheck()
    {
        if(Input.GetKeyDown(L1_Key))
        {
            CheckNearestNote(Active_L1_Short, Active_L1_Long, LeftJudgeX, L1_Key);
        }
        if(Input.GetKeyDown(L2_Key))
        {
            CheckNearestNote(Active_L2_Short, Active_L2_Long, LeftJudgeX, L2_Key);
        }
        if(Input.GetKeyDown(R1_Key))
        {
            CheckNearestNote(Active_R1_Short, Active_R1_Long, RightJudgeX, R1_Key);
        }
        if(Input.GetKeyDown(R2_Key))
        {
            CheckNearestNote(Active_R2_Short, Active_R2_Long, RightJudgeX, R2_Key);
        }
    }
    void CheckNearestNote(GameObject obj1, GameObject obj2, float JudgeX, KeyCode key)
    {
        List<GameObject> obj = new List<GameObject>();
        for(int i = 0; i < obj1.transform.childCount; i++)
        {
            obj.Add(obj1.transform.GetChild(i).gameObject);
        }
        for(int i = 0; i < obj2.transform.childCount; i++)
        {
            obj.Add(obj2.transform.GetChild(i).gameObject);
        }
        if(obj.Count == 0)
        {
            return;
        }
        float NearestDistance;
        int count = 0;
        INote note = obj[0].GetComponent<INote>();
        NearestDistance = Mathf.Abs(note.GetJudgeX() - JudgeX);
        for(int i = 1; i < obj.Count; i++)
        {
            note = obj[i].GetComponent<INote>();
            float distance = Mathf.Abs(note.GetJudgeX() - JudgeX);
            if(distance < NearestDistance)
            {
                NearestDistance = distance;
                count = i;
            }
        }
        note = obj[count].GetComponent<INote>();
        note.NoteCheck(key);
    }
}
