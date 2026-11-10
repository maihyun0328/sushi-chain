using UnityEngine;
using UnityEngine.Events;

public class TitleComponent : MonoBehaviour
{
    [SerializeField] private UnityEvent evt;
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Return)) 
        {
            AudioManager.i.PlayUI(AudioManager.i.Choice);
            evt?.Invoke();
        }
    }
}
