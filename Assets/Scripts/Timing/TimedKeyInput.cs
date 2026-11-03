// ─────────────────────────────────────────────────────────────
// 키 입력에 실제로 눌린 시각(timestamp)을 붙인다.
//
// Input.GetKeyDown은 프레임 단위라 "이번 프레임 어딘가에서 눌림"만 알 수 있다.
// 60fps라면 판정 시각이 최대 16.7ms 흔들린다.
// Input System 이벤트는 OS가 입력을 만든 시각(InputEvent.time)을 들고 있다.
// 이 시각은 Time.realtimeSinceStartup과 같은 시간축이므로 SongClock으로 곡 시각으로 바꿀 수 있다.
//
// 기존 코드와의 관계
//   - "눌렸는가"는 계속 Input.GetKeyDown/GetKeyUp(레거시, KeyCode)으로 판단한다. 키 설정, KeyCode 저장 방식은 그대로다.
//   - "언제 눌렸는가"만 같은 프레임에 들어온 Input System 이벤트에서 찾는다.
//   - 매칭되는 이벤트가 없으면 (배열이 다른 키보드, 미지원 키) 현재 프레임 시각을 쓴다. 기존 동작과 같다.
// activeInputHandler = Both(2)에서 동작한다.
//
// 참고: Input System InputAction.CallbackContext.time / InputEvent.time
//   https://docs.unity3d.com/Packages/com.unity.inputsystem@1.12/api/UnityEngine.InputSystem.InputAction.CallbackContext.html
// ─────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

[DefaultExecutionOrder(-999)]
public class TimedKeyInput : MonoBehaviour
{
    struct KeyRecord
    {
        public KeyCode code;
        public bool down;
        public double time;
    }

    const double MaxAgeSec = 0.25; // 이보다 오래된 timestamp는 믿지 않는다

    static readonly List<KeyRecord> frameRecords = new List<KeyRecord>();
    static Dictionary<Key, KeyCode> keyMap;

    // ===== 공개 API =====

    public static bool GetKeyDown(KeyCode key, out double realtime) => Get(key, true, out realtime);
    public static bool GetKeyUp(KeyCode key, out double realtime) => Get(key, false, out realtime);

    static bool Get(KeyCode key, bool down, out double realtime)
    {
        realtime = 0;
        if (down ? !Input.GetKeyDown(key) : !Input.GetKeyUp(key)) return false;
        realtime = FindTimestamp(key, down);
        return true;
    }

    static double FindTimestamp(KeyCode key, bool down)
    {
        double now = Time.realtimeSinceStartupAsDouble;
        for (int k = 0; k < frameRecords.Count; k++)
        {
            KeyRecord r = frameRecords[k];
            if (r.code != key || r.down != down) continue;
            if (r.time > now || now - r.time > MaxAgeSec) continue;
            return r.time;
        }
        return now;
    }

    // ===== 이벤트 수집 =====

    void OnEnable() => InputSystem.onEvent += OnInputEvent;
    void OnDisable() => InputSystem.onEvent -= OnInputEvent;

    // 이번 프레임의 Update가 모두 끝난 뒤 비운다.
    // 기본 설정(ProcessEventsInDynamicUpdate)에서 이벤트는 Update보다 먼저 처리된다.
    void LateUpdate() => frameRecords.Clear();

    static void OnInputEvent(InputEventPtr eventPtr, InputDevice device)
    {
        if (!(device is Keyboard)) return;
        if (!eventPtr.IsA<StateEvent>() && !eventPtr.IsA<DeltaStateEvent>()) return;

        var map = KeyMap();
        foreach (InputControl control in eventPtr.EnumerateChangedControls(device))
        {
            if (!(control is KeyControl keyControl)) continue;
            if (!map.TryGetValue(keyControl.keyCode, out KeyCode code)) continue;
            if (!keyControl.ReadValueFromEvent(eventPtr, out float value)) continue;
            frameRecords.Add(new KeyRecord { code = code, down = value >= 0.5f, time = eventPtr.time });
        }
    }

    // ===== Key(Input System) → KeyCode(레거시) =====

    static Dictionary<Key, KeyCode> KeyMap()
    {
        if (keyMap != null) return keyMap;
        keyMap = new Dictionary<Key, KeyCode>();
        foreach (Key key in Enum.GetValues(typeof(Key)))
        {
            if (key == Key.None || keyMap.ContainsKey(key)) continue;
            string name = LegacyName(key.ToString());
            if (Enum.TryParse(name, true, out KeyCode code)) keyMap[key] = code;
        }
        return keyMap;
    }

    static string LegacyName(string name)
    {
        if (name.StartsWith("Digit")) return "Alpha" + name.Substring(5);
        if (name.StartsWith("Numpad")) return "Keypad" + name.Substring(6);
        switch (name)
        {
            case "Enter": return "Return";
            case "LeftCtrl": return "LeftControl";
            case "RightCtrl": return "RightControl";
            case "LeftMeta": return "LeftCommand";
            case "RightMeta": return "RightCommand";
            case "ContextMenu": return "Menu";
            case "PrintScreen": return "Print";
            default: return name;
        }
    }
}
