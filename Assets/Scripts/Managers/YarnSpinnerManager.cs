using UnityEngine;
using Yarn.Unity;

public class YarnSpinnerManager : MonoBehaviour
{
    public static YarnSpinnerManager i;
    public DialogueRunner dialogueRunner;
    void Awake()
    {
        i = this;
    }
    [YarnCommand("ImgChange")]
    public void ImgChange(string img)
    {
        int num = 0;
        if (int.TryParse(img, out num))
        {
            UIManager.i.SetImg(num);
        }
    }
    public async YarnTask StartConversation(string name)
    {
        if (!dialogueRunner.IsDialogueRunning)
        {
            await dialogueRunner.StartDialogue(name);
            await dialogueRunner.DialogueTask;
        }
    }
}
