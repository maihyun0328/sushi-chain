using UnityEngine;

public class PointManager : MonoBehaviour
{
    public static PointManager i;
    [Header("Score")]
    public int Score;
    public int Perfect;
    public int Great;
    public int Good;
    public int Bad;
    public int Miss;

    [Header("Combo")]
    public int Combo;

    [Header("Judge Distance")]
    [SerializeField] float perfectRange = 0.2f;
    [SerializeField] float greatRange = 0.5f;
    [SerializeField] float goodRange = 0.8f;
    void Awake()
    {
        i = this;
    }
    public void SetPoint()
    {
        Score = 0;
        Perfect = 0;
        Great = 0;
        Good = 0;
        Bad = 0;
        Miss = 0;
    }
    public JUDGE_RESULT GetPoint(float distance)
    {
        JUDGE_RESULT result;

        if (distance <= perfectRange)
        {
            Perfect++;
            Score += 1000;
            Combo++;
            result = JUDGE_RESULT.PERFECT;
        }
        else if (distance <= greatRange)
        {
            Great++;
            Score += 700;
            Combo++;
            result = JUDGE_RESULT.GREAT;
        }
        else if (distance <= goodRange)
        {
            Good++;
            Score += 400;
            Combo++;
            result = JUDGE_RESULT.GOOD;
        }
        else
        {
            Bad++;
            Score += 100;
            Combo = 0;
            result = JUDGE_RESULT.BAD;
        }

        // 추후 여기서 UIManager에 판정 결과 전달
        // UIManager.i.ShowJudge(result);

        return result;
    }
    public JUDGE_RESULT AddMiss()
    {
        Miss++;
        Combo = 0;
        GameManager.i.Life -= 1;
        // UIManager.i.ShowJudge(JUDGE_RESULT.MISS);

        return JUDGE_RESULT.MISS;
    }
    public Score GetScore()
    {
        Score score = new Score();
        score.score = Score;
        score.Perfect = Perfect;
        score.Great = Great;
        score.Good = Good;
        score.Bad = Bad;
        score.Miss = Miss;
        return score;
    }
}
