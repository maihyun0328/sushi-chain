using System.Collections.Generic;
using UnityEngine;

public class LongNoteComponent : MonoBehaviour
{
    [Header("Note")]
    [SerializeField] Transform head;
    [SerializeField] Transform body;
    [SerializeField] Transform tail;

    [Header("Setting")]
    [SerializeField] float speed;
    [SerializeField] float judgeX;
    [SerializeField] float judgeRange = 0.3f;

    [SerializeField] KeyCode inputKey = KeyCode.D;

    NOTE_STATE state = NOTE_STATE.WAITING;
    List<Renderer> rends = new List<Renderer>();
    [SerializeField] Material OriginMat;
    [SerializeField] Material HoldingMat;

    void Awake()
    {
        for(int i = 0; i < transform.childCount; i++)
        {
            rends.Add(transform.GetChild(i).GetComponent<Renderer>());
        }
    }
    void Start()
    {
        SetOriginMat();
    }
    void Update()
    {
        Move();
        switch (state)
        {
            case NOTE_STATE.WAITING:
                CheckHead();
                break;

            case NOTE_STATE.HOLDING:
                CheckHolding();
                break;
        }
    }

    void Move()
    {
        transform.position += Vector3.left * speed * Time.deltaTime;
    }

    void CheckHead()
    {
        // 키를 누른 순간
        if (Input.GetKeyDown(inputKey))
        {
            if (IsInJudgeRange(head.position.x))
            {
                Debug.Log("Head 성공");
                head.gameObject.SetActive(false);
                Vector3 scale = body.transform.localScale;
                scale.x = Mathf.Abs(tail.position.x - judgeX);
                body.transform.localScale = scale;
                Vector3 pos = body.position;
                pos.x = (tail.position.x + judgeX) / 2;
                body.position = pos;
                SetHoldingMat();
                
                state = NOTE_STATE.HOLDING;
            }
        }

        // Head가 판정선을 지나쳤는데 입력하지 않음
        if (head.position.x < judgeX - judgeRange)
        {
            Fail();
        }
    }

    void CheckHolding()
    {
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

        // Tail이 판정선을 완전히 지나갔는데도 안 뗐음
        if (tail.position.x < judgeX - judgeRange)
        {
            Fail();
        }
    }

    bool IsInJudgeRange(float x)
    {
        return Mathf.Abs(x - judgeX) <= judgeRange;
    }

    void Complete()
    {
        state = NOTE_STATE.COMPLETED;

        Debug.Log("노트 성공!");

        Destroy(gameObject);
    }

    void Fail()
    {
        SetOriginMat();
        state = NOTE_STATE.FAILED;

        Debug.Log("노트 실패!");

        Destroy(gameObject);
    }
    void SetOriginMat()
    {
        for(int i = 0; i < rends.Count; i++)
        {
            rends[i].sharedMaterial = OriginMat;
        }
    }
    void SetHoldingMat()
    {
        for(int i = 0; i < rends.Count; i++)
        {
            rends[i].sharedMaterial = HoldingMat;
        }
    }
}
