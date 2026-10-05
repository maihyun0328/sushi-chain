using System;
using UnityEngine;
using UnityEngine.UI;

public class KeySettingComponent : MonoBehaviour
{
    [SerializeField] private GameObject KeySettingImg;
    [SerializeField] private Text L1_Key_Text;
    [SerializeField] private Text L2_Key_Text;
    [SerializeField] private Text R1_Key_Text;
    [SerializeField] private Text R2_Key_Text;
    KeyCode key;
    int waitingType = -1;
    int clickedFrame;
    void OnEnable()
    {
        KeyTextSet();
    }
    void Update()
    {
        if (waitingType < 0 || Time.frameCount == clickedFrame || !Input.anyKeyDown) return;
        foreach (KeyCode pressedKey in Enum.GetValues(typeof(KeyCode)))
        {
            if (!Input.GetKeyDown(pressedKey)) continue;
            if (pressedKey >= KeyCode.Mouse0 && pressedKey <= KeyCode.Mouse6) continue;
            if(IsDuplicateKey(pressedKey)) return;
            if (pressedKey != KeyCode.Escape) JudgeManager.i.SetKey((SUSHI_TYPE)waitingType,pressedKey);
            KeySettingImg.SetActive(false);
            KeyTextSet();
            waitingType = -1;
            return;
        }
    }
    public void KeySet(int type)
    {
        KeySettingImg.SetActive(true);
        clickedFrame = Time.frameCount;
        waitingType = type;
    }
    void OnDisable()
    {
        waitingType = -1;
        KeySettingImg.SetActive(false);
    }
    public void KeyTextSet()
    {
        L1_Key_Text.text = JudgeManager.i.L1_Key.ToString();
        L2_Key_Text.text = JudgeManager.i.L2_Key.ToString();
        R1_Key_Text.text = JudgeManager.i.R1_Key.ToString();
        R2_Key_Text.text = JudgeManager.i.R2_Key.ToString();
    }
    bool IsDuplicateKey(KeyCode key)
    {
        switch(waitingType)
        {
            case 0:
                if(key == JudgeManager.i.L2_Key || key == JudgeManager.i.R1_Key || key == JudgeManager.i.R2_Key)
                    return true;
                else 
                    break;
            case 1:
                if(key == JudgeManager.i.L1_Key || key == JudgeManager.i.R1_Key || key == JudgeManager.i.R2_Key)
                    return true;
                else 
                    break;
            case 2:
                if(key == JudgeManager.i.L1_Key || key == JudgeManager.i.L2_Key || key == JudgeManager.i.R2_Key)
                    return true;
                else 
                    break;
            case 3:
                if(key == JudgeManager.i.L1_Key || key == JudgeManager.i.L2_Key || key == JudgeManager.i.R1_Key)
                    return true;
                else 
                    break;
            default:
                return false;
        }
        return false;
    }
}
