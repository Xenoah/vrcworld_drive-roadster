using UdonSharp;
using UnityEngine;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class RoadsterAction : UdonSharpBehaviour
{
    public RoadsterVehicle vehicle;
    public RoadsterRun run;
    public int action; // 0 recover, 1 begin time trial, 2 exit
    public override void Interact()
    {
        if (action == 0 && vehicle.IsLocalDriver()) vehicle.Recover();
        else if (action == 1) run.BeginRun();
        else if (action == 2) vehicle.ExitVehicle();
    }
}
