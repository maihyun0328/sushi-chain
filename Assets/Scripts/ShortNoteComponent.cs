using System;
using UnityEngine;

public class ShortNoteComponent : MonoBehaviour, INote
{
    [Header("Note")]
    [SerializeField] NOTE_DIRECT dir;
    [SerializeField] Transform head;
    [SerializeField] Transform tail;
    [SerializeField] SUSHI_TYPE type;

    [Header("Setting")]
    float speed;
    [SerializeField] float judgeX;
    [SerializeField] float judgeRange;  // 판정 범위 (월드 거리). 시간 = judgeRange / speed
    [SerializeField] float FailRange;   // 판정선을 이만큼 지나면 Miss
    double noteTime;                    // 판정선에 닿는 게임 시간 (초)
    bool judged;

    void OnEnable()
    {
        speed = JsonManager.i.speed;
        judged = false;
    }

    public void Init(NoteData note)
    {
        noteTime = note.StartTime;
        judged = false;
        UpdatePosition();
    }

    void Update()
    {
        if(GameManager.i.IsGameOver || judged) return;
        UpdatePosition();
        MissCheck();
    }

    // 위치는 누적 이동(deltaTime)이 아니라 시계로 매 프레임 다시 계산한다.
    // 스폰 프레임의 오차나 프레임 드랍이 위치에 쌓이지 않는다.
    void UpdatePosition()
    {
        // LEFT 노트는 왼쪽에서 오른쪽(+x)으로, RIGHT 노트는 오른쪽에서 왼쪽(-x)으로 이동한다
        float sign = dir == NOTE_DIRECT.LEFT ? -1f : 1f;
        Vector3 pos = transform.position;
        pos.x = judgeX + sign * (float)(noteTime - SongClock.i.GameTime) * speed;
        transform.position = pos;
    }

    public double? GetJudgeTime() => judged ? (double?)null : noteTime;

    public void NoteCheck(KeyCode key, double inputTime)
    {
        if(judged) return;
        float distance = (float)Math.Abs(inputTime - noteTime) * speed;
        if(distance < judgeRange) Complete(distance);
    }

    void MissCheck()
    {
        // 노트가 판정선을 지나쳤는데 입력하지 않음
        if(SongClock.i.GameTime > noteTime + FailRange / speed) Fail();
    }

    void Complete(float distance)
    {
        judged = true;
        UIManager.i.SetPointUI(PointManager.i.GetPoint(distance));
        VFXManager.i.SushiVFX(type);
        SushiPoolComponent.i.ReturnSushi(type, NOTE_TYPE.SHORT, gameObject);
    }

    void Fail()
    {
        judged = true;
        UIManager.i.SetPointUI(PointManager.i.AddMiss());
        SushiPoolComponent.i.ReturnSushi(type, NOTE_TYPE.SHORT, gameObject);
    }
}
