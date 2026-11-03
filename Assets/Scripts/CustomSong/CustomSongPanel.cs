using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// 커스텀 곡 URL 입력 모달. CustomSongBootstrap이 Songs 화면 아래 "CustomSongUI"에 붙인다.
// 이 컴포넌트는 항상 활성인 루트에 있고, 모달(Modal)만 켜고 끈다.
public class CustomSongPanel : MonoBehaviour
{
    static readonly Color PanelColor = new Color(0.07f, 0.07f, 0.1f, 0.96f);
    static readonly Color DimColor = new Color(0f, 0f, 0f, 0.72f);
    static readonly Color FieldColor = new Color(1f, 1f, 1f, 0.08f);
    static readonly Color ButtonColor = new Color(1f, 1f, 1f, 0.14f);
    static readonly Color SelectedColor = new Color(0f, 1f, 0.8f, 0.55f);
    static readonly Color AccentColor = new Color32(0, 255, 204, 255);
    static readonly Color ErrorColor = new Color32(255, 90, 90, 255);
    static readonly Color SubTextColor = new Color(1f, 1f, 1f, 0.6f);

    const float Margin = 48f;
    const float IndeterminateWidth = 0.25f; // 불확정 진행 바의 막대 폭 (0..1)

    enum State { Idle, Busy, Done, Failed }

    GameObject modal;
    RectTransform window;
    InputField urlField;
    Button submitButton;
    Text submitLabel;
    Button secondaryButton;
    Text secondaryLabel;
    Text errorText;
    Text stageText;
    GameObject progressRoot;
    RectTransform progressFill;
    GameObject resultRoot;
    Image thumbImage;
    Text resultTitle;

    // 난이도
    Button[] difficultyButtons;
    string selectedDifficulty = ChartDifficulty.Normal;

    // 개발용 토큰
    RectTransform tokenHeader;
    RectTransform tokenRow;
    Text tokenToggleLabel;
    Text tokenStatusText;
    InputField tokenField;
    Button tokenSaveButton;
    bool tokenExpanded;

    State state = State.Idle;
    int requestToken;      // 닫기/취소 후 늦게 도착한 콜백 무시용
    bool pendingUnlock;    // Esc로 닫은 프레임에는 LateUpdate에서 잠금 해제 (Back의 Esc 동시 처리 방지)
    Sprite thumbSprite;
    Texture2D thumbTexture;
    Coroutine focusRoutine;

    // 진행 표시
    CUSTOM_SONG_STAGE currentStage = CUSTOM_SONG_STAGE.SUBMITTING;
    float processingElapsed;     // 서버가 마지막으로 알려 준 경과 시간(초)
    float processingReceivedAt;  // 그 값을 받은 시각 (unscaledTime)

    // InputField는 Esc에서 텍스트를 활성화 시점 값으로 되돌린다. 입력 중이던 값을 보존하기 위해 매 프레임 기록
    // EventSystem과 이 컴포넌트의 Update 순서가 정해져 있지 않으므로 "직전 프레임의 포커스"도 함께 본다
    bool urlFocusedLastFrame;
    bool tokenFocusedLastFrame;
    string urlDraft = "";
    string tokenDraft = "";

    public bool IsOpen => modal != null && modal.activeSelf;

    static PlayerPrefsTokenProvider DevTokenProvider => CustomSongService.TokenProvider as PlayerPrefsTokenProvider;

    // ===== 공개 API =====

    public void Setup(Font font, Button openButton)
    {
        if (modal != null) return;
        BuildModal(font);
        if (openButton != null) openButton.onClick.AddListener(Open);
        modal.SetActive(false);
    }

    public void Open()
    {
        if (modal == null || IsOpen) return;
        pendingUnlock = false;
        modal.SetActive(true);
        modal.transform.SetAsLastSibling();
        CustomSongInputLock.IsLocked = true;

        // 토큰이 없으면 토큰 행을 자동으로 펼친다
        if (DevTokenProvider != null && !HasToken()) tokenExpanded = true;
        RefreshTokenSection();

        if (state != State.Busy) SetState(State.Idle);
        else RestartFocus();
    }

