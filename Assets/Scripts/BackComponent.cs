using UnityEngine;

public class BackComponent : MonoBehaviour
{
    [SerializeField] private GameObject priorityPanel;
    void Update()
    {
        if (!Input.GetKeyDown(KeyCode.Escape)) return;

        if (priorityPanel != null && priorityPanel.activeSelf)
        {
            priorityPanel.SetActive(false);
            return;
        }

        UIManager.i.OnMenu();
    }
}
