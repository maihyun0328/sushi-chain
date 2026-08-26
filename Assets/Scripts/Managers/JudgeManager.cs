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
    [SerializeField] GameObject Active_L1;
    [SerializeField] GameObject Active_L2;
    [SerializeField] GameObject Active_R1;
    [SerializeField] GameObject Active_R2;
    [SerializeField] float LeftJudgeX;
    [SerializeField] float RightJudgeX;
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
            CheckNearestNote(Active_L1, LeftJudgeX, L1_Key);
        }
        if(Input.GetKeyDown(L2_Key))
        {
            CheckNearestNote(Active_L2, LeftJudgeX, L2_Key);
        }
        if(Input.GetKeyDown(R1_Key))
        {
            CheckNearestNote(Active_R1, RightJudgeX, R1_Key);
        }
        if(Input.GetKeyDown(R2_Key))
        {
            CheckNearestNote(Active_R2, RightJudgeX, R2_Key);
        }
    }
    void CheckNearestNote(GameObject obj, float JudgeX, KeyCode key)
    {
        if(obj.transform.childCount == 0)
        {
            return;
        }
        float NearestDistance;
        int count = 0;
        INote note = obj.transform.GetChild(0).GetComponent<INote>();
        NearestDistance = Mathf.Abs(note.GetJudgeX() - JudgeX);
        for(int i = 1; i < obj.transform.childCount; i++)
        {
            note = obj.transform.GetChild(i).GetComponent<INote>();
            float distance = Mathf.Abs(note.GetJudgeX() - JudgeX);
            if(distance < NearestDistance)
            {
                NearestDistance = distance;
                count = i;
            }
        }
        note = obj.transform.GetChild(count).GetComponent<INote>();
        note.NoteCheck(key);
    }
}
