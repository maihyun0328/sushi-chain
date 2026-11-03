using UnityEngine;
using UnityEngine.Events;

public class BackComponent : MonoBehaviour
{
    [SerializeField] private UnityEvent evt;
    void Update()
    {
        if (CustomSongInputLock.IsLocked) return; // 커스텀 곡 패널이 Esc를 직접 처리
        if (KeyboardScrollComponent.IsLoadingCustom) return; // 커스텀 곡 오디오 로드 중에는 뒤로 가기 막음
        if (Input.GetKeyDown(KeyCode.Escape)) evt?.Invoke();
    }
}
