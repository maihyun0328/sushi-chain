using System.Collections.Generic;
using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager i;
    public AudioSource music;
    public AudioSource UI;
    public AudioSource VFX;
    [SerializeField] public List<AudioClip> songs;
    [SerializeField] public AudioClip BGM;
    void Awake()
    {
        i = this;
    }
    void Start()
    {
        
    }
    public void SetBGM()
    {
        music.clip = BGM;
    }
    public void ChangeSong(AudioClip audio)
    {
        music.clip = audio;
    }
    public void PlayUI(AudioClip audio)
    {
        UI.PlayOneShot(audio);
    }
    public void PlayVFX(AudioClip audio)
    {
        VFX.PlayOneShot(audio);
    }
    public void PlaySong()
    {
        music.Play();
    }
    public void StopSong()
    {
        music.Stop();
    }
}
