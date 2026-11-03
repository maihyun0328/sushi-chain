using System;
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
    double startTime;   // 머리가 판정선에 닿는 게임 시간 (초)
    double endTime;     // 꼬리가 판정선에 닿는 게임 시간 (초)
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
    public void Init(NoteData note)
    {
        startTime = note.StartTime;
        endTime = note.EndTime;
        Move();
    }
    void Update()
    {
        if(GameManager.i.IsGameOver) return;
        Move();
        switch(state)
        {
            case NOTE_STATE.WAITING:
                MissCheck(startTime);
                break;
            case NOTE_STATE.HOLDING:
                CheckHolding();
                break;
        }
    }

    // 위치는 누적 이동(deltaTime)이 아니라 시계로 매 프레임 다시 계산한다 (머리 = 오브젝트 원점)
    void Move()
    {
        float sign = dir == NOTE_DIRECT.LEFT ? -1f : 1f;
        Vector3 pos = transform.position;
        pos.x = judgeX + sign * (float)(startTime - SongClock.i.GameTime) * speed;
        transform.position = pos;
    }

    public double? GetJudgeTime() => state == NOTE_STATE.WAITING ? (double?)startTime : null;

    public void NoteCheck(KeyCode key, double inputTime)
    {
        if(state != NOTE_STATE.WAITING) return;
        float distance = TimeToDistance(inputTime - startTime);
        if (distance <= judgeRange)
        {
            inputKey = key;
            cor = HoldingParticle();
            StartCoroutine(cor);
            head.gameObject.SetActive(false);
            UIManager.i.SetPointUI(PointManager.i.GetPoint(distance));
            state = NOTE_STATE.HOLDING;
        }
    }
    float TimeToDistance(double dt) => (float)Math.Abs(dt) * speed;

    void CheckHolding()
    {
        SetBody();
        // 키를 떼었을 때 (뗀 시각의 timestamp로 판정)
        if (TimedKeyInput.GetKeyUp(inputKey, out double realtime))
        {
            float distance = TimeToDistance(SongClock.i.GameTimeAt(realtime) - endTime);
            // Tail이 판정선 근처인지 확인
            if (distance <= judgeRange)
            {
                Complete(distance);
            }
            else
            {
                // 너무 일찍 떼었다
                Fail();
            }
            return;
        }
        MissCheck(endTime);
    }
    // noteTime(머리 또는 꼬리)이 FailedX까지 지나갔는데 입력이 없음
    void MissCheck(double noteTime)
    {
        if (SongClock.i.GameTime > noteTime + Mathf.Abs(FailedX - judgeX) / speed)
        {
            Fail();
        }
    }

    void Complete(float distance)
    {
        StopCoroutine(cor);
        state = NOTE_STATE.COMPLETED;
        VFXManager.i.SushiVFX(type);
        UIManager.i.SetPointUI(PointManager.i.GetPoint(distance));
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
                            scale.z = Mathf.Abs(tail.position.x - judgeX) * 0.9f;
                            pos.x = (tail.position.x - judgeX) / 2 *    0.902f + judgeX;
                        }
                        break;
                    case NOTE_STATE.FAILED:
                        if(tail.position.x - FailedX < 0)
                        {
                            scale.z = Mathf.Abs(tail.position.x - FailedX) * 0.9f;
                            pos.x = (tail.position.x - FailedX) / 2 * 0.902f + FailedX;
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
                            scale.z = Mathf.Abs(tail.position.x - judgeX) * 0.9f;
                            pos.x = (tail.position.x - judgeX) / 2 * 0.902f + judgeX;
                        }
                        break;
                    case NOTE_STATE.FAILED:
                        if(tail.position.x - FailedX > 0)
                        {
                            scale.z = Mathf.Abs(tail.position.x - FailedX) * 0.9f;
                            pos.x = (tail.position.x - FailedX) / 2 * 0.902f + FailedX;
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
            VFXManager.i.SushiVFX(type);
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
        Renderer rend = body.GetChild(0).GetComponent<Renderer>();
        rend.material.SetFloat("_Tweak_transparency", value);
        rend = tail.GetChild(0).GetComponent<Renderer>();
        rend.material.SetFloat("_Tweak_transparency", value);
        rend = tail.GetChild(1).GetComponent<Renderer>();
        rend.material.SetFloat("_Tweak_transparency", value);
    }
}
