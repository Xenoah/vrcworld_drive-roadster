using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class RoadsterRun : UdonSharpBehaviour
{
    public RoadsterVehicle vehicle;
    public Transform timeTrialStart;
    public Transform[] gates;
    public float[] gateHalfWidths;
    public float[] gateHalfHeights;
    public int[] requiredModes; // 0 any, 1 ground, 2 wall, 3 flight
    public int flightStartGate = -1;
    public int flightEndGate = -1;
    public UnityEngine.UI.Text display;
    public UnityEngine.UI.Text cockpitDisplay;
    public UnityEngine.UI.Text speedText, modeText, driveText, lapText, systemText, attitudeText, feedbackText, helpText, profileText;
    public UnityEngine.UI.Text pilotModeText, landingToggleText, inputText, guideToggleText;
    public UnityEngine.UI.Image[] thrusterBars;
    public RectTransform attitudeLine;
    public string courseVersion = "coast-v1";
    [UdonSynced] public bool running;
    [UdonSynced] public int nextGate;
    [UdonSynced] public int runId;
    [UdonSynced] public int driverGeneration;
    [UdonSynced] public double startedAt;
    [UdonSynced] public float lastTime;
    [UdonSynced] public string status = "FREE DRIVE / T: time trial";
    [UdonSynced] private string[] names = new string[5];
    [UdonSynced] private float[] times = new float[5];
    private Vector3 previousPosition;
    private double lastGateAt;
    private float nextDisplay;
    private bool ready;
    private float personalBest;

    void Start() { for (int i = 0; i < names.Length; i++) if (names[i] == null) names[i] = ""; previousPosition = vehicle.body.position; ready = true; }

    public void BeginRun()
    {
        if (!vehicle.IsLocalDriver()) { vehicle.DriverOnlyFeedback(); return; }
        Transform staging = timeTrialStart != null ? timeTrialStart : vehicle.spawn;
        if (Vector3.Distance(vehicle.body.position, staging.position) > 18f || vehicle.body.velocity.magnitude > 1f)
        { status = "Stop in the start box before starting"; RequestSerialization(); return; }
        // Parking bays are independent of the course start; never teleport a run into another car.
        running = true; nextGate = 0; runId++; driverGeneration = vehicle.generation;
        startedAt = Networking.GetServerTimeInSeconds(); lastGateAt = startedAt;
        previousPosition = vehicle.body.position;
        status = "TIME ATTACK"; RequestSerialization();
    }

    public void CancelRun()
    {
        if (!Networking.IsOwner(gameObject)) return;
        if (running) status = "INVALID / reset or driver change";
        running = false; nextGate = 0; RequestSerialization();
    }

    public void SamplePosition()
    {
        if (!ready || !vehicle.IsLocalDriver()) return;
        Vector3 current = vehicle.body.position;
        if (running)
        {
            if (driverGeneration != vehicle.generation || Vector3.Distance(current, previousPosition) > 15f)
            { status = "INVALID / discontinuity"; running = false; RequestSerialization(); }
            else if (nextGate >= 0 && nextGate < gates.Length)
            {
                Transform gate = gates[nextGate];
                Vector3 a = gate.InverseTransformPoint(previousPosition);
                Vector3 b = gate.InverseTransformPoint(current);
                if (a.z < 0f && b.z >= 0f)
                {
                    float t = -a.z / (b.z - a.z);
                    Vector3 crossing = Vector3.Lerp(a, b, t);
                    if (Mathf.Abs(crossing.x) <= gateHalfWidths[nextGate] && Mathf.Abs(crossing.y) <= gateHalfHeights[nextGate]) PassGate();
                }
            }
        }
        // Flight gates work in free drive too; tests use swept plane rather than thin trigger collisions.
        if (!running && Vector3.Distance(current, previousPosition) <= 15f)
        {
            TryFreeFlightGate(flightStartGate, previousPosition, current, true);
            TryFreeFlightGate(flightEndGate, previousPosition, current, false);
        }
        previousPosition = current;
    }

    private void TryFreeFlightGate(int index, Vector3 from, Vector3 to, bool enable)
    {
        if (index < 0 || index >= gates.Length) return;
        Vector3 a = gates[index].InverseTransformPoint(from);
        Vector3 b = gates[index].InverseTransformPoint(to);
        if (a.z >= 0f || b.z < 0f) return;
        Vector3 p = Vector3.Lerp(a, b, -a.z / (b.z - a.z));
        if (Mathf.Abs(p.x) > gateHalfWidths[index] || Mathf.Abs(p.y) > gateHalfHeights[index]) return;
        if (enable) vehicle.EnableFlight(); else vehicle.DisableFlight();
    }

    private void PassGate()
    {
        double now = Networking.GetServerTimeInSeconds();
        float minimumInterval = nextGate > 0 ? Vector3.Distance(gates[nextGate - 1].position, gates[nextGate].position) / vehicle.maxSpeed * 0.75f : 0.10f;
        if (now - lastGateAt < minimumInterval || (requiredModes[nextGate] > 0 && vehicle.motionMode != requiredModes[nextGate]))
        { running = false; status = "INVALID / gate time or drive mode"; RequestSerialization(); return; }
        lastGateAt = now;
        if(nextGate==0)startedAt=now;
        if (nextGate == flightStartGate) vehicle.EnableFlight();
        if (nextGate == flightEndGate) vehicle.DisableFlight();
        nextGate++;
        if (nextGate == gates.Length)
        {
            running = false; lastTime = (float)(now - startedAt);
            status = "FINISH / return to start box to retry";
            InsertResult(Networking.LocalPlayer.displayName, lastTime);
        }
        RequestSerialization();
    }

    private void InsertResult(string playerName, float seconds)
    {
        // Session-only casual board. Not a trusted anti-cheat leaderboard.
        int insert = -1;
        for (int i = 0; i < times.Length; i++)
            if (insert < 0 && (times[i] <= 0f || seconds < times[i])) insert = i;
        if (personalBest <= 0f || seconds < personalBest) personalBest = seconds;
        if (insert < 0) return;
        for (int i = times.Length - 1; i > insert; i--)
        { times[i] = times[i - 1]; names[i] = names[i - 1]; }
        times[insert] = seconds;
        names[insert] = playerName.Length > 24 ? playerName.Substring(0, 24) : playerName;
    }

    public void InvalidateFlightBounds()
    {
        if (!Networking.IsOwner(gameObject) || !running) return;
        running = false; status = "INVALID / flight bounds"; RequestSerialization();
    }

    void Update()
    {
        if (Time.time < nextDisplay) return;
        nextDisplay = Time.time + (vehicle.IsLocalDriver() ? 0.1f : .5f);
        if(!vehicle.IsLocalDriver())return;
        float elapsed = running ? (float)(Networking.GetServerTimeInSeconds() - startedAt) : lastTime;
        string text = "DRIVE / ROADSTER\n" + status + "\n" + elapsed.ToString("F2") + " s  |  Gate " + nextGate + "/" + gates.Length;
        if (personalBest > 0f) text += "\nMY SESSION BEST " + personalBest.ToString("F2");
        text += "\nINSTANCE RECORDS (unverified)";
        for (int i = 0; i < times.Length; i++)
            if (times[i] > 0f) text += "\n" + (i + 1) + ". " + names[i] + "  " + times[i].ToString("F2");
        if (vehicle.showDiagnostics) text += "\nDriver " + vehicle.driverId + " gen " + vehicle.generation + " run " + runId + "\nLast send " + vehicle.lastSerializedBytes + " B / ok " + vehicle.lastSerializationSucceeded + " / failed " + vehicle.failedSerializations;
        if(Utilities.IsValid(display))display.text = text;
        UpdateCockpit(elapsed);
        if (Utilities.IsValid(cockpitDisplay)) cockpitDisplay.text = (vehicle.GetDisplaySpeed() * 3.6f).ToString("F0") + " km/h  |  MODE " + vehicle.motionMode + "\n" + elapsed.ToString("F2") + " s  CP " + nextGate + "/" + gates.Length + "\n" + status;
    }
    private void UpdateCockpit(float elapsed)
    {
        if (!Utilities.IsValid(speedText)) return;
        float speed=vehicle.GetDisplaySpeed()*3.6f;
        int mode=vehicle.motionMode;
        string modeName=mode==0 ? "PARKED" : mode==1 ? "ROAD" : mode==2 ? "WALL / BANK" : mode==3 ? "FLIGHT" : "AIRBORNE";
        speedText.text=speed.ToString("F0"); modeText.text=modeName;
        modeText.color=mode>=3 ? new Color(.35f,.8f,1f) : mode==2 ? new Color(1f,.73f,.25f) : new Color(.55f,1f,.78f);
        float pedal=vehicle.GetDisplayThrottle();
        driveText.text=(pedal<-.05f ? "BRAKE" : "THRUST")+"  "+Mathf.RoundToInt(Mathf.Abs(pedal)*100)+"%";
        lapText.text=(running ? "LAP 1 / 1   " : "FREE DRIVE   ")+elapsed.ToString("F2")+" s\nCP "+nextGate+" / "+gates.Length+"    BEST "+(personalBest>0 ? personalBest.ToString("F2")+" s" : "--");
        Vector3 landing=vehicle.GetLandingTelemetry();
        string landingState=!vehicle.landingAssistEnabled ? "LANDING  OFF" : mode<3 ? "LANDING  STANDBY" : landing.z>.5f ? "LANDING  BRAKING "+landing.z.ToString("F1") : Vector3.Dot(transform.up,Vector3.up)<.35f ? "LANDING  CHECK ATTITUDE" : "LANDING  ARMED";
        float fanG=vehicle.GetFanAcceleration()/9.81f;
        systemText.text=(vehicle.manualFlight ? "MANUAL / RATE DAMPING" : "AUTO / THRUSTER LEVEL HOLD")+"\n"+landingState+"\nFAN "+(fanG>.01f ? fanG.ToString("F1")+" G / SURFACE GRIP" : "STANDBY");
        float pitch=Mathf.Asin(Mathf.Clamp(transform.forward.y,-1f,1f))*Mathf.Rad2Deg;
        float roll=Mathf.Atan2(transform.right.y,transform.up.y)*Mathf.Rad2Deg;
        attitudeText.text="PITCH "+pitch.ToString("F0")+"  ROLL "+roll.ToString("F0")+"\nH "+(landing.x<0 ? "--" : landing.x.ToString("F1"))+" m  DOWN "+landing.y.ToString("F1")+" m/s";
        if (Utilities.IsValid(attitudeLine)) attitudeLine.localRotation=Quaternion.Euler(0,0,-roll);
        if (thrusterBars!=null) for(int i=0;i<thrusterBars.Length;i++)
        {
            float demand=vehicle.thrusterDemand[i];
            thrusterBars[i].rectTransform.localScale=new Vector3(1,Mathf.Max(.025f,demand),1);
            thrusterBars[i].color=demand>.03f ? new Color(.3f,.85f,1f) : new Color(.13f,.22f,.27f);
        }
        feedbackText.text=vehicle.GetControlMessage();
        if (running) feedbackText.text+="\n"+status;
        else if (status.StartsWith("INVALID") || status.StartsWith("Stop") || status.StartsWith("FINISH")) feedbackText.text+="\n"+status;
        profileText.text="FEEL / "+vehicle.GetProfileName()+" [C]";
        if(Utilities.IsValid(pilotModeText)) pilotModeText.text=(vehicle.manualFlight ? "MANUAL" : "AUTO")+" [F]";
        if(Utilities.IsValid(landingToggleText)) landingToggleText.text="LANDING "+(vehicle.landingAssistEnabled ? "ON" : "OFF")+" [L]";
        bool vr=vehicle.IsVRControlGuide();
        if(Utilities.IsValid(inputText)) inputText.text=vehicle.GetInputName().StartsWith("VR") ? "VR / CONTROLLERS\nDrive: right index / no grip needed" : "DESKTOP / KEYBOARD\nPgUp / PgDn: view height";
        if(Utilities.IsValid(guideToggleText))guideToggleText.text=(vr ? "VR GUIDE" : "PC GUIDE")+"\nPC / VR";
        string help=vr ? "R INDEX: forward / L INDEX: brake\nDrive / steer: no grip needed\nLeft stick X: steering / yaw (Y unused)\nR GRIP near yoke: hold attitude control\nHold JUMP + yoke: continuous lift\nTouch default: right A = mapped JUMP\nAUTO: thrusters keep pitch / roll level\nMANUAL: wrist tilt = pitch / roll\nDrop yoke: release trigger, press again\nPanel Interact: release inputs to resume\nPanel: AUTO / LANDING / VIEW / EXIT\nPC / VR button: switch this guide" : "Space HOLD: jump / continuous lift\nRelease Space: lift off, then descend\nAUTO: thrusters keep pitch / roll level\nGROUND + AUTO: W/S drive / brake\nA/D steer / yaw; keys up: coast\nMANUAL AIR: W/S pitch, Q/E roll\nCtrl forward thrust / Shift brake\nAfter takeoff: release W/S, A/D, Q/E\nF Auto/Manual / L landing assist\nPgUp/PgDn view / Home default\nFAN: automatic on slopes / up to 2 G\nT time attack / X exit / H help";
        helpText.text=vehicle.showControlHelp ? help : "HELP H / INTERACT TO SHOW CONTROLS\nPC / VR switches the guide\nEXIT always returns to the paddock";
    }

}
