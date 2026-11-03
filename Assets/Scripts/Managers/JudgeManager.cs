using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;

public class JudgeManager : MonoBehaviour
{
    public static JudgeManager i;
    public KeyCode L1_Key = KeyCode.Q;
    public KeyCode L2_Key = KeyCode.A;
    public KeyCode R1_Key = KeyCode.P;
    public KeyCode R2_Key = KeyCode.L;
    [SerializeField] GameObject Active_L1_Short;
    [SerializeField] GameObject Active_L2_Short;
    [SerializeField] GameObject Active_R1_Short;
    [SerializeField] GameObject Active_R2_Short;

    [SerializeField] GameObject Active_L1_Long;
    [SerializeField] GameObject Active_L2_Long;
    [SerializeField] GameObject Active_R1_Long;
    [SerializeField] GameObject Active_R2_Long;
    [SerializeField] float LeftJudgeX;   // 시간 기반 판정으로 바뀐 뒤로는 쓰지 않는다 (씬 직렬화 유지용)
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
        if(GameManager.i.IsGameOver || !GameManager.i.IsGameStart) return;
        InputCheck();
    }
    void InputCheck()
    {
        CheckLane(L1_Key, Active_L1_Short, Active_L1_Long);
        CheckLane(L2_Key, Active_L2_Short, Active_L2_Long);
        CheckLane(R1_Key, Active_R1_Short, Active_R1_Long);
        CheckLane(R2_Key, Active_R2_Short, Active_R2_Long);
    }
    void CheckLane(KeyCode key, GameObject shortParent, GameObject longParent)
    {
        if(!TimedKeyInput.GetKeyDown(key, out double realtime)) return;
        // 프레임 시각이 아니라 키가 실제로 눌린 시각으로 판정한다
        double inputTime = SongClock.i.GameTimeAt(realtime);
        CheckNearestNote(shortParent, longParent, key, inputTime);
    }
    // 노트 시각이 입력 시각에 가장 가까운 노트 하나를 판정한다
    void CheckNearestNote(GameObject obj1, GameObject obj2, KeyCode key, double inputTime)
    {
        INote nearest = null;
        double nearestGap = double.MaxValue;
        FindNearest(obj1.transform, inputTime, ref nearest, ref nearestGap);
        FindNearest(obj2.transform, inputTime, ref nearest, ref nearestGap);
        if(nearest != null) nearest.NoteCheck(key, inputTime);
    }
    static void FindNearest(Transform parent, double inputTime, ref INote nearest, ref double nearestGap)
    {
        for(int i = 0; i < parent.childCount; i++)
        {
            INote note = parent.GetChild(i).GetComponent<INote>();
            double? judgeTime = note?.GetJudgeTime();
            if(!judgeTime.HasValue) continue;
            double gap = Math.Abs(judgeTime.Value - inputTime);
            if(gap < nearestGap)
            {
                nearestGap = gap;
                nearest = note;
            }
        }
    }
    public void SetKey(SUSHI_TYPE type, KeyCode key)
    {
        switch(type)
        {
            case SUSHI_TYPE.L1:
                L1_Key = key;
                break;
            case SUSHI_TYPE.L2:
                L2_Key = key;
                break;
            case SUSHI_TYPE.R1:
                R1_Key = key;
                break;
            case SUSHI_TYPE.R2:
                R2_Key = key;
                break;
            
        }
    }
}
