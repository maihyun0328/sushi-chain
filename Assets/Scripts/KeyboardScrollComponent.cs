using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class KeyboardScrollComponent : MonoBehaviour
{
    int index = 0;
    List<GameObject> Songs = new List<GameObject>();
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if((Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow)) && index != 0)
        {
            index -= 1;
        }
        if((Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow)) && index != Songs.Count - 1)
        {
            index += 1;
        }
    }
}
