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
    [SerializeField] RectTransform Accuracys;
    [SerializeField] RectTransform Results_UI;
    [SerializeField] RectTransform Results_UI1;
    [SerializeField] RectTransform Results_UI2;
    [SerializeField] RectTransform Results_UI3;
    Vector2 ResultsImg1_Vector = new Vector2(172.5f, -98.2f);
    Vector2 ResultsImg2_Vector = new Vector2(-170.3f, 226.8f);
    Vector2 ResultsImg3_Vector = new Vector2(148, 14.37f);
    Vector2 Accuracys_Vector = new Vector2(285, 283);
    Vector2 offset = new Vector2(960, 0);
    Sequence seq;
    void OnEnable()
    {
        Score score = PointManager.i.GetScore();
        Score.text = "0";
        Perfect.text = score.Perfect.ToString();
        Great.text = score.Great.ToString();
        Good.text = score.Good.ToString();
        Bad.text = score.Bad.ToString();
        Miss.text = score.Miss.ToString();
        seq = DOTween.Sequence();
        seq.Join(Results_UI1.DOAnchorPos(ResultsImg1_Vector, 1f).SetEase(Ease.OutQuad));
        seq.Join(Results_UI2.DOAnchorPos(ResultsImg2_Vector, 1f).SetEase(Ease.OutQuad));
        seq.Join(Results_UI3.DOAnchorPos(ResultsImg3_Vector, 1f).SetEase(Ease.OutQuad));
        seq.Join(Accuracys.DOAnchorPos(Accuracys_Vector, 1f).SetEase(Ease.OutQuad));
        seq.AppendCallback(() =>
        {
            DOVirtual.Int(0, score.score, 2f, value =>
            {
                Score.text = value.ToString("N0");
            }).SetEase(Ease.OutExpo);
        });
    }
    void OnDisable()
    {
        Results_UI1.anchoredPosition = ResultsImg1_Vector - offset;
        Results_UI2.anchoredPosition = ResultsImg2_Vector + offset;
        Results_UI3.anchoredPosition = ResultsImg3_Vector - offset;
        Accuracys.anchoredPosition = Accuracys_Vector - offset;
    }
}
