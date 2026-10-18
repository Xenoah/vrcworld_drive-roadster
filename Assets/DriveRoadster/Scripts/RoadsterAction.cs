using UdonSharp;
using UnityEngine;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class RoadsterAction : UdonSharpBehaviour
{
    public RoadsterVehicle vehicle;
    public RoadsterRun run;
    public int action; // 0 reset, 1 time attack, 2 exit, 3 feel, 4 help, 5 board, 6 flight, 7 landing, 8 view down, 9 up, 10 default, 11 PC/VR guide
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
        vehicle.SuspendForPanel();
        if (action==4) { vehicle.ToggleControlHelp(); return; }
        if (action==11) { vehicle.ToggleGuideDevice(); return; }
        if (action==2) { vehicle.ExitVehicle(); return; } // Escape must survive an authority mismatch.
        if (!vehicle.IsLocalDriver()) { vehicle.DriverOnlyFeedback(); return; }
        if (action==0) vehicle.RequestRecover();
        else if (action==1) run.BeginRun();
        else if (action==3) vehicle.CycleSensitivity();
        else if (action==6) vehicle.ToggleFlightMode();
        else if (action==7) vehicle.ToggleLandingAssist();
        else if (action==8) vehicle.ViewDown();
        else if (action==9) vehicle.ViewUp();
        else if (action==10) vehicle.ViewReset();
    }
}
