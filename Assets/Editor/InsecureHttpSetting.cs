using UnityEditor;
using UnityEngine;

// 커스텀 곡 로컬 서버(http://localhost:8000)는 HTTP라서 "Allow downloads over HTTP"가 필요하다.
// Not Allowed면 Editor 로드 시 Development builds only로 바꾼다.
// 이 옵션에서 Release 빌드는 여전히 HTTP를 차단하므로 운영(https)에는 영향이 없다.
[InitializeOnLoad]
static class InsecureHttpSetting
{
    static InsecureHttpSetting()
    {
        if (PlayerSettings.insecureHttpOption != InsecureHttpOption.NotAllowed) return;
        PlayerSettings.insecureHttpOption = InsecureHttpOption.DevelopmentOnly;
        AssetDatabase.SaveAssets();
        Debug.Log("[InsecureHttpSetting] Allow downloads over HTTP → Development builds only");
    }
}
