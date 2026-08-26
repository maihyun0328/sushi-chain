using UnityEngine;

public class JudgeManager : MonoBehaviour
{
    public static JudgeManager i;
    KeyCode L1Key;
    KeyCode L2Key;
    KeyCode R1Key;
    KeyCode R2Key;
    [SerializeField] float LeftJudgeX;
    [SerializeField] float RightJudgeX;
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
        InputCheck();
    }
    void InputCheck()
    {
        if(Input.GetKeyDown(L1Key))
        {
            
        }
        if(Input.GetKeyDown(L2Key))
        {
            
        }
        if(Input.GetKeyDown(R1Key))
        {
            
        }
        if(Input.GetKeyDown(R2Key))
        {
            
        }
    }
}
