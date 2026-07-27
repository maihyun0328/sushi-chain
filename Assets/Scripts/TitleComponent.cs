using UnityEngine;

public class TitleComponent : MonoBehaviour
{
    [SerializeField] GameObject Menu;
    [SerializeField] GameObject Title;
    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Return))
        {
            Menu.SetActive(true);
            Title.SetActive(false);
        }
    }
}
