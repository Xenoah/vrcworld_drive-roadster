using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class RoadsterAudio : UdonSharpBehaviour
{
    public RoadsterVehicle vehicle;
    public AudioSource motor, rolling, gas, impact;
    private float nextTick;
    private bool wasAir;
    private float previousSpeed;
    void Update()
    {
        if (Time.time < nextTick || !Utilities.IsValid(vehicle)) return;
        nextTick=Time.time+.05f;
        bool near=Utilities.IsValid(Networking.LocalPlayer) && Vector3.Distance(Networking.LocalPlayer.GetPosition(),transform.position)<110f;
        bool active=near && vehicle.driverId>=0;
        float speed=vehicle.GetDisplaySpeed();
        float pedal=Mathf.Max(0,vehicle.GetDisplayThrottle());
        float jet=0;
        for(int i=0;i<vehicle.thrusterDemand.Length;i++) jet=Mathf.Max(jet,vehicle.thrusterDemand[i]);
        bool air=vehicle.motionMode>=3;
        SetLoop(motor,active ? Mathf.Clamp01(speed/8f)*(.12f+.19f*pedal) : 0,.55f+speed*.046f);
        SetLoop(rolling,active && !air ? Mathf.Clamp01(speed/55f)*.27f : 0,.7f+speed*.012f);
        SetLoop(gas,active ? Mathf.Sqrt(jet)*.65f : 0,.88f+jet*.16f);
        if(active && wasAir && !air && previousSpeed>2 && Utilities.IsValid(impact)) impact.Play();
        wasAir=air; previousSpeed=speed;
    }
    private void SetLoop(AudioSource source,float volume,float pitch)
    {
        if(!Utilities.IsValid(source))return;
        source.volume=Mathf.Lerp(source.volume,volume,.45f);source.pitch=pitch;
        if(volume>.002f && !source.isPlaying)source.Play();
        if(volume<=.002f && source.volume<.003f && source.isPlaying)source.Stop();
    }
}
