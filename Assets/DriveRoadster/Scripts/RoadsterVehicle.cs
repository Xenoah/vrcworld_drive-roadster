using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common;

// One authority for seats, physics and snapshots. No VRCObjectSync on this object.
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class RoadsterVehicle : UdonSharpBehaviour
{
    public Rigidbody body;
    public VRC.SDK3.Components.VRCStation station;
    public Transform spawn;
    public Transform safeExit;
    public Transform[] suspensionPoints;
    public Transform[] wheels;
    public LayerMask roadMask = 1;
    public RoadsterRun run;
    public bool showDiagnostics;
    [HideInInspector] public int lastSerializedBytes;
    [HideInInspector] public bool lastSerializationSucceeded;
    [HideInInspector] public int failedSerializations;
    // Game assistance, not a reconstruction of any real vehicle's internal mechanism.
    public ParticleSystem[] thrusterJets;
    public ParticleSystem[] groundMist;
    public ParticleSystem[] thrusterSmoke;
    public bool landingAssistEnabled = true;
    public float attitudeStrength = 9f;
    public float attitudeDamping = 4.5f;
    public float maxAttitudeAcceleration = 10f;
    public float landingProbeDistance = 12f;
    public float landingTargetSpeed = 1.3f;
    public float maxLandingAcceleration = 42f;
    public float maxReboundSpeed = .65f;
    [HideInInspector] public float groundClearance = -1f;
    [HideInInspector] public float closingSpeed;
    [HideInInspector] public float landingAcceleration;
    [HideInInspector] public float lastTouchdownSpeed;
    [HideInInspector] public int touchdownCount;
    [HideInInspector] public Vector3 attitudeCommand;
    [HideInInspector] public float[] thrusterDemand = new float[7];
    [UdonSynced] private int netThrusterA;
    [UdonSynced] private int netThrusterB;
    private float[] visibleThrust = new float[7];
    private float[] emissionCarry = new float[18];
    private bool wasGrounded;
    private float contactTime = -100f;
    private float previousClosingSpeed;
    private Vector3 landingNormal = Vector3.up;
    public float maxSpeed = 65f;
    public float acceleration = 18f;
    public float brakeAcceleration = 32f;
    public float springStrength = 42f;
    public float springDamping = 7f;
    public float rideDistance = 0.6f;
    public float syncInterval = 0.12f;
    public float flightCeiling = 100f;
    public Vector3 flightBoundsCenter;
    public Vector3 flightBoundsSize = new Vector3(60f, 80f, 180f);
    [UdonSynced] public int driverId = -1;
    [UdonSynced] public int generation;
    [UdonSynced] public int motionMode; // 0 parked, 1 road, 2 bank/wall, 3 flight
    [UdonSynced] private Vector3 netPosition;
    [UdonSynced] private Quaternion netRotation;
    [UdonSynced] private Vector3 netVelocity;
    [UdonSynced] private int resetSequence;
    private bool localSeated;
    private bool pendingSeat;
    private float seatRequestTime;
    private float throttle;
    private float steering;
    private float nextSync;
    private float snapshotTime;
    private int observedReset;
    private bool hasSnapshot;
    private Vector3 surfaceUp = Vector3.up;
    private float airborneSince;
    private bool flightAllowed;
    private float wheelSpin;

    void Start()
    {
        SetAuthorityPhysics(Networking.IsOwner(gameObject));
        body.useGravity = false;
        body.centerOfMass = new Vector3(0f, 0.30f, 0f);
        if (Networking.IsOwner(gameObject)) { Recover(); }
    }

    public override void Interact()
    {
        VRCPlayerApi local = Networking.LocalPlayer;
        if (!Networking.IsNetworkSettled || (!Networking.IsOwner(gameObject) && !hasSnapshot)) return;
        if (!Utilities.IsValid(local) || localSeated || pendingSeat || driverId >= 0) return;
        pendingSeat = true;
        seatRequestTime = Time.time;
        Networking.SetOwner(local, gameObject);
        SendCustomEventDelayedSeconds(nameof(ConfirmSeat), 0.4f);
    }

    public override bool OnOwnershipRequest(VRCPlayerApi requester, VRCPlayerApi newOwner)
    {
        return driverId < 0 && Utilities.IsValid(requester) && Utilities.IsValid(newOwner)
            && requester.playerId == newOwner.playerId;
    }

    public void ConfirmSeat()
    {
        if (!pendingSeat) return;
        pendingSeat = false;
        if (!Networking.IsOwner(gameObject) || driverId >= 0) return;
        driverId = Networking.LocalPlayer.playerId;
        generation++;
        RequestSerialization();
        station.UseStation(Networking.LocalPlayer);
        SendCustomEventDelayedSeconds(nameof(CheckSeatEntered), 2f);
    }

    public void CheckSeatEntered()
    {
        if (Networking.IsOwner(gameObject) && !localSeated && driverId >= 0) ReleaseVehicle();
    }

    public override void OnStationEntered(VRCPlayerApi player)
    {
        if (!player.isLocal) return;
        if (!Networking.IsOwner(gameObject) || driverId != player.playerId)
        { station.ExitStation(player); return; }
        localSeated = true;
        throttle = 0f; steering = 0f;
        SetAuthorityPhysics(true);
    }

    public override void OnStationExited(VRCPlayerApi player)
    {
        if (!player.isLocal) return;
        localSeated = false;
        throttle = 0f; steering = 0f;
        if (Networking.IsOwner(gameObject)) ReleaseVehicle();
        if (Utilities.IsValid(safeExit)) player.TeleportTo(safeExit.position, safeExit.rotation);
    }

    public override void OnOwnershipTransferred(VRCPlayerApi player)
    {
        SetAuthorityPhysics(player.isLocal);
        if (!player.isLocal && localSeated)
        {
            localSeated = false;
            station.ExitStation(Networking.LocalPlayer);
        }
        if (player.isLocal && !pendingSeat && !localSeated)
        {
            // Automatic reassignment is not permission to continue the previous driver's run.
            ReleaseVehicle();
        }
    }

    public override void OnPlayerLeft(VRCPlayerApi player)
    {
        if (player.playerId == driverId && Networking.IsOwner(gameObject)) ReleaseVehicle();
    }

    public void ReleaseVehicle()
    {
        if (!Networking.IsOwner(gameObject)) return;
        driverId = -1; generation++; motionMode = 0;
        if (Utilities.IsValid(run)) run.CancelRun();
        Recover();
    }

    public void Recover()
    {
        if (!Networking.IsOwner(gameObject)) return;
        if (Utilities.IsValid(run)) run.CancelRun();
        body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero;
        body.position = spawn.position; body.rotation = spawn.rotation;
        surfaceUp = Vector3.up; flightAllowed = false; motionMode = driverId >= 0 ? 1 : 0;
        wasGrounded = true; contactTime = -100f; airborneSince = Time.time;
        groundClearance = -1f; closingSpeed = 0; previousClosingSpeed = 0; landingAcceleration = 0;
        attitudeCommand = Vector3.zero;
        ClearThrusters();
        resetSequence++;
        PublishSnapshot();
    }

    public void ExitVehicle()
    {
        if (localSeated) station.ExitStation(Networking.LocalPlayer);
    }

    public float GetDisplaySpeed()
    {
        return Networking.IsOwner(gameObject) ? body.velocity.magnitude : netVelocity.magnitude;
    }

    public bool IsLocalDriver()
    {
        return localSeated && Networking.IsOwner(gameObject) && Utilities.IsValid(Networking.LocalPlayer)
            && driverId == Networking.LocalPlayer.playerId;
    }

    public override void InputMoveVertical(float value, UdonInputEventArgs args) { throttle = value; }
    public override void InputMoveHorizontal(float value, UdonInputEventArgs args) { steering = value; }
    // Jump remains the standard Station escape. No forced camera manipulation.

    void Update()
    {
        if (pendingSeat && Time.time - seatRequestTime > 3f) pendingSeat = false;
        if (!Networking.IsOwner(gameObject) && hasSnapshot)
        {
            float age = Time.time - snapshotTime;
            Vector3 target = netPosition + netVelocity * Mathf.Min(age, 0.18f);
            transform.position = Vector3.Lerp(transform.position, target, 1f - Mathf.Exp(-12f * Time.deltaTime));
            transform.rotation = Quaternion.Slerp(transform.rotation, netRotation, 1f - Mathf.Exp(-12f * Time.deltaTime));
        }
        if (IsLocalDriver() && Input.GetKeyDown(KeyCode.R)) Recover();
        if (IsLocalDriver() && Input.GetKeyDown(KeyCode.T) && Utilities.IsValid(run)) run.BeginRun();
        if (Networking.IsOwner(gameObject) && Time.time >= nextSync)
        {
            nextSync = Time.time + (driverId >= 0 ? syncInterval : 1f);
            PublishSnapshot();
        }
        UpdateThrusterVisuals();
        float speed = Networking.IsOwner(gameObject) ? body.velocity.magnitude : netVelocity.magnitude;
        wheelSpin += speed * Time.deltaTime * 163.7f;
        for (int i = 0; i < wheels.Length; i++)
            if (Utilities.IsValid(wheels[i])) wheels[i].localRotation = Quaternion.Euler(wheelSpin, i % 4 < 2 ? steering * 22f : 0f, 0f);
    }

    void FixedUpdate()
    {
        if (!Networking.IsOwner(gameObject)) return;
        if (!IsLocalDriver())
        {
            body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero;
            for (int i = 0; i < 7; i++) thrusterDemand[i] = 0;
            return;
        }
        for (int i = 0; i < 7; i++) thrusterDemand[i] = 0;
        attitudeCommand = Vector3.zero; landingAcceleration = 0;
        ProbeLandingSurface();
        if (body.position.y < -20f || body.position.magnitude > 1800f) { Recover(); return; }
        if (flightAllowed)
        {
            Vector3 offset = body.position - flightBoundsCenter;
            if (Mathf.Abs(offset.x) > flightBoundsSize.x * .5f || Mathf.Abs(offset.y) > flightBoundsSize.y * .5f || Mathf.Abs(offset.z) > flightBoundsSize.z * .5f)
            { flightAllowed = false; if (Utilities.IsValid(run)) run.InvalidateFlightBounds(); }
        }
        int hits = 0;
        Vector3 normalSum = Vector3.zero;
        for (int i = 0; i < suspensionPoints.Length; i++)
        {
            RaycastHit hit;
            Transform point = suspensionPoints[i];
            if (Physics.Raycast(point.position, -transform.up, out hit, rideDistance, roadMask, QueryTriggerInteraction.Ignore))
            {
                hits++; normalSum += hit.normal;
                float compression = 1f - hit.distance / rideDistance;
                float verticalSpeed = Vector3.Dot(body.GetPointVelocity(point.position), hit.normal);
                body.AddForceAtPosition(hit.normal * (compression * springStrength - verticalSpeed * springDamping), point.position, ForceMode.Acceleration);
            }
        }
        if (hits >= 2)
        {
            if (!wasGrounded && Time.time - airborneSince > .15f)
            {
                lastTouchdownSpeed = previousClosingSpeed; touchdownCount++; contactTime = Time.time;
            }
            if (landingAssistEnabled && Time.time - contactTime < .35f)
            {
                Vector3 n = normalSum.normalized;
                float rebound = Vector3.Dot(body.velocity, n);
                if (rebound > maxReboundSpeed) body.velocity -= n * (rebound - maxReboundSpeed);
            }
            airborneSince = Time.time;
            surfaceUp = Vector3.Slerp(surfaceUp, normalSum.normalized, 8f * Time.fixedDeltaTime);
            motionMode = Vector3.Dot(surfaceUp, Vector3.up) < 0.65f ? 2 : 1;
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, surfaceUp).normalized;
            float forwardSpeed = Vector3.Dot(body.velocity, forward);
            float force = throttle * acceleration;
            if (throttle < 0f && forwardSpeed > 1f) force = throttle * brakeAcceleration;
            body.AddForce(forward * force, ForceMode.Acceleration);
            body.AddForce(-surfaceUp * (motionMode == 2 ? 23f : 9.81f), ForceMode.Acceleration);
            Vector3 lateral = Vector3.Project(body.velocity, transform.right);
            body.AddForce(-lateral * 6f, ForceMode.Acceleration);
            float yaw = steering * Mathf.Clamp(forwardSpeed / 12f, -1f, 1f) * 1.2f;
            body.AddTorque(Vector3.Cross(transform.up, surfaceUp) * 18f - body.angularVelocity * 4f + surfaceUp * yaw * 4f, ForceMode.Acceleration);
        }
        else if (flightAllowed && Time.time - airborneSince > 0.12f)
        {
            motionMode = 3;
            Vector3 forward = transform.forward;
            body.AddForce(forward * (8f + Mathf.Max(0f, throttle) * 10f), ForceMode.Acceleration);
            // Assisted glide: gravity retained, lift follows speed and altitude cap.
            float lift = Mathf.Clamp(body.velocity.magnitude / 28f, 0f, 1f) * 9.81f;
            if (body.position.y > flightCeiling) lift = 0f;
            // Hand descent to the landing controller, which already compensates gravity.
            // Keeping glide lift here can arrest a shallow descent before the wheels reach the road.
            if (landingAssistEnabled && groundClearance >= 0f && closingSpeed > .1f) lift = 0f;
            body.AddForce(Vector3.up * (lift - 9.81f - Mathf.Max(0f, -throttle) * 6f), ForceMode.Acceleration);
            // Directional jets expose the same PD request that drives attitude assistance.
            thrusterDemand[6] = Mathf.Clamp01((8f + Mathf.Max(0f, throttle) * 10f) / 18f);
            body.AddForce(-body.velocity * 0.12f, ForceMode.Acceleration);
        }
        else { motionMode = 4; body.AddForce(Vector3.down * 9.81f, ForceMode.Acceleration); }
        if (hits < 2)
        {
            ApplyAirAttitude();
            ApplyLandingBrake();
            MixThrusters();
        }
        wasGrounded = hits >= 2;
        previousClosingSpeed = closingSpeed;
        body.velocity = Vector3.ClampMagnitude(body.velocity, maxSpeed);
        if (Utilities.IsValid(run)) run.SamplePosition();
    }

    private void ProbeLandingSurface()
    {
        groundClearance = -1f; closingSpeed = 0;
        RaycastHit hit;
        // Gravity-relative look-ahead, independent of upside-down vehicle orientation.
        if (Physics.Raycast(body.position + Vector3.up * .5f, Vector3.down, out hit,
            landingProbeDistance + .5f, roadMask, QueryTriggerInteraction.Ignore) && hit.normal.y > .5f)
        {
            groundClearance = Mathf.Max(0f, Vector3.Dot(body.position - hit.point, hit.normal));
            landingNormal = hit.normal;
            closingSpeed = Mathf.Max(0f, -Vector3.Dot(body.velocity, hit.normal));
        }
    }

    private void ApplyAirAttitude()
    {
        Quaternion error = Quaternion.FromToRotation(transform.up, Vector3.up);
        float angle; Vector3 axis;
        error.ToAngleAxis(out angle, out axis);
        if (angle > 180f) angle -= 360f;
        Vector3 correction = angle == 0f ? Vector3.zero : axis * (angle * Mathf.Deg2Rad * attitudeStrength);
        Vector3 requested = correction - body.angularVelocity * attitudeDamping + Vector3.up * steering * 2.5f;
        requested = Vector3.ClampMagnitude(requested, maxAttitudeAcceleration);
        attitudeCommand = transform.InverseTransformDirection(requested);
        body.AddTorque(requested, ForceMode.Acceleration);
    }

    private void ApplyLandingBrake()
    {
        if (!landingAssistEnabled || groundClearance < .08f || groundClearance > landingProbeDistance
            || closingSpeed <= landingTargetSpeed) return;
        float alignment = Vector3.Dot(transform.up, landingNormal);
        if (alignment < .35f) return; // Correct attitude first; exhaust always follows the body.
        float distance = Mathf.Max(.15f, groundClearance - .08f);
        float required = (closingSpeed * closingSpeed - landingTargetSpeed * landingTargetSpeed) / (2f * distance);
        landingAcceleration = Mathf.Clamp((required + 9.81f) / alignment, 0f, maxLandingAcceleration);
        body.AddForce(transform.up * landingAcceleration, ForceMode.Acceleration);
    }

    private void MixThrusters()
    {
        // Four upward-force nozzles: r x up = (-r.z, 0, r.x).
        // The torque is game assistance; this mixer visualizes its direction, not real gas dynamics.
        float pitch = attitudeCommand.x / maxAttitudeAcceleration;
        float roll = attitudeCommand.z / maxAttitudeAcceleration;
        float collective = landingAcceleration / maxLandingAcceleration;
        float baseDemand = Mathf.Max(collective, (Mathf.Abs(pitch) + Mathf.Abs(roll)) * .28f);
        thrusterDemand[0] = Mathf.Clamp01(baseDemand - pitch * .7f - roll * .7f);
        thrusterDemand[1] = Mathf.Clamp01(baseDemand - pitch * .7f + roll * .7f);
        thrusterDemand[2] = Mathf.Clamp01(baseDemand + pitch * .7f - roll * .7f);
        thrusterDemand[3] = Mathf.Clamp01(baseDemand + pitch * .7f + roll * .7f);
        float yaw = attitudeCommand.y / maxAttitudeAcceleration;
        thrusterDemand[4] = Mathf.Clamp01(yaw);  // left exhaust -> positive yaw force moment
        thrusterDemand[5] = Mathf.Clamp01(-yaw);
    }

    private void UpdateThrusterVisuals()
    {
        if (thrusterJets == null || thrusterJets.Length != 7) return;
        bool owner = Networking.IsOwner(gameObject);
        bool fresh = owner || (hasSnapshot && Time.time - snapshotTime < .6f);
        for (int i = 0; i < 7; i++)
        {
            float demand = fresh && driverId >= 0 ? thrusterDemand[i] : 0f;
            // Fast valve response; smoke already emitted persists in world space until lifetime ends.
            visibleThrust[i] = Mathf.MoveTowards(visibleThrust[i], demand, Time.deltaTime * (demand > visibleThrust[i] ? 14f : 9f));
            EmitBudgeted(thrusterJets[i], i, Mathf.Sqrt(visibleThrust[i]) * 320f);
            if (thrusterSmoke != null && thrusterSmoke.Length == 7)
                EmitBudgeted(thrusterSmoke[i], 7 + i, visibleThrust[i] * 120f);
            if (i < 4 && groundMist != null && groundMist.Length == 4)
            {
                RaycastHit hit;
                Transform jet = thrusterJets[i].transform;
                if (visibleThrust[i] > .02f && Physics.Raycast(jet.position, jet.forward, out hit, 3f, roadMask, QueryTriggerInteraction.Ignore))
                {
                    groundMist[i].transform.SetPositionAndRotation(hit.point + hit.normal * .06f, Quaternion.LookRotation(hit.normal));
                    EmitBudgeted(groundMist[i], 14 + i, visibleThrust[i] * 55f * (1f - hit.distance / 3f));
                }
            }
        }
    }

    private void EmitBudgeted(ParticleSystem particles, int index, float rate)
    {
        if (!Utilities.IsValid(particles)) return;
        emissionCarry[index] = Mathf.Min(8f, emissionCarry[index] + rate * Mathf.Min(Time.deltaTime, .05f));
        int count = Mathf.Min(8, Mathf.FloorToInt(emissionCarry[index]));
        emissionCarry[index] -= count;
        if (count > 0) particles.Emit(count);
    }

    private void ClearThrusters()
    {
        for (int i = 0; i < 7; i++) { thrusterDemand[i] = 0; visibleThrust[i] = 0; }
        for (int i = 0; i < 18; i++) emissionCarry[i] = 0;
        if (thrusterJets != null) for (int i = 0; i < thrusterJets.Length; i++) if (Utilities.IsValid(thrusterJets[i])) thrusterJets[i].Clear();
        if (groundMist != null) for (int i = 0; i < groundMist.Length; i++) if (Utilities.IsValid(groundMist[i])) groundMist[i].Clear();
        if (thrusterSmoke != null) for (int i = 0; i < thrusterSmoke.Length; i++) if (Utilities.IsValid(thrusterSmoke[i])) thrusterSmoke[i].Clear();
        netThrusterA = 0; netThrusterB = 0;
    }

    private void PackThrust()
    {
        netThrusterA = 0; netThrusterB = 0;
        for (int i = 0; i < 7; i++)
        {
            int value = Mathf.RoundToInt(Mathf.Clamp01(thrusterDemand[i]) * 255f);
            if (i < 4) netThrusterA |= value << (8 * i);
            else netThrusterB |= value << (8 * (i - 4));
        }
    }

    public void EnableFlight() { if (IsLocalDriver()) flightAllowed = true; }
    public void DisableFlight() { if (IsLocalDriver()) flightAllowed = false; }

    private void SetAuthorityPhysics(bool owner)
    {
        body.isKinematic = !owner;
        body.collisionDetectionMode = owner ? CollisionDetectionMode.ContinuousDynamic : CollisionDetectionMode.ContinuousSpeculative;
    }

    private void PublishSnapshot()
    {
        netPosition = body.position; netRotation = body.rotation; netVelocity = body.velocity;
        PackThrust();
        RequestSerialization();
    }

    public override void OnPreSerialization()
    {
        if (!Networking.IsOwner(gameObject)) return;
        netPosition = body.position; netRotation = body.rotation; netVelocity = body.velocity;
        PackThrust();
    }

    public override void OnPostSerialization(SerializationResult result)
    {
        lastSerializedBytes = result.byteCount; lastSerializationSucceeded = result.success;
        if (!result.success) failedSerializations++;
    }

    public override void OnDeserialization()
    {
        if (Networking.IsOwner(gameObject)) return;
        SetAuthorityPhysics(false);
        int receivedA = netThrusterA, receivedB = netThrusterB;
        if (!hasSnapshot || observedReset != resetSequence)
        {
            transform.SetPositionAndRotation(netPosition, netRotation);
            observedReset = resetSequence;
            ClearThrusters();
        }
        for (int i = 0; i < 7; i++)
            thrusterDemand[i] = ((i < 4 ? receivedA >> (8 * i) : receivedB >> (8 * (i - 4))) & 255) / 255f;
        hasSnapshot = true; snapshotTime = Time.time;
    }
}
