using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager i;
    [SerializeField] public GameObject GamePlay;
    void Awake()
    {
        i = this;
    }
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
    public void GameStart()
    {
        GamePlay.SetActive(true);
    }
}
