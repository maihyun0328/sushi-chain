using UnityEngine;
using UnityEngine.Events;

public class BackComponent : MonoBehaviour
{
    [SerializeField] private UnityEvent evt;
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) evt?.Invoke();
    }
}
