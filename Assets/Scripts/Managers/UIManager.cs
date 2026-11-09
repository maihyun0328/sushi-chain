using UnityEngine.UI;
using UnityEngine;
using DG.Tweening;

public class UIManager : MonoBehaviour
{
    public static UIManager i;
    public GameObject Title;
    public GameObject Menu;
    public GameObject Songs;
    public GameObject Options;
    public GameObject Credits;
    public GameObject GamePlay;
    public Text Combo;
    public GameObject TransitionUI;
    public Material TransitionMat;
    public Transform ComboObj;
    public GameObject GameResult;
    public GameObject GameOverUI;
    public Text GameOverText;
    public Slider Gauge;
    public GameObject GaugeObj;
    public Image[] Hearts;
    public GameObject PauseUI;
    private Color Perfect_Color = new Color32(0, 255, 204, 255);
    private Color Great_Color = new Color32(124, 255, 0, 255);
    private Color Good_Color = new Color32(255, 217, 0, 255);
    private Color Bad_Color = new Color32(255, 51, 51, 255);
    private Color Miss_Color = new Color32(107, 88, 117, 255);
    
    private bool IsTransitioning = false;
    
    void Awake()
    {
        i = this;
    }
    public void OnSongs()
    {
        Transition(Menu, Songs);
    }
    public void OnOptoins()
    {
        Transition(Menu, Options);
    }
    public void OnCredits()
    {
        Transition(Menu, Credits);
    }
    public void OnMenu()
    {
        GameObject obj;
        if(Songs.activeSelf) obj = Songs;
        else if(Options.activeSelf) obj = Options;
        else if(Credits.activeSelf) obj = Credits;
        else if(GameResult.activeSelf) obj = GameResult;
        else if(GameOverUI.activeSelf) obj = GameOverUI;
        else if(GamePlay.activeSelf) obj = GamePlay;
        else if(Title.activeSelf) obj = Title;
        else return;
        Transition(obj, Menu);
    }
    public void OnTitle()
    {
        Transition(Menu, Title);
    }
    public void GameStart(int index = 0)
    {
        Transition(Songs, GamePlay, GAME_STATUS.GAMESTART, index);
        Gauge.maxValue = 10;
        Gauge.value = 0;
        Combo.text = "";
        for(int i = 0; i < 3; i++)
        {
            Hearts[i].color = Color.white;
        }
        GaugeObj.SetActive(false);
    }
    public void GameClear()
    {
        Transition(GamePlay, GameResult, GAME_STATUS.GAMECLEAR);
        GameManager.i.ResetGame();
    }
    public void GameOver()
    {
        Color color = GameOverText.color;
        color.a = 0f;
        GameOverText.color = color;
        GameOverUI.SetActive(true);
        GameOverText.DOFade(1f, 1f);
        GameManager.i.ResetGame();
    }
    public void SetPointUI(JUDGE_RESULT judge_result)
    {
        switch(judge_result)
        {
            case JUDGE_RESULT.PERFECT:
                Combo.color = Perfect_Color;
                SetLifeGauge(1);
                break;
            case JUDGE_RESULT.GREAT:
                Combo.color = Great_Color;
                SetLifeGauge(0.5f);
                break;
            case JUDGE_RESULT.GOOD:
                Combo.color = Good_Color;
                SetLifeGauge(0.25f);
                break;
            case JUDGE_RESULT.BAD:
                Combo.color = Bad_Color;
                break;
            case JUDGE_RESULT.MISS:
                Combo.color = Miss_Color;
                RemoveLife();
                break;
        }
        Combo.text = judge_result.ToString() + "\nX" + PointManager.i.Combo;
        ComboAnim();
    }
    public void Transition(GameObject obj1, GameObject obj2, 
    GAME_STATUS status = GAME_STATUS.IDLE, int index = 0, bool IsMainMenu = false)
    {
        if(IsTransitioning) return;
        IsTransitioning = true;
        TransitionUI.SetActive(true);
        TransitionMat.DOFloat(2.6f, "_Scroll", 0.5f).SetEase(Ease.OutQuad).OnComplete(() =>
        {
            TransitionMat.SetFloat("_Scroll", 0f);
            TransitionMat.SetFloat("_IsOutColor", 1f);
            TransitionMat.SetColor("_OutColor", Color.black);
            TransitionMat.SetColor("_InColor", Color.white);
            if(status == GAME_STATUS.GAMESTART) GameManager.i.GameStart(index);
            else if(status == GAME_STATUS.GAMECLEAR) GameManager.i.GameClear();
            else if(IsMainMenu) 
            {
                GameManager.i.GamePlay.SetActive(false);
                PauseUI.SetActive(false);
            }
            obj1.SetActive(false);
            obj2.SetActive(true);
            TransitionMat.DOFloat(2.6f, "_Scroll", 0.5f).SetEase(Ease.InQuad).OnComplete(() =>
            {
                TransitionMat.SetColor("_OutColor", Color.white);
                TransitionMat.SetColor("_InColor", Color.black);
                TransitionMat.SetFloat("_IsOutColor", 0f);
                TransitionMat.SetFloat("_Scroll", 0f);
                TransitionUI.SetActive(false);
                IsTransitioning = false;
            });
        });
    }
    public void ComboAnim()
    {
        ComboObj.DOKill();
        ComboObj.localScale = Vector3.one * 0.6f;
        ComboObj.DOScale(Vector3.one, 0.1f).SetEase(Ease.OutQuad).OnComplete(() =>
        {
            ComboObj.DOScale(Vector3.one * 0.8f, 0.05f).SetEase(Ease.OutQuad);
        });
    }
    public void SetLifeGauge(float value)
    {
        if(GameManager.i.Life >= 3) return;
        Gauge.value += value;
        if(Gauge.value >= Gauge.maxValue)
        {
            Gauge.value = 0;
            AddLife();
        }
    }
    public void AddLife()
    {
        Hearts[GameManager.i.Life].color = Color.white;
        GameManager.i.Life += 1;
        if(GameManager.i.Life == 3) GaugeObj.SetActive(false);
    }
    public void RemoveLife()
    {
        GaugeObj.SetActive(true);
        GameManager.i.Life -= 1;
        Hearts[GameManager.i.Life].color = Color.black;
    }
}
