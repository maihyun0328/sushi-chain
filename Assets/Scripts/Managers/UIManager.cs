using UnityEngine;
using UnityEngine.Rendering;

public class UIManager : MonoBehaviour
{
    public static UIManager i;
    public GameObject Title;
    public GameObject Menu;
    public GameObject Songs;
    public GameObject Options;
    public GameObject Credits;
    public GameObject GamePlay;
    
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
}
