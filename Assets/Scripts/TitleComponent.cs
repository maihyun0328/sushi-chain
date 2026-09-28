using UnityEngine;

public class TitleComponent : MonoBehaviour
{
    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Return))
        {
            UIManager.i.OnMenu();
        }
    }
}
