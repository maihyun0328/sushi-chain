using UnityEngine;

public class PointManager : MonoBehaviour
{
    public static PointManager i;
    public int Score;
    public int Perfect;
    public int Great;
    public int Good;
    public int Bad;
    public int Miss;
    void Awake()
    {
        i = this;
    }

    void Start()
    {
        
    }

    void Update()
    {
        
    }
}
