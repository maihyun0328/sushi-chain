using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

public class ButtonComponent : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    const float SPEED = 10f;
    Vector3 LargerSize = new Vector3(1.2f, 1.2f, 1.2f);
    Vector3 OriginSize = new Vector3(1,1,1);
    const float OFFSET = 0.01f;
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
        while(Vector3.Distance(transform.localScale, size) >= OFFSET)
        {
            transform.localScale = Vector3.Lerp(transform.localScale, size, Time.deltaTime * SPEED);
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
