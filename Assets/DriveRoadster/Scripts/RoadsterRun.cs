using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class RoadsterRun : UdonSharpBehaviour
{
    public RoadsterVehicle vehicle;
    public Transform[] gates;
    public float[] gateHalfWidths;
    public float[] gateHalfHeights;
    public int[] requiredModes; // 0 any, 1 ground, 2 wall, 3 flight
    public int flightStartGate = -1;
    public int flightEndGate = -1;
    public UnityEngine.UI.Text display;
    public UnityEngine.UI.Text cockpitDisplay;
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
        if (!vehicle.IsLocalDriver()) return;
        if (Vector3.Distance(vehicle.body.position, vehicle.spawn.position) > 8f || vehicle.body.velocity.magnitude > 1f)
        { status = "Stop in the start box before starting"; RequestSerialization(); return; }
        vehicle.Recover();
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
        if (!Utilities.IsValid(display) || Time.time < nextDisplay) return;
        nextDisplay = Time.time + 0.1f;
        float elapsed = running ? (float)(Networking.GetServerTimeInSeconds() - startedAt) : lastTime;
        string text = "DRIVE / ROADSTER\n" + status + "\n" + elapsed.ToString("F2") + " s  |  Gate " + nextGate + "/" + gates.Length;
        if (personalBest > 0f) text += "\nMY SESSION BEST " + personalBest.ToString("F2");
        text += "\nINSTANCE RECORDS (unverified)";
        for (int i = 0; i < times.Length; i++)
            if (times[i] > 0f) text += "\n" + (i + 1) + ". " + names[i] + "  " + times[i].ToString("F2");
        if (vehicle.showDiagnostics) text += "\nDriver " + vehicle.driverId + " gen " + vehicle.generation + " run " + runId + "\nLast send " + vehicle.lastSerializedBytes + " B / ok " + vehicle.lastSerializationSucceeded + " / failed " + vehicle.failedSerializations;
        display.text = text;
        if (Utilities.IsValid(cockpitDisplay)) cockpitDisplay.text = (vehicle.GetDisplaySpeed() * 3.6f).ToString("F0") + " km/h  |  MODE " + vehicle.motionMode + "\n" + elapsed.ToString("F2") + " s  CP " + nextGate + "/" + gates.Length + "\n" + status;
    }
}
