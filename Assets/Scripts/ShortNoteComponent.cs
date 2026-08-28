using System.Collections.Generic;
using UnityEngine;

public class ShortNoteComponent : MonoBehaviour, INote
{
    [Header("Note")]
    [SerializeField] NOTE_DIRECT dir;
    [SerializeField] Transform head;
    [SerializeField] Transform tail;
    [SerializeField] SUSHI_TYPE type;

    [Header("Setting")]
    [SerializeField] float speed;
    [SerializeField] float judgeX;
    [SerializeField] float judgeRange;
    [SerializeField] float FailRange;

    void Awake()
    {
        
    }
    void Start()
    {
        
    }
    void Update()
    {
        Move();
        MissCheck();
    }

    void Move()
    {
        switch(dir)
        {
            case NOTE_DIRECT.RIGHT:
                transform.position += Vector3.left * speed * Time.deltaTime;
                break;
            case NOTE_DIRECT.LEFT:
                transform.position += Vector3.right * speed * Time.deltaTime;
                break;
        }
    }

    public void NoteCheck(KeyCode key)
    {
        if (IsInJudgeRange(head.position.x))
        {
            Complete();
            return;
        }
    }
    void MissCheck()
    {
        switch(dir)
        {
            case NOTE_DIRECT.RIGHT:
                // Head가 판정선을 지나쳤는데 입력하지 않음
                if (head.position.x < judgeX - FailRange)
                {
                    Fail();
                }
                break;
            case NOTE_DIRECT.LEFT:
                // Head가 판정선을 지나쳤는데 입력하지 않음
                if (head.position.x > judgeX + FailRange)
                {
                    Fail();
                }
                break;
        }
    }
    public float GetJudgeX()
    {
        return head.position.x;
    }
    bool IsInJudgeRange(float x)
    {
        return Mathf.Abs(x - judgeX) <= judgeRange;
    }

    void Complete()
    {
        SushiPoolComponent.i.ReturnSushi(type, NOTE_TYPE.SHORT, gameObject);
    }

    void Fail()
    {
        SushiPoolComponent.i.ReturnSushi(type, NOTE_TYPE.SHORT, gameObject);
    }
}
