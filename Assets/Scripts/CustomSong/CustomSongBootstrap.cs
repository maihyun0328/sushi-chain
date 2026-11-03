using UnityEngine;
using UnityEngine.UI;

// 씬 로드 후 커스텀 곡 기능을 런타임으로 붙인다. 씬/프리팹은 수정하지 않는다.
// - "CustomSongService" GameObject (DontDestroyOnLoad)
// - UIManager.i.Songs 아래 "CustomSongUI" (버튼 + 모달 패널)
public static class CustomSongBootstrap
{
    const string ServiceObjectName = "CustomSongService";
    const string UIObjectName = "CustomSongUI";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        // Domain Reload가 꺼져 있으면 static 값이 남아 있을 수 있다
        CustomSongInputLock.IsLocked = false;

        EnsureService();

        // 토큰 공급자 기본값: 개발용 PlayerPrefs 토큰. 다른 공급자를 쓰려면 이보다 먼저 설정한다
        if (CustomSongService.TokenProvider == null) CustomSongService.TokenProvider = new PlayerPrefsTokenProvider();

        if (UIManager.i == null || UIManager.i.Songs == null)
        {
            Debug.LogError("[CustomSong] UIManager.i 또는 UIManager.i.Songs가 없습니다. 커스텀 곡 UI를 만들지 않습니다.");
            return;
        }

        Transform songs = UIManager.i.Songs.transform;
        if (songs.Find(UIObjectName) != null) return; // 이미 생성됨

        Font font = UIManager.i.Score != null && UIManager.i.Score.font != null
            ? UIManager.i.Score.font
            : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // 루트: Songs 화면 전체를 덮는 투명 컨테이너. Songs가 비활성이어도 생성 가능.
        GameObject root = new GameObject(UIObjectName, typeof(RectTransform));
        RectTransform rootRt = (RectTransform)root.transform;
        rootRt.SetParent(songs, false);
        CustomSongPanel.Stretch(rootRt);
        rootRt.SetAsLastSibling();

        Button openButton = CreateOpenButton(rootRt, font);

        CustomSongPanel panel = root.AddComponent<CustomSongPanel>();
        panel.Setup(font, openButton);
    }

    static void EnsureService()
    {
        if (CustomSongService.i != null) return;
        CustomSongService existing = Object.FindFirstObjectByType<CustomSongService>(FindObjectsInactive.Include);
        if (existing != null)
        {
            CustomSongService.i = existing;
            return;
        }
        GameObject go = new GameObject(ServiceObjectName);
        Object.DontDestroyOnLoad(go);
        go.AddComponent<CustomSongService>();
    }

    // 우상단 "+ 커스텀 곡 (C)" 버튼
    static Button CreateOpenButton(RectTransform parent, Font font)
    {
        Button button = CustomSongPanel.CreateButton(parent, "OpenButton", "+ 커스텀 곡", font,
            new Vector2(340f, 84f), 34);
        RectTransform rt = (RectTransform)button.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-48f, -48f);

        // 라벨을 왼쪽으로 조금 밀고 오른쪽에 (C) 힌트
        RectTransform label = (RectTransform)button.transform.Find("Label");
        label.offsetMax = new Vector2(-64f, 0f);

        Text hint = CustomSongPanel.CreateText(rt, "Hint", "(C)", font, 24,
            TextAnchor.MiddleRight, new Color(1f, 1f, 1f, 0.55f));
        RectTransform hintRt = hint.rectTransform;
        CustomSongPanel.Stretch(hintRt);
        hintRt.offsetMax = new Vector2(-20f, 0f);
        return button;
    }
}
