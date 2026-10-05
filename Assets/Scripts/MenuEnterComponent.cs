using UnityEngine;

public class MenuEnterComponent : MonoBehaviour
{
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Return))
        {
            UIManager.i.OnMenu();
        }
    }
}
