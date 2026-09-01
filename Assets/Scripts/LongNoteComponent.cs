using System.Collections.Generic;
using UnityEngine;

public class LongNoteComponent : MonoBehaviour, INote
{
    [Header("Note")]
    [SerializeField] NOTE_DIRECT dir;
    [SerializeField] Transform head;
    [SerializeField] Transform body;
    [SerializeField] Transform tail;
    [SerializeField] SUSHI_TYPE type;

    [Header("Setting")]
    public float speed;
    [SerializeField] float judgeX;
    [SerializeField] float judgeRange;
    [SerializeField] float FailRange;

    [SerializeField] KeyCode inputKey;

    NOTE_STATE state = NOTE_STATE.WAITING;
    List<Renderer> rends = new List<Renderer>();

    void Awake()
    {
        for(int i = 0; i < transform.childCount; i++)
        {
            rends.Add(transform.GetChild(i).GetComponent<Renderer>());
        }
    }
    void Start()
    {
        
    }
    void Update()
    {
        Move();
        switch(state)
        {
            case NOTE_STATE.WAITING:
                MissCheck(head.position.x);
                break;
            case NOTE_STATE.HOLDING:
                CheckHolding();
                break;
        }
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
        inputKey = key;
        if (IsInJudgeRange(head.position.x))
        {
            Debug.Log("Head 성공");
            head.gameObject.SetActive(false);
            state = NOTE_STATE.HOLDING;
            return;
        }
    }

    void CheckHolding()
    {
        SetBody();
        // 키를 떼었을 때
        if (Input.GetKeyUp(inputKey))
        {
            // Tail이 판정선 근처인지 확인
            if (IsInJudgeRange(tail.position.x))
            {
                Complete();
            }
            else
            {
                // 너무 일찍 떼었다
                Fail();
            }
            return;
        }
        MissCheck(tail.transform.position.x);
    }
    void MissCheck(float NoteX)
    {
        switch(dir)
        {
            case NOTE_DIRECT.RIGHT:
                // Tail이 판정선을 완전히 지나갔는데도 안 뗐음
                if (NoteX < judgeX - judgeRange)
                {
                    Fail();
                }
                break;
            case NOTE_DIRECT.LEFT:
                // Tail이 판정선을 완전히 지나갔는데도 안 뗐음
                if (NoteX > judgeX + judgeRange)
                {
                    Fail();
                }
                break;
        }
    }

    bool IsInJudgeRange(float x)
    {
        return Mathf.Abs(x - judgeX) <= judgeRange;
    }
    public float GetJudgeX()
    {
        return head.position.x;
    }
    void Complete()
    {
        state = NOTE_STATE.COMPLETED;

        SushiPoolComponent.i.ReturnSushi(type, NOTE_TYPE.LONG, gameObject);
    }

    void Fail()
    {
        state = NOTE_STATE.FAILED;
        PointManager.i.Miss += 1;
        SushiPoolComponent.i.ReturnSushi(type, NOTE_TYPE.LONG, gameObject);
    }
    void SetBody()
    {
        Vector3 scale = body.transform.localScale;
        switch(dir)
        {
            case NOTE_DIRECT.LEFT:
                if(tail.position.x - judgeX < 0)
                    scale.z = Mathf.Abs(tail.position.x - judgeX) * 100;
                break;
            case NOTE_DIRECT.RIGHT:
                if(tail.position.x - judgeX > 0)
                    scale.z = Mathf.Abs(tail.position.x - judgeX) * 100;
                break;
        }
        body.transform.localScale = scale;
        Vector3 pos = body.position;
        pos.x = (tail.position.x + judgeX) / 2;
        body.position = pos;
    }
}
