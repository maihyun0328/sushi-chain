using System.Collections.Generic;
using System.Collections;
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
    float speed;
    [SerializeField] float judgeX;
    [SerializeField] float FailedX;
    [SerializeField] float judgeRange;
    [SerializeField] float FailRange;

    [SerializeField] KeyCode inputKey;

    NOTE_STATE state = NOTE_STATE.WAITING;
    List<Renderer> rends = new List<Renderer>();
    IEnumerator cor;

    void Awake()
    {
        for(int i = 0; i < transform.childCount; i++)
        {
            rends.Add(transform.GetChild(i).GetComponent<Renderer>());
        }
    }
    void OnEnable()
    {
        head.gameObject.SetActive(true);
        speed = JsonManager.i.speed;
        state = NOTE_STATE.WAITING;
        SetTransparency(0);
        cor = null;
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
            cor = HoldingParticle();
            StartCoroutine(cor);
            head.gameObject.SetActive(false);
            UIManager.i.SetPointUI(PointManager.i.GetPoint(Mathf.Abs(GetJudgeX() - judgeX)));
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
                if (NoteX < FailedX)
                {
                    Fail();
                }
                break;
            case NOTE_DIRECT.LEFT:
                // Tail이 판정선을 완전히 지나갔는데도 안 뗐음
                if (NoteX > FailedX)
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
        StopCoroutine(cor);
        state = NOTE_STATE.COMPLETED;
        VFXManager.i.SushiVFX(dir);
        UIManager.i.SetPointUI(PointManager.i.GetPoint(Mathf.Abs(tail.transform.position.x - judgeX)));
        SushiPoolComponent.i.ReturnSushi(type, NOTE_TYPE.LONG, gameObject);
    }

    void Fail()
    {
        if(cor != null) StopCoroutine(cor);
        cor = Failing();
        state = NOTE_STATE.FAILED;
        StartCoroutine(cor);
    }
    void SetBody()
    {
        Vector3 scale = body.transform.localScale;
        Vector3 pos = body.position;
        switch(dir)
        {
            case NOTE_DIRECT.LEFT:
                switch(state)
                {
                    case NOTE_STATE.HOLDING:
                        if(tail.position.x - judgeX < 0)
                        {
                            scale.z = Mathf.Abs(tail.position.x - judgeX) * 100;
                            pos.x = (tail.position.x + judgeX) / 2;
                        }
                        break;
                    case NOTE_STATE.FAILED:
                        if(tail.position.x - FailedX < 0)
                        {
                            scale.z = Mathf.Abs(tail.position.x - FailedX) * 100;
                            pos.x = (tail.position.x + FailedX) / 2;
                        }
                        break;
                }
                break;
            case NOTE_DIRECT.RIGHT:
                switch(state)
                {
                    case NOTE_STATE.HOLDING:
                        if(tail.position.x - judgeX > 0)
                        {
                            scale.z = Mathf.Abs(tail.position.x - judgeX) * 100;
                            pos.x = (tail.position.x + judgeX) / 2;
                        }
                        break;
                    case NOTE_STATE.FAILED:
                        if(tail.position.x - FailedX > 0)
                        {
                            scale.z = Mathf.Abs(tail.position.x - FailedX) * 100;
                            pos.x = (tail.position.x + FailedX) / 2;
                        }
                        break;
                }
                break;
        }
        body.transform.localScale = scale;
        body.position = pos;
    }
    IEnumerator HoldingParticle() 
    {
        while(true)
        {
            VFXManager.i.SushiVFX(dir);
            UIManager.i.SetPointUI(PointManager.i.GetPoint(0f));
            yield return new WaitForSeconds(0.2f);
        }
    }
    IEnumerator Failing()
    {
        UIManager.i.SetPointUI(PointManager.i.AddMiss());
        SetTransparency(-0.5f);
        head.gameObject.SetActive(false);
        while(!EndCheck())
        {
            SetBody();
            yield return null;
        }
        SushiPoolComponent.i.ReturnSushi(type, NOTE_TYPE.LONG, gameObject);
    }
    bool EndCheck()
    {
        switch(dir)
        {
            case NOTE_DIRECT.LEFT:
                if(tail.transform.position.x > FailedX)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            case NOTE_DIRECT.RIGHT:
                if(tail.transform.position.x < FailedX)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            default:
                return false;
        }
    }
    void SetTransparency(float value)
    {
        Renderer rend = body.GetComponent<Renderer>();
        rend.material.SetFloat("_Tweak_transparency", value);
        rend = tail.GetChild(0).GetComponent<Renderer>();
        rend.material.SetFloat("_Tweak_transparency", value);
        rend = tail.GetChild(1).GetComponent<Renderer>();
        rend.material.SetFloat("_Tweak_transparency", value);
    }
}
