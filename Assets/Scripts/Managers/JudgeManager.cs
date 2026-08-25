using UnityEngine;

public class JudgeManager : MonoBehaviour
{
    KeyCode key1;
    KeyCode key2;
    KeyCode key3;
    KeyCode key4;
    [SerializeField] float LeftJudgeX;
    [SerializeField] float RightJudgeX;
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
        if(Input.GetKeyDown(key1)) {}
        if(Input.GetKeyDown(key2)) {}
        if(Input.GetKeyDown(key3)) {}
        if(Input.GetKeyDown(key4)) {}
    }
}
