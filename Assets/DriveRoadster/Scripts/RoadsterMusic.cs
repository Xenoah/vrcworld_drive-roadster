using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class RoadsterMusic : UdonSharpBehaviour
{
    public AudioSource source;
    public AudioClip[] playlist;
    public Text display;
    public bool enabledByUser=true;
    public float volume=.18f;
    private int track;
    void Start(){PlayTrack();}
    void Update()
    {
        if(source==null)return;
        source.volume=Mathf.MoveTowards(source.volume,enabledByUser ? volume : 0,Time.deltaTime*.3f);
        if(enabledByUser && !source.isPlaying && playlist!=null && playlist.Length>0) NextTrack();
    }
    private void PlayTrack()
    {
        if(source==null || playlist==null || playlist.Length==0)return;
        source.clip=playlist[track];source.loop=false;
        if(enabledByUser)source.Play();
        RefreshDisplay();
    }
    public void ToggleMusic()
    {
        enabledByUser=!enabledByUser;
        if(enabledByUser) { source.UnPause(); if(!source.isPlaying)source.Play(); }
        else source.Pause();
        RefreshDisplay();
    }
    public void NextTrack(){if(playlist==null || playlist.Length==0)return;track=(track+1)%playlist.Length;PlayTrack();}
    public void VolumeUp(){volume=Mathf.Min(.6f,volume+.06f);RefreshDisplay();}
    public void VolumeDown(){volume=Mathf.Max(0,volume-.06f);RefreshDisplay();}
    private void RefreshDisplay(){if(display!=null)display.text="AIRFIELD RADIO / LOCAL\n"+(enabledByUser ? "ON" : "OFF")+"    TRACK "+(track+1)+" / "+playlist.Length+"    VOL "+Mathf.RoundToInt(volume*100)+"%";}
}
