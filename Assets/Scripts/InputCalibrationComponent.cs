using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class InputCalibrationComponent : MonoBehaviour
{
    const double BEAT_INTERVAL  = 0.5;
    const int COUNT = 20;

    double firstBeatTime;
    double lastBeatTime = -1;
    double timer;
    int scheduledCount;
    List<double> player_input = new List<double>();

    void OnEnable()
    {
        player_input.Clear();
        scheduledCount = 0;
        lastBeatTime = -1;
        firstBeatTime = AudioSettings.dspTime;
        timer = firstBeatTime + BEAT_INTERVAL;
        AudioManager.i.StopSong();
        AudioManager.i.Metronome.Stop();
    }

    // Update is called once per frame
    void Update()
    {
        if(scheduledCount >= COUNT)
        {
            Finish();
            return;
        }
        if(AudioSettings.dspTime >= timer - 0.1) 
        {
            AudioManager.i.Metronome.PlayScheduled(timer);
            timer += BEAT_INTERVAL;
        }
        // 프레임 시각 대신 키가 실제로 눌린 시각을 dsp 시간축으로 바꿔 기록한다
        if(TimedKeyInput.GetKeyDown(KeyCode.Space, out double realtime)) RecordInput(SongClock.i.RealtimeToDsp(realtime));
    }
    void RecordInput(double inputTime)
    {
        double elapsed = inputTime - firstBeatTime;
        double nearestBeat = math.round(elapsed / BEAT_INTERVAL) * BEAT_INTERVAL;
        if(lastBeatTime == -1) lastBeatTime = nearestBeat;
        else if(lastBeatTime == nearestBeat) return;
        lastBeatTime = nearestBeat;
        ++scheduledCount;
        double result = elapsed - nearestBeat;
        player_input.Add(result);
    }
    void Finish()
    {
        player_input.Sort();
        GameManager.i.InputCalibration = COUNT % 2 == 0 ? (player_input[(COUNT / 2) - 1] + player_input[COUNT / 2]) / 2 : player_input[COUNT / 2];
        UIManager.i.Options.SetActive(true);
        gameObject.SetActive(false);
    }
    void OnDisable()
    {
        AudioManager.i.Metronome.Stop();
    }
}