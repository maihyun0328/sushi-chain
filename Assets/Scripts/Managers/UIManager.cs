using UnityEngine.UI;
using UnityEngine;
using JetBrains.Annotations;

public class UIManager : MonoBehaviour
{
    public static UIManager i;
    public GameObject Title;
    public GameObject Menu;
    public GameObject Songs;
    public GameObject Options;
    public GameObject Credits;
    public GameObject GamePlay;
    public Text Score;
    public Text Perfect;
    public Text Great;
    public Text Good;
    public Text Bad;
    public Text Miss;
    public Text Combo;
    
    void Awake()
    {
        i = this;
    }
    public void OnSongs()
    {
        Songs.SetActive(true);
        Menu.SetActive(false);
    }
    public void OnOptoins()
    {
        Options.SetActive(true);
        Menu.SetActive(false);
    }
    public void OnCredits()
    {
        Credits.SetActive(true);
        Menu.SetActive(false);
    }
    public void OnBack()
    {
        Menu.SetActive(true);
        Songs.SetActive(false);
        Options.SetActive(false);
        Credits.SetActive(false);
    }
    public void GameStart()
    {
        GamePlay.SetActive(true);
    }
    public void SetPointUI(JUDGE_RESULT judge_result)
    {
        Score.text = "Score : " + PointManager.i.Score;
        Combo.text = judge_result.ToString() + "\nX" + PointManager.i.Combo;
        switch(judge_result)
        {
            case JUDGE_RESULT.PERFECT:
                Perfect.text = "Perfect : " + PointManager.i.Perfect;
                break;
            case JUDGE_RESULT.GREAT:
                Great.text = "Great : " + PointManager.i.Great;
                break;
            case JUDGE_RESULT.GOOD:
                Good.text = "Good : " + PointManager.i.Good;
                break;
            case JUDGE_RESULT.BAD:
                Bad.text = "Bad : " + PointManager.i.Bad;
                break;
            case JUDGE_RESULT.MISS:
                Miss.text = "Miss : " + PointManager.i.Miss;
                break;
        }
    }
    public void PointUIInitialization()
    {
        Score.text = "Score : 0";
        Perfect.text = "Perfect : 0";
        Great.text = "Great : 0";
        Good.text = "Good : 0";
        Bad.text = "Bad : 0";
        Miss.text = "Miss : 0";
        Combo.text = "";
    }
}
