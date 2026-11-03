using UnityEngine;

public interface INote
{
    // 스폰 직후 한 번 호출된다. 노트 시각(초)을 받아 이후 위치와 판정을 시계 기준으로 계산한다.
    public void Init(NoteData note);
    // 지금 입력을 받을 수 있으면 판정 기준 시각(게임 시간, 초)을 돌려준다. 받을 수 없으면 null.
    public double? GetJudgeTime();
    // inputTime: 입력이 실제로 일어난 게임 시간 (SongClock.GameTimeAt(timestamp))
    public void NoteCheck(KeyCode key, double inputTime);
}
