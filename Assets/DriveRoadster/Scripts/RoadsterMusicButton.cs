using UdonSharp;
using UnityEngine;
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class RoadsterMusicButton : UdonSharpBehaviour
{
    public RoadsterMusic music;
    public int action;
    public override void Interact()
    {
        if(music==null)return;
        if(action==0)music.ToggleMusic();
        else if(action==1)music.NextTrack();
        else if(action==2)music.VolumeDown();
        else music.VolumeUp();
    }
}
