using UnityEngine.UI;
using UnityEngine;

public class GameResultComponent : MonoBehaviour
{
    [SerializeField] private Text Score;
    [SerializeField] private Text Perfect;
    [SerializeField] private Text Great;
    [SerializeField] private Text Good;
    [SerializeField] private Text Bad;
    [SerializeField] private Text Miss;
    void OnEnable()
    {
        Score.text = UIManager.i.Score.text;
        Perfect.text = UIManager.i.Perfect.text;
        Great.text = UIManager.i.Great.text;
        Good.text = UIManager.i.Good.text;
        Bad.text = UIManager.i.Bad.text;
        Miss.text = UIManager.i.Miss.text;
    }
}
