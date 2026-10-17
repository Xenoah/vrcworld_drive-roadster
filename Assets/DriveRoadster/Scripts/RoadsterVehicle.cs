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
        float speed = Networking.IsOwner(gameObject) ? body.velocity.magnitude : netVelocity.magnitude;
        wheelSpin += speed * Time.deltaTime * 163.7f;
        for (int i = 0; i < wheels.Length; i++)
            if (Utilities.IsValid(wheels[i])) wheels[i].localRotation = Quaternion.Euler(wheelSpin, i % 4 < 2 ? steering * 22f : 0f, 0f);
    }

    void FixedUpdate()
    {
        if (!Networking.IsOwner(gameObject)) return;
        if (!IsLocalDriver()) { body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero; return; }
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
            body.AddForce(Vector3.up * (lift - 9.81f - Mathf.Max(0f, -throttle) * 6f), ForceMode.Acceleration);
            Vector3 desiredUp = Vector3.up;
            body.AddTorque(Vector3.Cross(transform.up, desiredUp) * 5f - body.angularVelocity * 3f + Vector3.up * steering * 2.5f, ForceMode.Acceleration);
            body.AddForce(-body.velocity * 0.12f, ForceMode.Acceleration);
        }
        else { motionMode = 4; body.AddForce(Vector3.down * 9.81f, ForceMode.Acceleration); }
        body.velocity = Vector3.ClampMagnitude(body.velocity, maxSpeed);
        if (Utilities.IsValid(run)) run.SamplePosition();
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
        RequestSerialization();
    }

    public override void OnPreSerialization()
    {
        if (!Networking.IsOwner(gameObject)) return;
        netPosition = body.position; netRotation = body.rotation; netVelocity = body.velocity;
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
        if (!hasSnapshot || observedReset != resetSequence)
        {
            transform.SetPositionAndRotation(netPosition, netRotation);
            observedReset = resetSequence;
        }
        hasSnapshot = true; snapshotTime = Time.time;
    }
}
