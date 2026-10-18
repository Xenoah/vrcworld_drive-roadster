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
    public Transform[] yokes;
    public Transform[] throttlePedals;
    public Transform[] brakePedals;
    public Transform cockpitEye;
    public float seatBaseHeight = .30f;
    [UdonSynced] private float seatHeightOffset;
    public int fleetNumber = 1;
    public GameObject[] cockpitPanels;
    private float nextParkedVisual;
    private bool idlePhysicsCleared;
    public UnityEngine.UI.Text boardingDisplay;
    public float inputDeadzone = .12f;
    public int sensitivityProfile = 1; // 0 comfort, 1 normal, 2 direct (local preference)
    public bool showControlHelp = true;
    [HideInInspector] public string controlFeedback = "READY";
    private float feedbackUntil;
    private float recoverArmedUntil;
    private float rawThrottle, rawSteering;
    [UdonSynced] public bool manualFlight;
    public Transform yokeAnchor;
    public float yokeGrabRadius = .32f;
    public float yokeReleaseRadius = .55f;
    public float yokeFullAngle = 35f;
    public float manualRate = 1.5f; // radians/second at full input
    public float jumpAcceleration = 24f;
    private float moveVertical, moveHorizontal, desktopRoll;
    private bool jumpHeld, rightUseHeld, leftUseHeld, rightGrabHeld, desktopBrake;
    private bool inputArmed, vrInput, yokeHeld;
    private float lastControlFrame;
    private float neutralSince, pitchInput, rollInput, rawPitch, rawRoll, liftInput;
    private float appliedLift;
    private Quaternion yokeReference;
    private string inputMethodName = "Keyboard";
    [UdonSynced] private Vector3 netPilotAxes;
    [UdonSynced] private int netInputState;
    [UdonSynced] private int netControl;
    [UdonSynced] private Vector3 netLandingTelemetry;
    [HideInInspector] public int lastSerializedBytes;
    [HideInInspector] public bool lastSerializationSucceeded;
    [HideInInspector] public int failedSerializations;
    // Game assistance, not a reconstruction of any real vehicle's internal mechanism.
    public ParticleSystem[] thrusterJets;
    public ParticleSystem[] groundMist;
    public ParticleSystem[] thrusterSmoke;
    [UdonSynced] public bool landingAssistEnabled = true;
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
        if (!Networking.IsNetworkSettled || (!Networking.IsOwner(gameObject) && !hasSnapshot))
        { SetFeedback("SYNCING / WAIT FOR VEHICLE"); return; }
        if (!Utilities.IsValid(local)) return;
        if (localSeated) { SetFeedback("YOU ARE DRIVING"); return; }
        if (pendingSeat) { SetFeedback("REQUESTING CONTROL / PLEASE WAIT"); return; }
        if (driverId >= 0) { SetFeedback("OCCUPIED / DRIVER " + driverId); return; }
        SetFeedback("REQUESTING CONTROL / PLEASE WAIT");
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
        if (!Networking.IsOwner(gameObject) || driverId >= 0) { SetFeedback("SEAT TAKEN / TRY AGAIN"); return; }
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
        idlePhysicsCleared=false;
        vrInput = player.IsUserInVR();
        inputMethodName = VRC.SDKBase.InputManager.GetLastUsedInputMethod().ToString();
        manualFlight = false; landingAssistEnabled = true;
        SetFeedback("RELEASE INPUTS / X OR EXIT TO PADDOCK");
        ClearInput();
        SetAuthorityPhysics(true);
        SendCustomEventDelayedSeconds(nameof(CalibrateSeatHeight), .5f);
    }

    public void CalibrateSeatHeight()
    {
        if(!IsLocalDriver() || !Utilities.IsValid(cockpitEye) || body.velocity.magnitude>1f)return;
        // One adjustment at boarding, never follow subsequent HMD head movement.
        Vector3 head=transform.InverseTransformPoint(Networking.LocalPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position);
        seatHeightOffset=Mathf.Clamp(seatHeightOffset+cockpitEye.localPosition.y-head.y,-.85f,.35f);
        ApplySeatHeight();RequestSerialization();
    }
    private void ApplySeatHeight()
    {
        if(!Utilities.IsValid(station) || !Utilities.IsValid(station.stationEnterPlayerLocation))return;
        Vector3 p=station.stationEnterPlayerLocation.localPosition;p.y=seatBaseHeight+seatHeightOffset;
        station.stationEnterPlayerLocation.localPosition=p;
    }

    public override void OnStationExited(VRCPlayerApi player)
    {
        if (!player.isLocal) return;
        localSeated = false;
        SetFeedback("SAFE EXIT / PADDOCK");
        ClearInput();
        if (Networking.IsOwner(gameObject)) ReleaseVehicle();
        if (Utilities.IsValid(safeExit)) player.TeleportTo(safeExit.position, safeExit.rotation);
    }

    public override void OnOwnershipTransferred(VRCPlayerApi player)
    {
        ClearInput();
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
        seatHeightOffset=0;ApplySeatHeight();
        if (Utilities.IsValid(run)) run.CancelRun();
        Recover();
    }

    public void Recover()
    {
        if (!Networking.IsOwner(gameObject)) return;
        if (Utilities.IsValid(run)) run.CancelRun();
        ClearInput(); recoverArmedUntil = 0;
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

    // Observe release events even outside the seat so a held boarding input cannot arm propulsion.
    public override void InputMoveVertical(float value, UdonInputEventArgs args) { moveVertical = Mathf.Clamp(value,-1f,1f); }
    public override void InputMoveHorizontal(float value, UdonInputEventArgs args) { moveHorizontal = Mathf.Clamp(value,-1f,1f); }
    public override void InputJump(bool value, UdonInputEventArgs args) { jumpHeld = value; }
    public override void InputUse(bool value, UdonInputEventArgs args)
    {
        if (args.handType == HandType.RIGHT) rightUseHeld = value;
        else leftUseHeld = value;
    }
    public override void InputGrab(bool value, UdonInputEventArgs args)
    {
        if (args.handType != HandType.RIGHT) return;
        rightGrabHeld = value;
        if (!value) { ReleaseYoke(); return; }
        if (!IsLocalDriver() || !vrInput || !inputArmed || Networking.LocalPlayer.isSuspended || rightUseHeld || leftUseHeld) return;
        var hand = Networking.LocalPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand);
        if (Utilities.IsValid(yokeAnchor) && Vector3.Distance(hand.position,yokeAnchor.position) <= yokeGrabRadius)
        {
            yokeReference = Quaternion.Inverse(transform.rotation) * hand.rotation;
            yokeHeld = true;
        }
    }
    public override void InputDrop(bool value, UdonInputEventArgs args)
    {
        if (value && args.handType == HandType.RIGHT) { rightGrabHeld = false; ReleaseYoke(); }
    }
    public override void OnInputMethodChanged(VRCInputMethod inputMethod)
    {
        string nextMethod = inputMethod.ToString();
        bool wasVR = vrInput;
        if (Utilities.IsValid(Networking.LocalPlayer)) vrInput = Networking.LocalPlayer.IsUserInVR();
        if (wasVR != vrInput || (vrInput && inputMethodName != nextMethod)) ClearInput();
        inputMethodName = nextMethod;
    }
    public override void OnPlayerSuspendChanged(VRCPlayerApi player)
    {
        if (player.isLocal) ClearInput();
    }
    private void ClearInput()
    {
        rawThrottle=0; rawSteering=0; throttle=0; steering=0;
        rawPitch=0; rawRoll=0; pitchInput=0; rollInput=0; liftInput=0; appliedLift=0;
        inputArmed=false; yokeHeld=false; neutralSince=Time.time;
        // Preserve observed held inputs: neutral must be observed before rearming.
    }
    private void ReleaseYoke()
    {
        if (!vrInput) return;
        yokeHeld=false; rawPitch=0; rawRoll=0; pitchInput=0; rollInput=0;
        rawThrottle=0; throttle=0; liftInput=0;
    }
    public void SuspendForPanel()
    {
        if (IsLocalDriver()) ClearInput();
    }
    public void ToggleFlightMode()
    {
        if (!IsLocalDriver()) { DriverOnlyFeedback(); return; }
        manualFlight=!manualFlight; ClearInput();
        SetFeedback((manualFlight ? "MANUAL / RATE CONTROL" : "AUTO / LEVEL ASSIST")+" / RELEASE INPUTS");
        PublishSnapshot();
    }
    public void ToggleLandingAssist()
    {
        if (!IsLocalDriver()) { DriverOnlyFeedback(); return; }
        landingAssistEnabled=!landingAssistEnabled; ClearInput();
        SetFeedback("LANDING ASSIST / "+(landingAssistEnabled ? "ON" : "OFF"));
        PublishSnapshot();
    }
    private float ShapeInput(float value)
    {
        float magnitude=Mathf.Abs(value);
        if (magnitude<=inputDeadzone) return 0f;
        return Mathf.Sign(value)*Mathf.Clamp01((magnitude-inputDeadzone)/Mathf.Max(.01f,1f-inputDeadzone));
    }
    public void ProcessPilotInput()
    {
        if (!IsLocalDriver()) return;
        if (Networking.LocalPlayer.isSuspended) { ClearInput(); return; }
        bool neutral = Mathf.Abs(moveHorizontal)<=inputDeadzone && (vrInput || Mathf.Abs(moveVertical)<=inputDeadzone)
            && !jumpHeld && !rightUseHeld && !leftUseHeld && !rightGrabHeld && !desktopBrake && Mathf.Abs(desktopRoll)<.01f;
        if (!inputArmed)
        {
            if (!neutral) neutralSince=Time.time;
            else if (Time.time-neutralSince>.15f)
            {
                inputArmed=true;
                if(controlFeedback.Contains("RELEASE INPUTS")) feedbackUntil=0f;
            }
            return;
        }
        bool manualAir = manualFlight && !wasGrounded;
        rawSteering=moveHorizontal; rawPitch=0; rawRoll=0; rawThrottle=0;
        bool brakeRequested=false;
        if (vrInput)
        {
            // Left stick Y is deliberately unused. Trigger events are buttons, not analog pressure.
            if (yokeHeld)
            {
                var hand = Networking.LocalPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand);
                if (!rightGrabHeld || !Utilities.IsValid(yokeAnchor) || Vector3.Distance(hand.position,yokeAnchor.position)>yokeReleaseRadius)
                    ReleaseYoke();
                else
                {
                    Quaternion relative=Quaternion.Inverse(yokeReference)*(Quaternion.Inverse(transform.rotation)*hand.rotation);
                    Vector3 angles=relative.eulerAngles;
                    rawPitch=Mathf.Clamp(Mathf.DeltaAngle(0,angles.x)/yokeFullAngle,-1,1);
                    rawRoll=Mathf.Clamp(Mathf.DeltaAngle(0,angles.z)/yokeFullAngle,-1,1);
                }
            }
            brakeRequested=leftUseHeld;
            rawThrottle=brakeRequested ? -1f : yokeHeld && rightUseHeld ? 1f : 0f;
            if (!yokeHeld) rawSteering=0;
        }
        else if (manualAir)
        {
            rawPitch=moveVertical; rawRoll=-desktopRoll;
            brakeRequested=desktopBrake;
            rawThrottle=brakeRequested ? -1f : jumpHeld ? 1f : 0f;
        }
        else
        {
            rawThrottle=moveVertical; brakeRequested=rawThrottle<0;
        }
        float axis=ShapeInput(rawSteering);
        float gain=sensitivityProfile==0 ? .65f : sensitivityProfile==1 ? .85f : 1f;
        float targetSteer=Mathf.Sign(axis)*Mathf.Pow(Mathf.Abs(axis),1.4f)*gain;
        steering=Mathf.MoveTowards(steering,targetSteer,6f*Time.fixedDeltaTime);
        float targetThrottle=ShapeInput(rawThrottle);
        // Do not retain positive propulsion for a smoothing frame when braking begins.
        if (brakeRequested && throttle>0f) throttle=0f;
        throttle=Mathf.MoveTowards(throttle,targetThrottle,(targetThrottle<0 ? 10f : 6f)*Time.fixedDeltaTime);
        pitchInput=Mathf.MoveTowards(pitchInput,manualAir ? ShapeInput(rawPitch)*gain : 0f,6f*Time.fixedDeltaTime);
        rollInput=Mathf.MoveTowards(rollInput,manualAir ? ShapeInput(rawRoll)*gain : 0f,6f*Time.fixedDeltaTime);
        float liftTarget=jumpHeld && !manualFlight && !brakeRequested && (!vrInput || yokeHeld) ? 1f : 0f;
        if (brakeRequested) liftInput=0f;
        liftInput=Mathf.MoveTowards(liftInput,liftTarget,6f*Time.fixedDeltaTime);
    }
    private void SetFeedback(string message) { controlFeedback=message; feedbackUntil=Time.time+5f; }
    public void DriverOnlyFeedback() { SetFeedback("DRIVER ONLY / REQUEST THE SEAT FIRST"); }
    public string GetControlMessage()
    {
        if (Time.time<feedbackUntil) return controlFeedback;
        if (pendingSeat) return "REQUESTING CONTROL";
        if (!IsLocalDriver()) return driverId>=0 ? "OCCUPIED / DRIVER " + driverId : "AVAILABLE / INTERACT AT DRIVER DOOR";
        if (!inputArmed) return "RELEASE INPUTS TO ARM / X OR EXIT";
        if (vrInput && !yokeHeld) return "GRIP RIGHT YOKE TO ACCELERATE / X OR EXIT";
        return "CONTROL READY / X OR EXIT TO PADDOCK";
    }
    public void RequestRecover()
    {
        if (!IsLocalDriver()) { DriverOnlyFeedback(); return; }
        if (body.velocity.magnitude>2f && Time.time>recoverArmedUntil)
        { recoverArmedUntil=Time.time+2f; SetFeedback("RESET? PRESS AGAIN WITHIN 2s"); return; }
        Recover(); SetFeedback("RESET COMPLETE / START BOX");
    }
    public void CycleSensitivity()
    {
        if (!IsLocalDriver()) { DriverOnlyFeedback(); return; }
        sensitivityProfile=(sensitivityProfile+1)%3; SetFeedback("STEERING / " + GetProfileName());
    }
    public string GetProfileName() { return sensitivityProfile==0 ? "COMFORT" : sensitivityProfile==1 ? "NORMAL" : "DIRECT"; }
    public void ToggleControlHelp() { showControlHelp=!showControlHelp; }
    public float GetDisplaySteering() { return Networking.IsOwner(gameObject) ? steering : ((netControl & 255)-127)/127f; }
    public float GetDisplayThrottle() { return Networking.IsOwner(gameObject) ? throttle : (((netControl>>8) & 255)-127)/127f; }
    public Vector3 GetLandingTelemetry() { return Networking.IsOwner(gameObject) ? new Vector3(groundClearance,closingSpeed,landingAcceleration) : netLandingTelemetry; }
    public bool HasFlightPermission() { return flightAllowed; }
    public string GetInputName()
    {
        bool vr=Networking.IsOwner(gameObject) ? vrInput : (netInputState&1)!=0;
        return vr ? "VR / TRIGGER BUTTONS + YOKE" : "DESKTOP / KEYS";
    }
    public string GetInputMethodName() { return IsLocalDriver() ? inputMethodName=="Count" ? "DEVICE NOT REPORTED" : inputMethodName : "REMOTE"; }
    public Vector3 GetPilotAxes() { return Networking.IsOwner(gameObject) ? new Vector3(pitchInput,steering,rollInput) : netPilotAxes; }
    private void AnimateCockpit()
    {
        float steer=GetDisplaySteering(), pedal=GetDisplayThrottle();
        Vector3 axes=GetPilotAxes();
        if (yokes!=null) for(int i=0;i<yokes.Length;i++) if(Utilities.IsValid(yokes[i])) yokes[i].localRotation=manualFlight && motionMode>=3 ? Quaternion.Euler(axes.x*25f,0,axes.z*35f) : Quaternion.Euler(0,0,-steer*70f);
        if (throttlePedals!=null) for(int i=0;i<throttlePedals.Length;i++) if(Utilities.IsValid(throttlePedals[i])) throttlePedals[i].localRotation=Quaternion.Euler(Mathf.Max(0,pedal)*18f,0,0);
        if (brakePedals!=null) for(int i=0;i<brakePedals.Length;i++) if(Utilities.IsValid(brakePedals[i])) brakePedals[i].localRotation=Quaternion.Euler(Mathf.Max(0,-pedal)*18f,0,0);
        if (Utilities.IsValid(boardingDisplay)) boardingDisplay.text="CAR "+fleetNumber.ToString("D2")+" / "+(driverId>=0 ? "OCCUPIED" : "AVAILABLE")+"\n"+GetControlMessage()+"\nINTERACT / REQUEST SEAT";
    }

    void Update()
    {
        if(cockpitPanels!=null)for(int p=0;p<cockpitPanels.Length;p++)
            if(cockpitPanels[p]!=null && cockpitPanels[p].activeSelf!=localSeated)cockpitPanels[p].SetActive(localSeated);
        // Parked fleet stays quiet; snapshots still refresh for late joiners.
        if(driverId<0 && !pendingSeat && Time.time<nextParkedVisual)return;
        nextParkedVisual=Time.time+.25f;
        // Udon does not expose Application.isFocused. Supported menu release events,
        // suspension and long frame gaps disarm controls; OS focus delivery needs client testing.
        if (lastControlFrame>0f && Time.realtimeSinceStartup-lastControlFrame>.5f) ClearInput();
        lastControlFrame=Time.realtimeSinceStartup;
        if (IsLocalDriver())
        {
            desktopRoll=(Input.GetKey(KeyCode.E) ? 1f : 0f)-(Input.GetKey(KeyCode.Q) ? 1f : 0f);
            desktopBrake=Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        }
        if (pendingSeat && Time.time - seatRequestTime > 3f) { pendingSeat = false; SetFeedback("REQUEST TIMED OUT / TRY AGAIN"); }
        if (!Networking.IsOwner(gameObject) && hasSnapshot)
        {
            float age = Time.time - snapshotTime;
            Vector3 target = netPosition + netVelocity * Mathf.Min(age, 0.18f);
            transform.position = Vector3.Lerp(transform.position, target, 1f - Mathf.Exp(-12f * Time.deltaTime));
            transform.rotation = Quaternion.Slerp(transform.rotation, netRotation, 1f - Mathf.Exp(-12f * Time.deltaTime));
        }
        if (IsLocalDriver() && Input.GetKeyDown(KeyCode.Backspace)) RequestRecover();
        if (IsLocalDriver() && Input.GetKeyDown(KeyCode.T) && Utilities.IsValid(run)) run.BeginRun();
        if (Networking.IsOwner(gameObject) && Time.time >= nextSync)
        {
            nextSync = Time.time + (driverId >= 0 ? syncInterval : 5f + fleetNumber*.031f);
            PublishSnapshot();
        }
        if (localSeated && Input.GetKeyDown(KeyCode.X)) ExitVehicle();
        if (IsLocalDriver() && Input.GetKeyDown(KeyCode.C)) CycleSensitivity();
        if (IsLocalDriver() && Input.GetKeyDown(KeyCode.H)) ToggleControlHelp();
        if (IsLocalDriver() && Input.GetKeyDown(KeyCode.F)) ToggleFlightMode();
        if (IsLocalDriver() && Input.GetKeyDown(KeyCode.L)) ToggleLandingAssist();
        AnimateCockpit();
        UpdateThrusterVisuals();
        float speed = Networking.IsOwner(gameObject) ? body.velocity.magnitude : netVelocity.magnitude;
        wheelSpin += speed * Time.deltaTime * 163.7f;
        for (int i = 0; i < wheels.Length; i++)
            if (Utilities.IsValid(wheels[i])) wheels[i].localRotation = Quaternion.Euler(wheelSpin, i % 4 < 2 ? GetDisplaySteering() * 22f : 0f, 0f);
    }

    void FixedUpdate()
    {
        if (!Networking.IsOwner(gameObject)) return;
        if (!IsLocalDriver())
        {
            if(idlePhysicsCleared)return;
            ClearInput();
            body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero;
            for (int i = 0; i < 7; i++) thrusterDemand[i] = 0;
            body.Sleep();idlePhysicsCleared=true;
            return;
        }
        ProcessPilotInput();
        for (int i = 0; i < 7; i++) thrusterDemand[i] = 0;
        attitudeCommand = Vector3.zero; landingAcceleration = 0;
        appliedLift=0;
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
        if (manualFlight && wasGrounded != (hits>=2))
        {
            ClearInput();
            SetFeedback("MANUAL GROUND/AIR CHANGE / RELEASE INPUTS");
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
            if (vrInput && throttle<0f) force=-Mathf.Sign(forwardSpeed)*Mathf.Min(brakeAcceleration*-throttle,Mathf.Abs(forwardSpeed)/Time.fixedDeltaTime);
            body.AddForce(forward * force, ForceMode.Acceleration);
            body.AddForce(-surfaceUp * (motionMode == 2 ? 23f : 9.81f), ForceMode.Acceleration);
            Vector3 lateral = Vector3.Project(body.velocity, transform.right);
            body.AddForce(-lateral * 6f, ForceMode.Acceleration);
            float yaw = steering * Mathf.Clamp(forwardSpeed / 12f, -1f, 1f) * 1.2f;
            body.AddTorque(Vector3.Cross(transform.up, surfaceUp) * 18f - body.angularVelocity * 4f + surfaceUp * yaw * 4f, ForceMode.Acceleration);
            if (flightAllowed && !manualFlight && liftInput>.01f && body.position.y<flightCeiling)
            {
                appliedLift=liftInput;
                body.AddForce(Vector3.up*jumpAcceleration*liftInput,ForceMode.Acceleration);
            }
        }
        else if (flightAllowed && Time.time - airborneSince > 0.12f)
        {
            motionMode = 3;
            Vector3 forward = transform.forward;
            body.AddForce(forward * (Mathf.Max(0f, throttle) * 18f), ForceMode.Acceleration);
            if (throttle<0f && body.velocity.sqrMagnitude>.01f)
                body.AddForce(-body.velocity.normalized*Mathf.Min(-throttle*brakeAcceleration,body.velocity.magnitude/Time.fixedDeltaTime),ForceMode.Acceleration);
            // Assisted glide: gravity retained, lift follows speed and altitude cap.
            float lift = manualFlight ? 0f : Mathf.Clamp(body.velocity.magnitude / 28f, 0f, 1f) * 9.81f;
            if (body.position.y > flightCeiling) lift = 0f;
            // Hand descent to the landing controller, which already compensates gravity.
            // Keeping glide lift here can arrest a shallow descent before the wheels reach the road.
            if (landingAssistEnabled && groundClearance >= 0f && closingSpeed > .1f) lift = 0f;
            appliedLift=body.position.y<flightCeiling ? liftInput : 0f;
            body.AddForce(Vector3.up * (lift - 9.81f + appliedLift*jumpAcceleration), ForceMode.Acceleration);
            // Directional jets expose the same PD request that drives attitude assistance.
            thrusterDemand[6] = Mathf.Max(0f, throttle);
            body.AddForce(-body.velocity * 0.12f, ForceMode.Acceleration);
        }
        else { motionMode = 4; body.AddForce(Vector3.down * 9.81f, ForceMode.Acceleration); }
        if (hits < 2)
        {
            ApplyAirAttitude();
            ApplyLandingBrake();
        }
        if (hits<2 || appliedLift>0) MixThrusters();
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
        if (manualFlight)
        {
            Vector3 targetRate=new Vector3(pitchInput,steering,rollInput)*manualRate;
            Vector3 requestedLocal=(targetRate-transform.InverseTransformDirection(body.angularVelocity))*attitudeDamping;
            attitudeCommand=Vector3.ClampMagnitude(requestedLocal,maxAttitudeAcceleration);
            body.AddTorque(transform.TransformDirection(attitudeCommand),ForceMode.Acceleration);
            return; // Neutral input damps rotation; it never restores a level attitude.
        }
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
        if (alignment < .35f) return; // No hidden self-leveling in Manual; display poor alignment instead.
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
        float collective = Mathf.Max(landingAcceleration / maxLandingAcceleration,appliedLift);
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
        if(Utilities.IsValid(Networking.LocalPlayer) && Vector3.Distance(Networking.LocalPlayer.GetPosition(),transform.position)>160f)return;
        bool owner = Networking.IsOwner(gameObject);
        bool fresh = owner || (hasSnapshot && Time.time - snapshotTime < .6f);
        for (int i = 0; i < 7; i++)
        {
            float demand = fresh && driverId >= 0 ? thrusterDemand[i] : 0f;
            // Fast valve response; smoke already emitted persists in world space until lifetime ends.
            visibleThrust[i] = Mathf.MoveTowards(visibleThrust[i], demand, Time.deltaTime * (demand > visibleThrust[i] ? 14f : 9f));
            EmitBudgeted(thrusterJets[i], i, Mathf.Sqrt(visibleThrust[i]) * 320f);
            if (thrusterSmoke != null && thrusterSmoke.Length == 7)
                EmitBudgeted(thrusterSmoke[i], 7 + i, visibleThrust[i] * 185f);
            if (i < 4 && groundMist != null && groundMist.Length == 4)
            {
                RaycastHit hit;
                Transform jet = thrusterJets[i].transform;
                if (visibleThrust[i] > .02f && Physics.Raycast(jet.position, jet.forward, out hit, 5f, roadMask, QueryTriggerInteraction.Ignore))
                {
                    groundMist[i].transform.SetPositionAndRotation(hit.point + hit.normal * .06f, Quaternion.LookRotation(hit.normal));
                    EmitBudgeted(groundMist[i], 14 + i, visibleThrust[i] * 115f * (1f - hit.distance / 5f));
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
        netControl=Mathf.RoundToInt(Mathf.Clamp(steering,-1,1)*127+127) | (Mathf.RoundToInt(Mathf.Clamp(throttle,-1,1)*127+127)<<8);
        netLandingTelemetry=new Vector3(groundClearance,closingSpeed,landingAcceleration);
        netPilotAxes=new Vector3(pitchInput,steering,rollInput);
        netInputState=(vrInput ? 1 : 0)|(inputArmed ? 2 : 0)|(yokeHeld ? 4 : 0);
        RequestSerialization();
    }

    public override void OnPreSerialization()
    {
        if (!Networking.IsOwner(gameObject)) return;
        netPosition = body.position; netRotation = body.rotation; netVelocity = body.velocity;
        PackThrust();
        netControl=Mathf.RoundToInt(Mathf.Clamp(steering,-1,1)*127+127) | (Mathf.RoundToInt(Mathf.Clamp(throttle,-1,1)*127+127)<<8);
        netLandingTelemetry=new Vector3(groundClearance,closingSpeed,landingAcceleration);
        netPilotAxes=new Vector3(pitchInput,steering,rollInput);
        netInputState=(vrInput ? 1 : 0)|(inputArmed ? 2 : 0)|(yokeHeld ? 4 : 0);
    }

    public override void OnPostSerialization(SerializationResult result)
    {
        lastSerializedBytes = result.byteCount; lastSerializationSucceeded = result.success;
        if (!result.success) failedSerializations++;
    }

    public override void OnDeserialization()
    {
        if (Networking.IsOwner(gameObject)) return;
        ApplySeatHeight();
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
