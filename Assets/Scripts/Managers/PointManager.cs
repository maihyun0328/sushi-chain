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
    public int MaxCombo;

    [Header("Judge Distance")]
    [SerializeField] float perfectRange = 0.15f;
    [SerializeField] float greatRange = 0.2f;
    [SerializeField] float goodRange = 0.25f;
    [SerializeField] float badRange = 0.3f;
    [SerializeField] float MissRange = 0.35f;
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
            result = JUDGE_RESULT.PERFECT;
        }
        else if (distance <= greatRange)
        {
            Great++;
            Score += 700;
            result = JUDGE_RESULT.GREAT;
        }
        else if (distance <= goodRange)
        {
            Good++;
            Score += 400;
            result = JUDGE_RESULT.GOOD;
        }
        else if (distance <= badRange)
        {
            Bad++;
            Score += 100;
            result = JUDGE_RESULT.BAD;
        }
        else
        {
            return AddMiss();
        }

        Combo++;
        MaxCombo = Mathf.Max(MaxCombo, Combo);

        // 추후 여기서 UIManager에 판정 결과 전달
        // UIManager.i.ShowJudge(result);

        return result;
    }
    public JUDGE_RESULT AddMiss()
    {
        Miss++;
        Combo = 0;

        // UIManager.i.ShowJudge(JUDGE_RESULT.MISS);

        return JUDGE_RESULT.MISS;
    }
}
