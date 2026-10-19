using UnityEngine.UI;
using UnityEngine;
using DG.Tweening;

public class GameResultComponent : MonoBehaviour
{
    [SerializeField] private Text Score;
    [SerializeField] private Text Perfect;
    [SerializeField] private Text Great;
    [SerializeField] private Text Good;
    [SerializeField] private Text Bad;
    [SerializeField] private Text Miss;
    [SerializeField] GameObject[] obj;
    void OnEnable()
    {
        Score score = PointManager.i.GetScore();
        Score.text = score.score.ToString();
        Perfect.text = score.Perfect.ToString();
        Great.text = score.Great.ToString();
        Good.text = score.Good.ToString();
        Bad.text = score.Bad.ToString();
        Miss.text = score.Miss.ToString();
        for(int i = 0; i < 6; i++)
        {
            int index = i;
            DOVirtual.DelayedCall(0.5f * (i + 1), () =>
            {
                obj[index].SetActive(true);
            });
        }
    }
    void OnDisable()
    {
        for(int i = 0; i < 6; i++)
        {
            obj[i].SetActive(false);
        }
    }
}
