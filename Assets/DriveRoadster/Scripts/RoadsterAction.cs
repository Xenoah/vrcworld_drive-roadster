using UdonSharp;
using UnityEngine;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class RoadsterAction : UdonSharpBehaviour
{
    public RoadsterVehicle vehicle;
    public RoadsterRun run;
    public int action; // 0 reset request, 1 time attack, 2 safe exit, 3 steering profile, 4 help, 5 board
    public Transform visualCap;
    public float pressTravel = 8f; // Canvas units, .00272 m at the generated panel scale
    private Vector3 restPosition;
    private float releaseAt;
    void Start() { if (visualCap!=null) restPosition=visualCap.localPosition; }
    void Update() { if (visualCap!=null && Time.time>releaseAt) visualCap.localPosition=restPosition; }
    public override void Interact()
    {
        if (visualCap!=null) { visualCap.localPosition=restPosition+Vector3.forward*pressTravel; releaseAt=Time.time+.16f; }
        if (action==5) { vehicle.Interact(); return; }
        if (action==4) { vehicle.ToggleControlHelp(); return; }
        if (!vehicle.IsLocalDriver()) { vehicle.DriverOnlyFeedback(); return; }
        if (action==0) vehicle.RequestRecover();
        else if (action==1) run.BeginRun();
        else if (action==2) vehicle.ExitVehicle();
        else if (action==3) vehicle.CycleSensitivity();
    }
}
