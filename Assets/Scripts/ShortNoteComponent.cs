using System.Collections.Generic;
using UnityEngine;

public class ShortNoteComponent : MonoBehaviour
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

    [SerializeField] KeyCode inputKey = KeyCode.D;

    void Awake()
    {
        
    }
    void Start()
    {
        
    }
    void Update()
    {
        Move();
        NoteCheck();
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

    void NoteCheck()
    {
        // 키를 누른 순간
        if (Input.GetKeyDown(inputKey))
        {
            if (IsInJudgeRange(head.position.x))
            {
                Debug.Log("Head 성공");
                Complete();
                return;
            }
        }
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

    bool IsInJudgeRange(float x)
    {
        return Mathf.Abs(x - judgeX) <= judgeRange;
    }

    void Complete()
    {
        Debug.Log("노트 성공!");

        Destroy(gameObject);
    }

    void Fail()
    {
        Debug.Log("노트 실패!");

        Destroy(gameObject);
    }
}