    public void Close()
    {
        CloseInternal(true);
    }

    // ===== 수명 주기 =====

    void Update()
    {
        if (modal == null) return;

        if (!IsOpen)
        {
            if (Input.GetKeyDown(KeyCode.C) && !CustomSongInputLock.IsLocked
                && UIManager.i != null && UIManager.i.Songs != null && UIManager.i.Songs.activeInHierarchy)
            {
                Open();
            }
            return;
        }

        bool tokenFocused = IsFocused(tokenField) || tokenFocusedLastFrame;
        bool urlFocused = IsFocused(urlField) || urlFocusedLastFrame;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            CloseInternal(false);
            // InputField가 Esc로 되돌린 텍스트를 복원한다
            if (tokenFocused && tokenField != null) tokenField.text = tokenDraft;
            if (urlFocused && urlField != null) urlField.text = urlDraft;
            return;
        }

        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            // 토큰 입력 중의 Enter는 곡 요청이 아니라 토큰 저장
            if (tokenFocused) SaveToken();
            else if (state != State.Busy) Submit();
        }
    }

    void LateUpdate()
    {
        if (IsOpen)
        {
            urlFocusedLastFrame = IsFocused(urlField);
            tokenFocusedLastFrame = IsFocused(tokenField);
            if (urlFocusedLastFrame) urlDraft = urlField.text ?? "";
            if (tokenFocusedLastFrame) tokenDraft = tokenField.text ?? "";

            if (state == State.Busy && IsIndeterminate(currentStage)) AnimateProgress();
        }

        if (!pendingUnlock) return;
        pendingUnlock = false;
        CustomSongInputLock.IsLocked = false;
    }

    void OnDisable()
    {
        // Songs 화면이 꺼지면 깨끗하게 닫아 IsLocked가 남지 않게 한다
        if (IsOpen || CustomSongInputLock.IsLocked || pendingUnlock) CloseInternal(true);
    }

    void OnDestroy()
    {
        ReleaseThumbnail();
    }

    // ===== 동작 =====

    void CloseInternal(bool unlockNow)
    {
        if (focusRoutine != null)
        {
            StopCoroutine(focusRoutine);
            focusRoutine = null;
        }
        if (state == State.Busy)
        {
            requestToken++;
            if (CustomSongService.i != null && CustomSongService.i.IsBusy) CustomSongService.i.Cancel();
            state = State.Idle;
        }
        if (urlField != null) urlField.DeactivateInputField();
        if (tokenField != null) tokenField.DeactivateInputField();
        urlFocusedLastFrame = tokenFocusedLastFrame = false;
        if (modal != null) modal.SetActive(false);

        if (unlockNow)
        {
            pendingUnlock = false;
            CustomSongInputLock.IsLocked = false;
        }
        else
        {
            pendingUnlock = true;
        }
    }

    void Submit()
    {
        if (state == State.Busy) return;

        string url = urlField.text != null ? urlField.text.Trim() : "";
        if (url.Length == 0)
        {
            ShowInlineError("YouTube URL을 입력하세요.");
            return;
        }
        if (!CustomSongService.IsValidYoutubeUrl(url))
        {
            ShowInlineError("올바른 YouTube URL이 아닙니다. (예: https://www.youtube.com/watch?v=...)");
            return;
        }
        if (DevTokenProvider != null && !HasToken())
        {
            tokenExpanded = true;
            RefreshTokenSection();
            ShowInlineError("개발용 토큰을 먼저 저장하세요. (issue_dev_token.py로 발급)");
            return;
        }
        CustomSongService service = CustomSongService.i;
        if (service == null)
        {
            ShowInlineError("CustomSongService를 찾을 수 없습니다.");
            return;
        }
        if (service.IsBusy)
        {
            ShowInlineError("이미 다른 요청을 처리 중입니다.");
            return;
        }

        int token = ++requestToken;
        SetState(State.Busy);
        SetProgress(CUSTOM_SONG_STAGE.SUBMITTING, 0f);

        service.RequestSong(url, selectedDifficulty,
            (stage, progress) => { if (token == requestToken) SetProgress(stage, progress); },
            meta => { if (token == requestToken) OnDone(meta); },
            error => { if (token == requestToken) OnFailed(error); });
    }

    void OnSecondaryClicked()
    {
        if (state == State.Busy)
        {
            // 취소: 패널은 열어 둔 채 입력 상태로 돌아간다
            requestToken++;
            if (CustomSongService.i != null) CustomSongService.i.Cancel();
            SetState(State.Idle);
            stageText.text = "취소됨";
            stageText.color = Color.white;
            stageText.gameObject.SetActive(true);
            return;
        }
        Close();
    }

    void OnDone(CustomSongMeta meta)
    {
        SetState(State.Done);
        SetProgress(CUSTOM_SONG_STAGE.DONE, 1f);

        ReleaseThumbnail();
        Texture2D tex = meta != null && !string.IsNullOrEmpty(meta.youtubeId)
            ? CustomSongStore.LoadThumbnail(meta.youtubeId) : null;
        if (tex != null)
        {
            thumbTexture = tex;
            thumbSprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            thumbImage.sprite = thumbSprite;
            thumbImage.color = Color.white;
        }
        else
        {
            thumbImage.sprite = null;
            thumbImage.color = FieldColor;
        }

        string title = meta != null && !string.IsNullOrEmpty(meta.title) ? meta.DisplayName : "(제목 없음)";
        string artist = meta != null && !string.IsNullOrEmpty(meta.artist) ? "\n<size=24><color=#FFFFFF99>" + meta.artist + "</color></size>" : "";
        resultTitle.text = title + artist + "\n<size=22><color=#00FFCC>곡 목록에 추가되었습니다.</color></size>";
        resultRoot.SetActive(true);

        KeyboardScrollComponent scroll = FindFirstObjectByType<KeyboardScrollComponent>(FindObjectsInactive.Include);
        if (scroll != null) scroll.ReloadSongs();

        urlField.text = "";
        urlDraft = "";
    }

    void OnFailed(string error)
    {
        SetState(State.Failed);
        SetProgress(CUSTOM_SONG_STAGE.FAILED, 0f);
        ShowError(string.IsNullOrEmpty(error) ? "알 수 없는 오류가 발생했습니다." : error);

        // 403 등으로 토큰 상태가 바뀌었을 수 있다
        if (DevTokenProvider != null && !HasToken()) tokenExpanded = true;
        RefreshTokenSection();
    }

    void SelectDifficulty(string difficulty)
    {
        if (state == State.Busy) return;
        selectedDifficulty = difficulty;
        RefreshDifficultyButtons();
    }

    void ToggleTokenRow()
    {
        tokenExpanded = !tokenExpanded;
        RefreshTokenSection();
        if (tokenExpanded && tokenField != null && tokenField.interactable)
        {
            tokenField.Select();
            tokenField.ActivateInputField();
        }
    }

    void SaveToken()
    {
        if (DevTokenProvider == null || tokenField == null) return;
        string value = tokenField.text != null ? tokenField.text.Trim() : "";
        PlayerPrefsTokenProvider.Save(value);
        tokenField.text = "";
        tokenDraft = "";
        tokenField.DeactivateInputField();
        tokenFocusedLastFrame = false;
        if (value.Length > 0) tokenExpanded = false;
        RefreshTokenSection();
        if (errorText.gameObject.activeSelf && state != State.Busy && state != State.Failed)
            errorText.gameObject.SetActive(false);
    }

    IEnumerator FocusNextFrame()
    {
        // C 키로 연 경우 같은 프레임의 'c' 입력이 필드에 들어가지 않도록 한 프레임 뒤에 포커스
        yield return null;
        focusRoutine = null;
        if (!IsOpen) yield break;
        // 토큰이 없으면 토큰 입력부터
        InputField target = DevTokenProvider != null && !HasToken() && tokenExpanded ? tokenField : urlField;
        if (target == null || !target.interactable || !target.gameObject.activeInHierarchy) target = urlField;
        if (target == null || !target.interactable) yield break;
        target.Select();
        target.ActivateInputField();
    }

    void RestartFocus()
    {
        if (focusRoutine != null) StopCoroutine(focusRoutine);
        focusRoutine = null;
        if (IsOpen && isActiveAndEnabled) focusRoutine = StartCoroutine(FocusNextFrame());
    }

    // ===== 표시 =====

    void SetState(State next)
    {
        state = next;
        bool busy = next == State.Busy;

        urlField.interactable = !busy;
        submitButton.interactable = !busy;
        submitLabel.text = next == State.Failed ? "다시 시도" : "요청";
        secondaryLabel.text = busy ? "취소" : "닫기";
        RefreshDifficultyButtons();

        if (next == State.Idle)
        {
            errorText.gameObject.SetActive(false);
            stageText.gameObject.SetActive(false);
            progressRoot.SetActive(false);
            resultRoot.SetActive(false);
        }
        else if (next == State.Busy)
        {
            errorText.gameObject.SetActive(false);
            resultRoot.SetActive(false);
            stageText.gameObject.SetActive(true);
            progressRoot.SetActive(true);
        }
        else if (next == State.Failed)
        {
            resultRoot.SetActive(false);
            stageText.gameObject.SetActive(true);
            progressRoot.SetActive(false);
        }
        else // Done
        {
            errorText.gameObject.SetActive(false);
            stageText.gameObject.SetActive(true);
            progressRoot.SetActive(true);
        }

        if (!busy && IsOpen && isActiveAndEnabled && focusRoutine == null)
            focusRoutine = StartCoroutine(FocusNextFrame());
    }

    void SetProgress(CUSTOM_SONG_STAGE stage, float progress)
    {
        currentStage = stage;
        switch (stage)
        {
            case CUSTOM_SONG_STAGE.SUBMITTING:
                stageText.text = "요청 중...";
                break;
            case CUSTOM_SONG_STAGE.PROCESSING:
                // progress = 제출 후 경과 초. 다음 콜백 전까지는 로컬 시간으로 보간한다
                processingElapsed = Mathf.Max(0f, progress);
                processingReceivedAt = Time.unscaledTime;
                UpdateProcessingLabel();
                break;
            case CUSTOM_SONG_STAGE.DOWNLOADING:
                stageText.text = "다운로드 중...  " + Mathf.RoundToInt(Mathf.Clamp01(progress) * 100f) + "%";
                break;
            case CUSTOM_SONG_STAGE.DONE:
                stageText.text = "완료";
                break;
            default:
                stageText.text = "실패";
                break;
        }
        stageText.color = stage == CUSTOM_SONG_STAGE.FAILED ? ErrorColor
            : stage == CUSTOM_SONG_STAGE.DONE ? AccentColor : Color.white;

        if (IsIndeterminate(stage))
        {
            AnimateProgress();
        }
        else
        {
            float fill = stage == CUSTOM_SONG_STAGE.DONE ? 1f : Mathf.Clamp01(progress);
            progressFill.anchorMin = Vector2.zero;
            progressFill.anchorMax = new Vector2(fill, 1f);
        }
    }

    static bool IsIndeterminate(CUSTOM_SONG_STAGE stage) =>
        stage == CUSTOM_SONG_STAGE.SUBMITTING || stage == CUSTOM_SONG_STAGE.PROCESSING;

    void UpdateProcessingLabel()
    {
        float elapsed = processingElapsed + (Time.unscaledTime - processingReceivedAt);
        stageText.text = "서버 처리 중... " + Mathf.FloorToInt(elapsed) + "s";
    }

    // 서버가 진행률을 주지 않으므로 좌우로 오가는 막대로 표시
    void AnimateProgress()
    {
        float t = Mathf.PingPong(Time.unscaledTime * 0.9f, 1f);
        float x = Mathf.SmoothStep(0f, 1f, t) * (1f - IndeterminateWidth);
        progressFill.anchorMin = new Vector2(x, 0f);
        progressFill.anchorMax = new Vector2(x + IndeterminateWidth, 1f);
        if (currentStage == CUSTOM_SONG_STAGE.PROCESSING) UpdateProcessingLabel();
    }

    void RefreshDifficultyButtons()
    {
        if (difficultyButtons == null) return;
        bool busy = state == State.Busy;
        for (int k = 0; k < difficultyButtons.Length; k++)
        {
            Button b = difficultyButtons[k];
            if (b == null) continue;
            b.interactable = !busy;
            bool selected = ChartDifficulty.All[k] == selectedDifficulty;
            ((Image)b.targetGraphic).color = selected ? SelectedColor : ButtonColor;
            Text label = b.transform.Find("Label").GetComponent<Text>();
            label.color = selected ? Color.white : SubTextColor;
            label.fontStyle = selected ? FontStyle.Bold : FontStyle.Normal;
        }
    }

    bool HasToken()
    {
        PlayerPrefsTokenProvider p = DevTokenProvider;
        return p != null && !string.IsNullOrEmpty(p.GetAccessToken());
    }

    void RefreshTokenSection()
    {
        bool visible = DevTokenProvider != null;
        if (tokenHeader != null) tokenHeader.gameObject.SetActive(visible);
        if (tokenRow != null) tokenRow.gameObject.SetActive(visible && tokenExpanded);

        if (visible)
        {
            string token = DevTokenProvider.GetAccessToken();
            if (string.IsNullOrEmpty(token))
            {
                tokenStatusText.text = "토큰 없음";
                tokenStatusText.color = ErrorColor;
            }
            else
            {
                string tail = token.Length > 6 ? token.Substring(token.Length - 6) : token;
                tokenStatusText.text = "토큰 저장됨 (..." + tail + ")";
                tokenStatusText.color = AccentColor;
            }
            tokenToggleLabel.text = (tokenExpanded ? "[-]" : "[+]") + " 개발용 토큰";
        }

        Relayout();
    }

    void ShowInlineError(string message)
    {
        if (state == State.Failed || state == State.Done) SetState(State.Idle);
        ShowError(message);
    }

    void ShowError(string message)
    {
        errorText.text = message;
        errorText.gameObject.SetActive(true);
    }

    void ReleaseThumbnail()
    {
        if (thumbImage != null) thumbImage.sprite = null;
        if (thumbSprite != null) Destroy(thumbSprite);
        if (thumbTexture != null) Destroy(thumbTexture);
        thumbSprite = null;
        thumbTexture = null;
    }

    static bool IsFocused(InputField field) => field != null && field.isActiveAndEnabled && field.isFocused;

    // ===== UI 생성 =====

    void BuildModal(Font font)
    {
        RectTransform root = (RectTransform)transform;

        // 전체 화면 dim (뒤쪽 클릭 차단)
        modal = new GameObject("Modal", typeof(RectTransform), typeof(Image));
        RectTransform modalRt = (RectTransform)modal.transform;
        modalRt.SetParent(root, false);
        Stretch(modalRt);
        Image dim = modal.GetComponent<Image>();
        dim.color = DimColor;
        dim.raycastTarget = true;

        // 창 (높이는 Relayout에서 결정)
        window = CreateImage(modalRt, "Window", PanelColor).rectTransform;
        window.anchorMin = window.anchorMax = window.pivot = new Vector2(0.5f, 0.5f);
        window.sizeDelta = new Vector2(1040f, 900f);

        Text title = CreateText(window, "Title", "커스텀 곡 추가", font, 48, TextAnchor.MiddleLeft, Color.white);
        PlaceTop(title.rectTransform, Margin, 32f, 64f);

        Text desc = CreateText(window, "Description", "YouTube URL을 입력하면 서버가 채보, 썸네일, 음원을 만들어 줍니다.",
            font, 26, TextAnchor.MiddleLeft, SubTextColor);
        PlaceTop(desc.rectTransform, Margin, 100f, 36f);

        urlField = CreateInputField(window, "UrlInput", "https://www.youtube.com/watch?v=...", font, 30, 512);
        PlaceTop((RectTransform)urlField.transform, Margin, 152f, 84f);

        BuildDifficultyRow(font);
        BuildTokenSection(font);

        errorText = CreateText(window, "Error", "", font, 24, TextAnchor.UpperLeft, ErrorColor);
        errorText.horizontalOverflow = HorizontalWrapMode.Wrap;
        errorText.verticalOverflow = VerticalWrapMode.Truncate;

        stageText = CreateText(window, "Stage", "", font, 30, TextAnchor.MiddleLeft, Color.white);

        // 진행률 바: Fill의 anchorMin/Max.x로 표시 (스프라이트 불필요)
        Image barBg = CreateImage(window, "Progress", FieldColor);
        progressRoot = barBg.gameObject;
        Image fill = CreateImage(barBg.rectTransform, "Fill", AccentColor);
        progressFill = fill.rectTransform;
        progressFill.anchorMin = Vector2.zero;
        progressFill.anchorMax = new Vector2(0f, 1f);
        progressFill.pivot = new Vector2(0f, 0.5f);
        progressFill.offsetMin = progressFill.offsetMax = Vector2.zero;

        // 완료 결과: 썸네일 + 제목
        GameObject result = new GameObject("Result", typeof(RectTransform));
        resultRoot = result;
        RectTransform resultRt = (RectTransform)result.transform;
        resultRt.SetParent(window, false);

        thumbImage = CreateImage(resultRt, "Thumbnail", FieldColor);
        thumbImage.preserveAspect = true;
        RectTransform thumbRt = thumbImage.rectTransform;
        thumbRt.anchorMin = thumbRt.anchorMax = thumbRt.pivot = new Vector2(0f, 0.5f);
        thumbRt.sizeDelta = new Vector2(320f, 180f);
        thumbRt.anchoredPosition = Vector2.zero;

        resultTitle = CreateText(resultRt, "Title", "", font, 32, TextAnchor.MiddleLeft, Color.white);
        resultTitle.supportRichText = true;
        resultTitle.horizontalOverflow = HorizontalWrapMode.Wrap;
        RectTransform resultTitleRt = resultTitle.rectTransform;
        Stretch(resultTitleRt);
        resultTitleRt.offsetMin = new Vector2(352f, 0f);

        // 하단 버튼
        submitButton = CreateButton(window, "SubmitButton", "요청", font, new Vector2(280f, 84f), 34);
        submitLabel = submitButton.transform.Find("Label").GetComponent<Text>();
        RectTransform submitRt = (RectTransform)submitButton.transform;
        submitRt.anchorMin = submitRt.anchorMax = submitRt.pivot = new Vector2(1f, 0f);
        submitRt.anchoredPosition = new Vector2(-Margin, 40f);
        submitButton.GetComponent<Image>().color = new Color(0f, 1f, 0.8f, 0.35f);
        submitButton.onClick.AddListener(Submit);

        secondaryButton = CreateButton(window, "CloseButton", "닫기", font, new Vector2(220f, 84f), 34);
        secondaryLabel = secondaryButton.transform.Find("Label").GetComponent<Text>();
        RectTransform secRt = (RectTransform)secondaryButton.transform;
        secRt.anchorMin = secRt.anchorMax = secRt.pivot = new Vector2(1f, 0f);
        secRt.anchoredPosition = new Vector2(-Margin - 280f - 24f, 40f);
        secondaryButton.onClick.AddListener(OnSecondaryClicked);

        Text keys = CreateText(window, "KeyHint", "Enter: 요청   Esc: 닫기", font, 22, TextAnchor.MiddleLeft, SubTextColor);
        RectTransform keysRt = keys.rectTransform;
        keysRt.anchorMin = keysRt.anchorMax = keysRt.pivot = new Vector2(0f, 0f);
        keysRt.sizeDelta = new Vector2(360f, 84f);
        keysRt.anchoredPosition = new Vector2(Margin, 40f);

        tokenExpanded = DevTokenProvider != null && !HasToken();
        RefreshTokenSection();
        SetState(State.Idle);
    }

    // 난이도: EASY / NORMAL / HARD 토글 버튼 (기본 NORMAL)
    void BuildDifficultyRow(Font font)
    {
        RectTransform row = CreateContainer(window, "Difficulty");
        PlaceTop(row, Margin, 252f, 64f);

        Text label = CreateText(row, "Label", "난이도", font, 28, TextAnchor.MiddleLeft, SubTextColor);
        RectTransform labelRt = label.rectTransform;
        labelRt.anchorMin = new Vector2(0f, 0f);
        labelRt.anchorMax = new Vector2(0f, 1f);
        labelRt.pivot = new Vector2(0f, 0.5f);
        labelRt.sizeDelta = new Vector2(140f, 0f);
        labelRt.anchoredPosition = Vector2.zero;

        string[] all = ChartDifficulty.All;
        difficultyButtons = new Button[all.Length];
        for (int k = 0; k < all.Length; k++)
        {
            string difficulty = all[k];
            Button b = CreateButton(row, "Difficulty_" + difficulty, difficulty.ToUpperInvariant(), font,
                new Vector2(200f, 0f), 28);
            RectTransform rt = (RectTransform)b.transform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(200f, 0f);
            rt.anchoredPosition = new Vector2(140f + k * 216f, 0f);
            // 선택 표시는 Image 색으로 하므로 tint는 흰색 기준으로 둔다
            ColorBlock colors = b.colors;
            colors.normalColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.selectedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.highlightedColor = Color.white;
            b.colors = colors;
            b.onClick.AddListener(() => SelectDifficulty(difficulty));
            difficultyButtons[k] = b;
        }
    }

    // 개발용 토큰: 접이식 헤더(토글 + 상태) + 입력 행(InputField + 저장)
    void BuildTokenSection(Font font)
    {
        tokenHeader = CreateContainer(window, "TokenHeader");

        Button toggle = CreateButton(tokenHeader, "Toggle", "[+] 개발용 토큰", font, new Vector2(260f, 0f), 24);
        RectTransform toggleRt = (RectTransform)toggle.transform;
        toggleRt.anchorMin = new Vector2(0f, 0f);
        toggleRt.anchorMax = new Vector2(0f, 1f);
        toggleRt.pivot = new Vector2(0f, 0.5f);
        toggleRt.sizeDelta = new Vector2(260f, 0f);
        toggleRt.anchoredPosition = Vector2.zero;
        tokenToggleLabel = toggle.transform.Find("Label").GetComponent<Text>();
        toggle.onClick.AddListener(ToggleTokenRow);

        tokenStatusText = CreateText(tokenHeader, "Status", "", font, 24, TextAnchor.MiddleLeft, SubTextColor);
        RectTransform statusRt = tokenStatusText.rectTransform;
        Stretch(statusRt);
        statusRt.offsetMin = new Vector2(284f, 0f);

        tokenRow = CreateContainer(window, "TokenRow");

        tokenField = CreateInputField(tokenRow, "TokenInput", "issue_dev_token.py로 발급한 JWT를 붙여 넣기", font, 24, 8192);
        RectTransform fieldRt = (RectTransform)tokenField.transform;
        Stretch(fieldRt);
        fieldRt.offsetMax = new Vector2(-196f, 0f);

        tokenSaveButton = CreateButton(tokenRow, "SaveButton", "저장", font, new Vector2(180f, 0f), 28);
        RectTransform saveRt = (RectTransform)tokenSaveButton.transform;
        saveRt.anchorMin = new Vector2(1f, 0f);
        saveRt.anchorMax = new Vector2(1f, 1f);
        saveRt.pivot = new Vector2(1f, 0.5f);
        saveRt.sizeDelta = new Vector2(180f, 0f);
        saveRt.anchoredPosition = Vector2.zero;
        tokenSaveButton.onClick.AddListener(SaveToken);
    }

    // 토큰 영역 표시 여부에 따라 아래 요소와 창 높이를 다시 배치한다
    void Relayout()
    {
        if (window == null || errorText == null) return;

        float y = 332f; // 난이도 행(252..316) 아래
        if (tokenHeader != null && tokenHeader.gameObject.activeSelf)
        {
            PlaceTop(tokenHeader, Margin, y, 48f);
            y += 48f + 12f;
            if (tokenRow != null && tokenRow.gameObject.activeSelf)
            {
                PlaceTop(tokenRow, Margin, y, 64f);
                y += 64f + 12f;
            }
            y += 8f;
        }

        PlaceTop(errorText.rectTransform, Margin, y, 56f);
        y += 64f;
        PlaceTop(stageText.rectTransform, Margin, y, 44f);
        y += 52f;
        PlaceTop((RectTransform)progressRoot.transform, Margin, y, 14f);
        y += 30f;
        PlaceTop((RectTransform)resultRoot.transform, Margin, y, 180f);
        y += 180f;

        // 하단 버튼(40 + 84) + 여백
        window.sizeDelta = new Vector2(window.sizeDelta.x, y + 24f + 84f + 40f);
    }

    InputField CreateInputField(RectTransform parent, string name, string placeholderText, Font font, int fontSize, int characterLimit)
    {
        Image bg = CreateImage(parent, name, FieldColor);
        GameObject go = bg.gameObject;

        Text text = CreateText(bg.rectTransform, "Text", "", font, fontSize, TextAnchor.MiddleLeft, Color.white);
        text.supportRichText = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        RectTransform textRt = text.rectTransform;
        Stretch(textRt);
        textRt.offsetMin = new Vector2(24f, 8f);
        textRt.offsetMax = new Vector2(-24f, -8f);

        Text placeholder = CreateText(bg.rectTransform, "Placeholder", placeholderText, font, fontSize,
            TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.3f));
        placeholder.fontStyle = FontStyle.Italic;
        placeholder.raycastTarget = false;
        RectTransform phRt = placeholder.rectTransform;
        Stretch(phRt);
        phRt.offsetMin = textRt.offsetMin;
        phRt.offsetMax = textRt.offsetMax;

        InputField field = go.AddComponent<InputField>();
        field.targetGraphic = bg;
        field.textComponent = text;
        field.placeholder = placeholder;
        field.lineType = InputField.LineType.SingleLine;
        field.contentType = InputField.ContentType.Standard;
        field.characterLimit = characterLimit;
        field.caretColor = Color.white;
        field.customCaretColor = true;
        field.selectionColor = new Color(0f, 1f, 0.8f, 0.35f);
        return field;
    }

    static RectTransform CreateContainer(RectTransform parent, string name)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    // ===== 공용 UI 헬퍼 (Bootstrap에서도 사용) =====

    public static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    // 부모 상단 기준 가로 stretch (좌우 margin), top에서 y만큼 내려온 위치
    static void PlaceTop(RectTransform rt, float margin, float top, float height)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(margin, -top - height);
        rt.offsetMax = new Vector2(-margin, -top);
    }

    public static Image CreateImage(RectTransform parent, string name, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Image image = go.GetComponent<Image>();
        image.color = color;
        return image;
    }

    public static Text CreateText(RectTransform parent, string name, string value, Font font, int size,
        TextAnchor anchor, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        Text text = go.GetComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.alignment = anchor;
        text.color = color;
        text.text = value;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    public static Button CreateButton(RectTransform parent, string name, string label, Font font, Vector2 size, int fontSize)
    {
        Image bg = CreateImage(parent, name, ButtonColor);
        bg.rectTransform.sizeDelta = size;
        Button button = bg.gameObject.AddComponent<Button>();
        button.targetGraphic = bg;
        ColorBlock colors = button.colors;
        colors.normalColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        colors.highlightedColor = Color.white;
        colors.selectedColor = Color.white;
        colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        colors.colorMultiplier = 1f;
        button.colors = colors;

        Text text = CreateText(bg.rectTransform, "Label", label, font, fontSize, TextAnchor.MiddleCenter, Color.white);
        Stretch(text.rectTransform);
        return button;
    }
}
