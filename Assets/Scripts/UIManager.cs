using UnityEngine;

public class UIManager : MonoBehaviour
{
    public static UIManager i;
    void Awake()
    {
        i = this;
    }
    public void OnSongs() {}
    public void OnOptoins() {}
    public void OnCredits() {}
}
