using System.Collections;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.EventSystems;

public class ButtonComponent : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    float speed = 10f;
    Vector3 LargerSize = new Vector3(1.2f, 1.2f, 1.2f);
    Vector3 OriginSize = new Vector3(1,1,1);
    Coroutine cor;
    public void OnPointerEnter(PointerEventData eventData)
    {
        if(cor != null) StopCoroutine(cor);
        cor = StartCoroutine(ChangeScale(LargerSize));
    }

    // 마우스가 UI 밖으로 나갔을 때
    public void OnPointerExit(PointerEventData eventData)
    {
        if(cor != null) StopCoroutine(cor);
        cor = StartCoroutine(ChangeScale(OriginSize));
    }
    public IEnumerator ChangeScale(Vector3 size)
    {
        while(Vector3.Distance(transform.localScale, size) >= 0.01f)
        {
            transform.localScale = Vector3.Lerp(transform.localScale, size, Time.deltaTime * speed);
            yield return null;
        }
        transform.localScale = size;
        cor = null;
    }
    public void OnDisable()
    {
        transform.localScale = OriginSize;
    }
}
