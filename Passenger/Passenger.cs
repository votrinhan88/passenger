using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.UI;
using GTA.Native;

public class Passenger : Script
{
    // CONSTRUCTOR /////////////////////////////////////////////////////////////
    public static readonly Dictionary<string, string> metadata = new Dictionary<string, string>
    {
        {"name",      "Passenger"},
        {"developer", "votrinhan88"},
        {"version",   "1.1"},
        {"iniPath",   @"scripts\Passenger.ini"}
    };
    private static readonly Dictionary<string, Dictionary<string, object>> defaultSettingsDict = new Dictionary<string, Dictionary<string, object>>
    {
        {
            "SETTINGS", new Dictionary<string, object>
            {
                {"verbose",      Verbosity.WARNING},
                {"Interval",     200},
                {"keyPassenger", "G"}
            }
        },
        {
            "PARAMETERS", new Dictionary<string, object>
            {
                {"distanceClosestVehicle",   10.0f},
                {"timeoutEnterVehicle",    5000   },
            }
        },
    };
    private Dictionary<string, Dictionary<string, object>> settings = new Dictionary<string, Dictionary<string, object>>();
    private static Keys keyPassenger;

    public Passenger()
    {
        DevUtils.EnsureSettingsFile(
            (string)metadata["iniPath"],
            defaultSettingsDict,
            (int)defaultSettingsDict["SETTINGS"]["verbose"]
        );
        ScriptSettings loadedsettings = DevUtils.LoadSettings(
            (string)metadata["iniPath"],
            defaultSettingsDict,
            (int)defaultSettingsDict["SETTINGS"]["verbose"]
        );
        loadedsettings.Save();
        InitSettings(loadedsettings);

        // Config keyPassenger
        string keyPassengerString = (string)this.settings["SETTINGS"]["keyPassenger"];
        if (Enum.TryParse(keyPassengerString, out Keys _keyPassenger))
        {
            keyPassenger = _keyPassenger;
            if ((int)this.settings["SETTINGS"]["verbose"] >= Verbosity.INFO)
            {
                Notification.PostTicker($"keyPassenger set to {keyPassenger}.", true);
            }
        }

        Tick += OnTick;
        KeyDown += OnKeyDown;
        Interval = (int)this.settings["SETTINGS"]["Interval"];
    }

    private Dictionary<string, Dictionary<string, object>> InitSettings(ScriptSettings scriptSettings)
    {
        foreach (string sectionName in scriptSettings.GetAllSectionNames())
        {
            this.settings.Add(sectionName, new Dictionary<string, object>());
            foreach (string keyName in scriptSettings.GetAllKeyNames(sectionName))
            {   
                Type type = defaultSettingsDict[sectionName][keyName].GetType();
                this.settings[sectionName].Add(keyName, Convert.ChangeType(scriptSettings.GetValue(sectionName, keyName, defaultSettingsDict[sectionName][keyName]), type));
            }
        }

        if ((int)this.settings["SETTINGS"]["verbose"] >= Verbosity.INFO)
        {
            Notification.PostTicker($"~b~{metadata["name"]} ~g~{metadata["version"]}~w~ has been loaded.", true);
        }
        return settings;
    }


    // VARIABLES ///////////////////////////////////////////////////////////////
    private static Ped player => Game.Player.Character;
    private ModState modState = ModState.Detached;
    private string attachedVehicleBoneName = string.Empty;
    private VehicleSeat seatToBreach = VehicleSeat.None;
    private VehicleDoorIndex doorToBreachIndex = (VehicleDoorIndex)(-1);
    private enum ModState : int
    {
        // CurrentState             // --> NextStates                           // TODO All NextStates
        Unknown = -1,               // --> {Detached}                           // {Detached}
        Detached = 0,               // --> {OneHanded}                          // {OneHanded}

        OneHanded = 10,             // --> {TwoHanded}                          // {Detached, TwoHanded}
        TwoHanded = 11,             // --> {TwoHandedTop}                       // {OneHanded, OneHandedTop}

        OneHandedTop = 20,          // --> {}                                   // {TwoHanded<Back>, TwoHandedTop}
        TwoHandedTop = 21,          // --> {AttemptedOpenDoor}                  // {OneHandedTop, AttemptedOpenDoor, BrokenDoor, SnatchedPassenger, WarpedIn}
        AttemptedOpenDoor = 22,     // --> {AttemptedOpenDoor, BrokenDoor}      // {OneHandedTop, AttemptedOpenDoor, BrokenDoor, SnatchedPassenger, WarpedIn}
        BrokenDoor = 23,            // --> {WarpedIn}                           // {OneHandedTop, SnatchedPassenger, WarpedIn}
        // SnatchedPassenger = 24   // --> {}                                   // {OneHandedTop, WarpedIn}

        WarpedIn = 30,              // --> {}                                   // {Seated}
        // Seated = 31              // --> {}                                   // {Exited} - check in OnTick (?)

        // Note: TwoHanded<Back> is the same ModState as TwoHanded, just with
        // different attachedVehicleBoneName
        // 
        // ┌──────────────────────────────────────────────────────────────────────────────────────────────┐ 
        // │ Current ModState graph:                                               https://asciiflow.com/ │ 
        // │                                                  ┌┐                                          │ 
        // │                                                  ││                                          │ 
        // │                                                  ▼▲                                          │ 
        // │  U  ►──► D  ►──► 1H ►──► 1H ►──► 1T ►──► 2T ►──► AD ►──► BD ►──────────► WI                  │ 
        // │                                                                                              │ 
        // └──────────────────────────────────────────────────────────────────────────────────────────────┘ 
        // ┌──────────────────────────────────────────────────────────────────────────────────────────────┐
        // │ TODO ModState graph:                                                                         │
        // │                                                                                              │
        // │           ┌───────────────────────────────────────────────────────────────────────────────┐  │
        // │           │                                                                               │  │
        // │           │                       ┌───◄───────────────────────◄───┐                       │  │
        // │           │                       ├──◄─────────────────◄──┐       │                       │  │
        // │           │                       ├─◄───────────◄─┐       │       │                       │  │
        // │           │                       │              ┌┤       │       │                       │  │
        // │           │                       ▼              ││       │       │                       │  │
        // │           │                       ▼              │▲       │       │                       │  │
        // │           ▼                       ▼              ▼▲       ▲       ▲                       ▲  │
        // │  U  ►──► D  ►──► 1H ►──► 2H ►──► 1T ►──► 2T ►──► AD ►──► BD ►──► SP ►──► WI ►──► S  ►──► E   │
        // │           ▲      ▼▲      ▼▲      ▼▲      ▼▼       ▼      ▲▼      ▲       ▲                   │
        // │           └──────┘└──────┘└──────┘└──────┘▼       ▼      ││      ▲       ▲                   │
        // │                                           ▼       │      ││      │       ▲                   │
        // │                                           │       │      ││      │       │                   │
        // │                                           ├─►─────│────►─┘│      │       │                   │
        // │                                           │       │       │      │       │                   │
        // │                                           ▼       ├─►─────│────►─┤       │                   │
        // │                                           ▼       │       │      │       │                   │
        // │  ┌───────────────────────────┐            │       ▼       └─►────┼─────►─┤                   │
        // │  │                           │            │       │              │       │                   │
        // │  │ ▼▲  : In/out nodes        │            │       │              │       ▲                   │
        // │  │                           │            ├──►────├──────────────┘       ▲                   │
        // │  │ ─►  : 1-state transition  │            │       │                      │                   │
        // │  │                           │            ▼       └──►────────────────►──┤                   │
        // │  │ ─►►►: n-state transition  │            │                              │                   │
        // │  │                           │            │                              │                   │
        // │  └───────────────────────────┘            └───►──────────────────────►───┘                   │
        // │                                                                                              │
        // └──────────────────────────────────────────────────────────────────────────────────────────────┘
        // 
        // ┌──To─┬──┬──┬──┬──┬──┬──┬──┬──┬──┬──┬──┐
        // │  │ U│ D│1H│2H│1T│2T│AD│BD│SP│WI│ S│ E│
        // From──┼──┼──┼──┼──┼──┼──┼──┼──┼──┼──┼──┤
        // │ U│ .  X  .  .  .  .  .  .  .  .  .  .│
        // │ D│ .  .  X  .  .  .  .  .  .  .  .  .│
        // │1H│ .  O  .  X  .  .  .  .  .  .  .  .│
        // │2H│ .  .  O  .  X  .  .  .  .  .  .  .│
        // │1T│ .  .  .  O  .  X  .  .  .  .  .  .│
        // │2T│ .  .  .  .  O  .  X  O  O  O  .  .│
        // │AD│ .  .  .  .  O  .  X  X  O  O  .  .│
        // │BD│ .  .  .  .  O  .  .  .  O  X  .  .│
        // │SP│ .  .  .  .  O  .  .  .  .  O  .  .│
        // │WI│ .  .  .  .  .  .  .  .  .  .  O  .│
        // │ S│ .  .  .  .  .  .  .  .  .  .  .  O│
        // │ E│ .  O  .  .  .  .  .  .  .  .  .  .│
        // └──┴───────────────────────────────────┘
    }


    // EVENTS //////////////////////////////////////////////////////////////////
    private void OnTick(object sender, EventArgs e)
    {
        ShowDebugInfo();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == keyPassenger)
        {
            if (player.IsInVehicle() == false)
            {
                EnterClosestVehicleAsPassenger();
            }
            else
            {
                if (player.IsAiming == true)
                {
                    ThreatenOccupants();
                    Ped driver = player.CurrentVehicle.Driver;
                    MakeDriverDriveOrCruise(driver);
                    MakeDriverReckless(driver);
                }
                else
                {
                    CycleFreeSeatsWhileOnVehicle();
                }
            }
        }

        if ((e.KeyCode == Keys.H) & (e.Modifiers == Keys.Shift))
        {
            switch (this.modState)
            {
                // Safe measures. Sometimes the grabbing doesn't work.
                case ModState.Unknown:
                    this.modState = ResetMod();
                    break;
                case ModState.Detached:
                    this.modState = AttachOneHandToTargetedVehicle();
                    break;
                // TODO: Needs to fix state transition here. Maybe
                // AttachOneHandToTargetedVehicle() only if the player is
                // aiming, else (?) finish sequence ResetMod()
                case ModState.WarpedIn:
                    this.modState = AttachOneHandToTargetedVehicle();
                    break;
                default:
                    this.modState = DetachFromCurrentVehicle();
                    break;
            }
        }

        if (e.KeyCode == Keys.Space)
        {
            switch (this.modState)
            {
                case ModState.OneHanded:
                    this.modState = AttachOtherHandToCurrentVehicle();
                    break;
                case ModState.TwoHanded:
                    this.modState = ClimbOneHandToTop();
                    break;
                case ModState.OneHandedTop:
                    this.modState = ClimbOtherHandToTop();
                    break;
                case ModState.TwoHandedTop:
                    this.modState = AttemptOpenDoorCurrentVehicle();
                    break;
                case ModState.AttemptedOpenDoor:
                    this.modState = BreakDoorCurrentVehicle();
                    break;
                case ModState.BrokenDoor:
                    this.modState = WarpInCurrentVehicle();
                    break;
                // case ModState.BrokenDoor:
                //     this.modState = MaybeSnatchPassenger();
                //     break;
                // case ModState.SnatchPassenger:
                //     this.modState = LetGoPassenger();
                //     break;
                case ModState.WarpedIn:
                    // this.modState = ResetMod();
                    break;
            }
        }

        if (e.KeyCode == Keys.J)
        {
            if (player.IsInVehicle())
            {
                player.CurrentVehicle.Velocity = 1.5f * player.CurrentVehicle.Velocity;
            }
        }
    }

    private void ShowDebugInfo()
    {
        string subtitle = "";
        subtitle += $"player: {player.Position.Round(2)}\n";
        subtitle += $"modState: {this.modState}\n";

        Vehicle closestVehicle = World.GetClosestVehicle(player.Position, 50f);
        if (closestVehicle != null)
        {
            string closestBoneName = GetClosestVehicleBoneNameSweep(closestVehicle, player.Bones[Bone.SkelHead].Position);
            subtitle += $"closestBoneName = {closestVehicle.DisplayName}.{closestBoneName}\n";
        }

        if ((this.modState > ModState.Detached) & (this.modState < ModState.WarpedIn))
        {
            Vehicle vehicle = (Vehicle)player.AttachedEntity;
            if (vehicle != null)
            {
                subtitle += $"vehicle = {vehicle}, {vehicle.Position.Round(2)}\n";
                subtitle += $"this.attachedVehicleBoneName = {this.attachedVehicleBoneName}\n";
                World.DrawLine(vehicle.Bones[this.attachedVehicleBoneName].Position, player.Bones[Bone.PHLeftHand].Position, System.Drawing.Color.Red);
                World.DrawLine(vehicle.Bones[this.attachedVehicleBoneName].Position, vehicle.Position, System.Drawing.Color.Red);

                string closestDoorName = GetClosestVehicleDoorSelective(
                    vehicle,
                    vehicle.Bones[this.attachedVehicleBoneName].Position
                );
                if (closestDoorName != string.Empty)
                {
                    EntityBone closestDoorBone = vehicle.Bones[closestDoorName];
                    subtitle += $"closestDoorName = {closestDoorName}\n";
                    World.DrawLine(vehicle.Bones[this.attachedVehicleBoneName].Position, closestDoorBone.Position, System.Drawing.Color.Red);
                }

                if (this.modState >= ModState.AttemptedOpenDoor)
                {
                    if (this.doorToBreachIndex != (VehicleDoorIndex)(-1))
                    {
                        VehicleDoor doorToBreach = vehicle.Doors[this.doorToBreachIndex];
                        subtitle += $"this.seatToBreach = {this.seatToBreach}\n";
                        subtitle += $"this.doorToBreachIndex = {this.doorToBreachIndex}\n";
                        subtitle += $"=> doorToBreach.AngleRatio = {doorToBreach.AngleRatio}\n";
                    }
                }
            }
        }
        GTA.UI.Screen.ShowSubtitle(subtitle, (int)this.settings["SETTINGS"]["Interval"]);
    }


    // PASSENGER ///////////////////////////////////////////////////////////////
    private void EnterClosestVehicleAsPassenger()
    {
        float distanceClosestVehicle = (float)this.settings["PARAMETERS"]["distanceClosestVehicle"];
        int timeoutEnterVehicle = (int)this.settings["PARAMETERS"]["timeoutEnterVehicle"];
        Ped player = Game.Player.Character;
        Vehicle closestVehicle = World.GetClosestVehicle(player.Position, distanceClosestVehicle);

        if (closestVehicle != null && closestVehicle.IsDriveable)
        {
            EnterVehicleFlags enterVehicleFlags;
            VehicleSeat bestSeat;

            VehicleSeat freeSeat = FindFirstFreePassengerSeat(closestVehicle);
            if (freeSeat == VehicleSeat.None)
            {
                bestSeat = VehicleSeat.Passenger;
                enterVehicleFlags = EnterVehicleFlags.None;
            }
            else
            {
                bestSeat = freeSeat;
                enterVehicleFlags = EnterVehicleFlags.DontJackAnyone;
            }

            player.Task.EnterVehicle(
                closestVehicle,
                bestSeat,
                timeoutEnterVehicle,
                2f,
                enterVehicleFlags
            );

            if ((int)this.settings["SETTINGS"]["verbose"] >= Verbosity.INFO)
            {
                Notification.PostTicker($"Enter seat {bestSeat}.", true);
            }
        }
    }

    private void CycleFreeSeatsWhileOnVehicle()
    {
        Vehicle vehicle = player.CurrentVehicle;
        if ((vehicle.GetPedOnSeat(VehicleSeat.Driver) != null) && (vehicle.PassengerCount == vehicle.PassengerCapacity))
        {
            if ((int)this.settings["SETTINGS"]["verbose"] >= Verbosity.WARNING)
            {
                Notification.PostTicker("No free seat available.", true);
            }
        }

        var idxCurrentSeat = (int)player.SeatIndex;
        int idxNextSeat = idxCurrentSeat;

        // Find next free seat
        for (int i = 0; i < vehicle.PassengerCapacity + 1; i++)
        {
            if (idxNextSeat + 1 <= vehicle.PassengerCapacity)
            {
                idxNextSeat = idxNextSeat + 1;
            }
            else
            {
                idxNextSeat = (int)VehicleSeat.Driver;
            }

            if (vehicle.IsSeatFree((VehicleSeat)idxNextSeat))
            {
                player.Task.WarpIntoVehicle(vehicle, (VehicleSeat)idxNextSeat);
                if ((int)this.settings["SETTINGS"]["verbose"] >= Verbosity.INFO)
                {
                    Notification.PostTicker($"Switch to {(VehicleSeat)idxNextSeat}.", true);
                }
                return;
            }
        }
    }

    private void ThreatenOccupants()
    {
        bool notify = false;
        foreach (Ped ped in player.CurrentVehicle.Occupants)
        {
            if (ped == player) {
                continue;
            }

            ped.PlayAmbientSpeech("GENERIC_FRIGHTENED_HIGH", SpeechModifier.ShoutedCritical);
            ped.SetFleeAttributes((
                FleeAttributes.CanScream
                | FleeAttributes.DisableExitVehicle
            ), true);

            if ((int)this.settings["SETTINGS"]["verbose"] >= Verbosity.INFO)
            {
                if (!notify)
                {
                    Notification.PostTicker($"Peds threatened not to leave vehficle.", true);
                    notify = true;
                }
            }
        }
    }

    private void MakeDriverReckless(Ped driver)
    {
        if (driver == null) { return; }
        if (!driver.IsAlive) { return; }
        if (driver == player) { return; }
        
        driver.VehicleDrivingFlags = ((
            VehicleDrivingFlags.SwerveAroundAllVehicles
            | VehicleDrivingFlags.SteerAroundStationaryVehicles
            | VehicleDrivingFlags.SteerAroundPeds
            | VehicleDrivingFlags.SteerAroundObjects
            | VehicleDrivingFlags.DontSteerAroundPlayerPed
            | VehicleDrivingFlags.GoOffRoadWhenAvoiding
            | VehicleDrivingFlags.UseShortCutLinks
            | VehicleDrivingFlags.ChangeLanesAroundObstructions
        ));
        driver.SetConfigFlag(PedConfigFlagToggles.IsAgitated, true);
        driver.SetFleeAttributes(FleeAttributes.UseVehicle, true);
        driver.SetCombatAttribute(CombatAttributes.FleeWhilstInVehicle, true);
        driver.DrivingAggressiveness = 1.0f;
        driver.DrivingSpeed = 9999f;
        driver.MaxDrivingSpeed = 9999f;
        if ((int)this.settings["SETTINGS"]["verbose"] >= Verbosity.INFO)
        {
            Notification.PostTicker($"Driver became reckless.", true);
        }
    }

    private void MakeDriverDriveOrCruise(Ped driver) {
        VehicleDrivingFlags drivingFlags = (
            VehicleDrivingFlags.SwerveAroundAllVehicles
            | VehicleDrivingFlags.SteerAroundStationaryVehicles
            | VehicleDrivingFlags.SteerAroundPeds
            | VehicleDrivingFlags.SteerAroundObjects
            | VehicleDrivingFlags.DontSteerAroundPlayerPed
            | VehicleDrivingFlags.GoOffRoadWhenAvoiding
            | VehicleDrivingFlags.UseShortCutLinks
            | VehicleDrivingFlags.ChangeLanesAroundObstructions
        );
        driver.PlayAmbientSpeech("GENERIC_FRIGHTENED_HIGH", SpeechModifier.ShoutedCritical);

        driver.Task.ClearAll();
        TaskSequence taskSequence = new TaskSequence(
        );
        if (Game.IsWaypointActive)
        {
            taskSequence.AddTask.DriveTo(driver.CurrentVehicle, World.WaypointPosition, 9999f, drivingFlags, 10f);
            if ((int)this.settings["SETTINGS"]["verbose"] >= Verbosity.INFO)
            {
                Notification.PostTicker($"Driver going to Waypoint.", true);
            }
        }
        taskSequence.AddTask.CruiseWithVehicle(driver.CurrentVehicle, 9999f, drivingFlags);
        driver.Task.PerformSequence(taskSequence);
    }

    private VehicleSeat FindFirstFreePassengerSeat(Vehicle vehicle)
    {
        VehicleSeat freeSeat = VehicleSeat.None;
        for (int i = 0; i < vehicle.PassengerCapacity + 1; i++)
        {
            if (vehicle.IsSeatFree((VehicleSeat)i))
            {
                freeSeat = (VehicleSeat)i;
                break;
            }
        }
        return freeSeat;
    }


    // HITCHHIKING /////////////////////////////////////////////////////////////
    // HITCHHIKING.MODSTATE ////////////////////////////////////////////////////
    private ModState ResetMod()
    {
        this.attachedVehicleBoneName = string.Empty;
        this.doorToBreachIndex = (VehicleDoorIndex)(-1);
        this.seatToBreach = VehicleSeat.None;
        player.Detach();
        player.CanRagdoll = true;
        player.CancelRagdoll();
        player.IsPositionFrozen = false;
        return ModState.Detached;
    }

    private ModState DetachFromCurrentVehicle()
    {
        // Check: Vehicle is not null
        Vehicle vehicle = (Vehicle)player.AttachedEntity;
        if (vehicle == null) { return ModState.Unknown; }

        Vector3 offsetPlayerFromVehicle = (
            (player.Bones[Bone.PHLeftHand].Position + player.Bones[Bone.PHRightHand].Position) / 2
            - vehicle.Position
        );
        player.Detach();
        player.CancelRagdoll();
        player.Task.ClearAllImmediately();
        player.Ragdoll(1000);
        player.ApplyForce(offsetPlayerFromVehicle.Normalized + Vector3.WorldUp, Vector3.Zero, ForceType.ExternalImpulse);
        player.Velocity = vehicle.Velocity;
        this.attachedVehicleBoneName = "";
        return ModState.Detached;
    }

    private ModState AttachOneHandToTargetedVehicle()
    {
        // Check: Raycast hit a vehicle
        RaycastResult raycastResult = World.Raycast(
            GameplayCamera.Position,
            GameplayCamera.Position + GameplayCamera.Direction * 500f, // TODO: Change to 12-15 in release
            IntersectFlags.Vehicles,
            player
        );
        if ((!raycastResult.DidHit) | (raycastResult.HitEntity is not Vehicle))
        {
            return ModState.Detached;
        }

        // Check: Vehicle is not null
        Vehicle vehicle = (Vehicle)raycastResult.HitEntity;
        if (vehicle == null) { return ModState.Detached; }

        string closestBoneName = GetClosestVehicleBoneNameSelective(
            vehicle,
            player.Bones[Bone.SkelHead].Position // This makes more sense.
        );
        this.attachedVehicleBoneName = closestBoneName;
        EntityBone vehicleClosestBone = vehicle.Bones[this.attachedVehicleBoneName];
        AttachBoneToVehicleBone(Bone.PHLeftHand, vehicleClosestBone, LeftRight.Left);
        MakeDriverReckless(vehicle.GetPedOnSeat(VehicleSeat.Driver));
        return ModState.OneHanded;
    }

    private ModState AttachOtherHandToCurrentVehicle()
    {
        // Check: Player must be already attached to a vehicle
        // if (this.stateIsPhysicallyAttachedToVehicle == false) { return false; }
        if (this.modState == ModState.Detached) { return ModState.Unknown; }

        // Check: Vehicle is not null
        Vehicle vehicle = (Vehicle)player.AttachedEntity;
        if (vehicle == null) { return ModState.Unknown; }

        EntityBone vehicleClosestBone = vehicle.Bones[this.attachedVehicleBoneName];
        AttachBoneToVehicleBone(Bone.PHRightHand, vehicleClosestBone, LeftRight.Right);
        return ModState.TwoHanded;
    }

    private ModState ClimbOneHandToTop()
    {
        if (this.modState != ModState.TwoHanded)
        {
            return ModState.Unknown;
        }
        // Check: attachedVehicleBoneName is not empty
        if (this.attachedVehicleBoneName == string.Empty) { return ModState.Unknown; }

        // Check: Vehicle is not null
        Vehicle vehicle = (Vehicle)player.AttachedEntity;
        if (vehicle == null) { return ModState.Unknown; }

        string topMostVehicleBoneName = GetClosestVehicleBoneNameSweep(vehicle, vehicle.GetOffsetPosition(3f * Vector3.RelativeTop));
        this.attachedVehicleBoneName = topMostVehicleBoneName;
        // Alternative solution: Instead of getting the highest bone (which is
        // not always reliable for estimating the hand-attaching position), can
        // use raycast to detect the topmost part of vehicle? Based on the
        // selected seat? 
        // Then we can move this raycast part to one of the previous state.

        player.Detach();
        // player.ApplyForce(2f*Vector3.WorldUp, Vector3.Zero, ForceType.ExternalImpulse);
        player.Velocity = 1.05f * vehicle.Velocity + 5f * Vector3.WorldUp;
        AttachBoneToVehicleBone(
            Bone.PHLeftHand,
            vehicle.Bones[this.attachedVehicleBoneName],
            LeftRight.Left,
            0.9f * Vector3.RelativeTop + 0.3f * vehicleBoneLeftGrabOffset["boot"]
        );
        return ModState.OneHandedTop;
    }

    private ModState ClimbOtherHandToTop()
    {
        // Check: Player must be attached with two hands
        if (this.modState != ModState.OneHandedTop)
        {
            return ModState.Unknown;
        }
        // Check: attachedVehicleBoneName is not empty
        if (this.attachedVehicleBoneName == string.Empty) { return ModState.Unknown; }

        // Check: Vehicle is not null
        Vehicle vehicle = (Vehicle)player.AttachedEntity;
        if (vehicle == null) { return ModState.Unknown; }

        // Door is a bone, with extended functionalities
        // Check: Ensure vehicle has a door (else solve condition, e.g. motorbike)
        string closestDoorName = GetClosestVehicleDoorSelective(
            vehicle,
            vehicle.Bones[this.attachedVehicleBoneName].Position
        );
        if (closestDoorName == "") { return ModState.Unknown; }

        AttachBoneToVehicleBone(
            Bone.PHRightHand,
            vehicle.Bones[this.attachedVehicleBoneName],
            LeftRight.Right,
            0.9f * Vector3.RelativeTop - 0.3f * vehicleBoneLeftGrabOffset["boot"]
        );
        return ModState.TwoHandedTop;
    }

    private ModState AttemptOpenDoorCurrentVehicle()
    {
        // Check: Climb to door before opening
        if (
            (this.modState != ModState.TwoHandedTop)
            & (this.modState != ModState.AttemptedOpenDoor)
        )
        {
            return ModState.Unknown;
        }

        // Check: Vehicle is not null
        Vehicle vehicle = (Vehicle)player.AttachedEntity;
        if (vehicle == null) { return ModState.Unknown; }

        // TwoHandedTop -> Find seat and door and attempt to breach
        if (this.modState == ModState.TwoHandedTop)
        {
            // if (    withDoors &     withFreeSeats): Find free seat    --> find door --> (BreakDoorCurrentVehicle)                     --> WarpInCurrentVehicle
            // if (    withDoors & not withFreeSeats): Find closest door --> find seat --> (BreakDoorCurrentVehicle) --> SnatchPassenger --> WarpInCurrentVehicle
            // if (not withDoors &     withFreeSeats):                                                                                   --> WarpInCurrentVehicle
            // if (not withDoors & not withFreeSeats): Find closest door --> find seat                               --> SnatchPassenger --> WarpInCurrentVehicle
            VehicleSeat[] allFreeSeats = GetAllFreeSeats(vehicle);
            bool withFreeSeats = (allFreeSeats.Length > 0);
            bool withDoors = (vehicle.Doors.ToArray().Length > 0);

            if (withDoors)
            {
                if (withFreeSeats)
                {
                    Notification.PostTicker($"withDoors={withDoors}, withFreeSeats={withFreeSeats}.", true);
                    this.seatToBreach = GetClosestFreeSeat(vehicle, allFreeSeats, player.Position);
                    this.doorToBreachIndex = (VehicleDoorIndex)SeatToDoorIndex[(int)this.seatToBreach];
                }
                else // if (!withFreeSeats)
                {
                    Notification.PostTicker($"withDoors={withDoors}, withFreeSeats={withFreeSeats}.", true);
                    this.doorToBreachIndex = GetClosestVehicleDoorSweep(vehicle, player.Position);
                    this.seatToBreach = (VehicleSeat)DoorIndexToSeat[(int)this.doorToBreachIndex];
                    Notification.PostTicker($"doorToBreachIndex={doorToBreachIndex}, seatToBreach={seatToBreach}.", true);
                }
            }
            else // if (!withDoors)
            {
                if (withFreeSeats)
                {
                    Notification.PostTicker($"withDoors={withDoors}, withFreeSeats={withFreeSeats}.", true);
                    Notification.PostTicker($"AttemptOpenDoorCurrentVehicle -> WarpInCurrentVehicle.", true);
                    return WarpInCurrentVehicle();
                }
                else // if (!withFreeSeats)
                {
                    Notification.PostTicker($"withDoors={withDoors}, withFreeSeats={withFreeSeats}.", true);
                    Notification.PostTicker($"AttemptOpenDoorCurrentVehicle -> SnatchPassenger (n.a yet).", true);
                    return ModState.Unknown;
                }
            }
        }
        else
        {
            // Check: Door is not null
            if (this.doorToBreachIndex == (VehicleDoorIndex)(-1))
            {
                Notification.PostTicker($"modState={modState}: No door to breach.", true);
                return ModState.Unknown;
            }
            // Check: Seat is not null
            if (this.seatToBreach == VehicleSeat.None)
            {
                Notification.PostTicker($"modState={modState}: No seat to breach.", true);
                return ModState.Unknown;
            }
        }

        // Attempt to open door (again?)
        VehicleDoor doorToBreach = vehicle.Doors[this.doorToBreachIndex];
        if (doorToBreach.IsOpen == false)
        {
            doorToBreach.Open(true, false);
        }

        doorToBreach.AngleRatio = Math.Min(doorToBreach.AngleRatio + 0.3f, 1.0f);
        return ModState.AttemptedOpenDoor;
    }

    private ModState BreakDoorCurrentVehicle()
    {
        // Check: Open door before breaking
        if (this.modState != ModState.AttemptedOpenDoor)
        {
            return ModState.Unknown;
        }

        // Check: Vehicle is not null
        Vehicle vehicle = (Vehicle)player.AttachedEntity;
        if (vehicle == null) { return ModState.Unknown; }

        // Check: Door is not null
        if (this.doorToBreachIndex == (VehicleDoorIndex)(-1)) { return ModState.Unknown; }

        VehicleDoor doorToBreach = vehicle.Doors[this.doorToBreachIndex];
        if ((doorToBreach.IsOpen == false) | (doorToBreach.AngleRatio < 0.6f))
        {
            return AttemptOpenDoorCurrentVehicle();
        }
        doorToBreach.Break();
        return ModState.BrokenDoor;
    }

    private ModState WarpInCurrentVehicle()
    {
        // Check: Break door before warping in
        if (this.modState != ModState.BrokenDoor)
        {
            return ModState.Unknown;
        }

        // Check: Vehicle is not null
        Vehicle vehicle = (Vehicle)player.AttachedEntity;
        if (vehicle == null) { return ModState.Unknown; }

        // Check: Seat to breach is not null
        if (this.seatToBreach == VehicleSeat.None)
        {
            Notification.PostTicker($"modState={modState}: No seat to breach.", true);
            return ModState.Unknown;
        }

        Vector3 vehicleVelocity = vehicle.Velocity;
        player.SetIntoVehicle(vehicle, this.seatToBreach);
        vehicle.SetShouldFreezeWaitingOnCollision(false);
        for (int i = 0; i < 20; i++)
        {
            vehicle.LocalRotationVelocity *= -0.5f;
            Vector3 speedDifference = (vehicleVelocity - vehicle.Velocity);
            speedDifference.Z *= -0.5f;
            vehicle.Velocity = vehicle.Velocity + 0.3f * speedDifference;
            vehicle.ApplyWorldForceCenterOfMass(0.3f * speedDifference, ForceType.InternalImpulse, true, true);
            Script.Wait(50);
        }
        // Hash.SMASH_VEHICLE_WINDOW
        // vehicle.AttachTo(player, Vector3.Zero, Vector3.Zero); // DON'T
        // Function.Call(Hash.CLEAR_DEFAULT_PRIMARY_TASK, vehicle.Driver);
        // Function.Call(Hash.CLEAR_PRIMARY_VEHICLE_TASK, vehicle);
        // Function.Call(Hash.CLEAR_VEHICLE_CRASH_TASK, vehicle);
        return ModState.WarpedIn;
        // SET_VEHICLE_HAS_BEEN_OWNED_BY_PLAYER
        // SET_VEHICLE_HAS_BEEN_DRIVEN_FLAG
        // Entity.
    }


    // HITCHHIKING.GETTERS /////////////////////////////////////////////////////
    private static readonly Dictionary<int, string> DoorIndexToDoorBoneName = new Dictionary<int, string> {
        { (int)VehicleDoorIndex.FrontLeftDoor,  "door_dside_f"},
        { (int)VehicleDoorIndex.FrontRightDoor, "door_pside_f"},
        { (int)VehicleDoorIndex.BackLeftDoor,   "door_dside_r"},
        { (int)VehicleDoorIndex.BackRightDoor,  "door_pside_r"},
        { (int)VehicleDoorIndex.Hood,           "bonnet"},
        { (int)VehicleDoorIndex.Trunk,          "boot"},
    };
    private static readonly Dictionary<int, string> SeatToSeatBoneName = new Dictionary<int, string> {
        { (int)VehicleSeat.Driver,    "seat_dside_f"}, // VehicleSeat.LeftFront
        { (int)VehicleSeat.Passenger, "seat_pside_f"}, // VehicleSeat.RightFront
        { (int)VehicleSeat.LeftRear,  "seat_dside_r"},
        { (int)VehicleSeat.RightRear, "seat_pside_r"},
    };
    private static readonly Dictionary<int, int> DoorIndexToSeat = new Dictionary<int, int> {
        { (int)VehicleDoorIndex.FrontLeftDoor,  (int)VehicleSeat.Driver },
        { (int)VehicleDoorIndex.FrontRightDoor, (int)VehicleSeat.Passenger },
        { (int)VehicleDoorIndex.BackLeftDoor,   (int)VehicleSeat.LeftRear },
        { (int)VehicleDoorIndex.BackRightDoor,  (int)VehicleSeat.RightRear },
        // { (int)VehicleDoorIndex.Hood,           (int)VehicleSeat.Driver },    // Experimental
        // { (int)VehicleDoorIndex.Trunk,          (int)VehicleSeat.RightRear }, // Experimental
    };
    private static readonly Dictionary<int, int> SeatToDoorIndex = new Dictionary<int, int> {
        { (int)VehicleSeat.Driver,      (int)VehicleDoorIndex.FrontLeftDoor},
        { (int)VehicleSeat.Passenger,   (int)VehicleDoorIndex.FrontRightDoor},
        { (int)VehicleSeat.LeftRear,    (int)VehicleDoorIndex.BackLeftDoor},
        { (int)VehicleSeat.RightRear,   (int)VehicleDoorIndex.BackRightDoor},
    };


    private static string GetClosestVehicleBoneNameSelective(Vehicle vehicle, Vector3 position)
    {
        float closestDistance = float.MaxValue;
        string closestBoneName = string.Empty;

        foreach (string boneName in vehicleBoneSafeGrabOffset.Keys)
        {
            EntityBone bone = vehicle.Bones[boneName];
            float distance = Vector3.Distance(bone.Position, position);
            if (distance < closestDistance)
            {
                closestBoneName = boneName;
                closestDistance = distance;
            }
        }
        return closestBoneName;
    }

    private static string GetClosestVehicleBoneNameSweep(Entity Entity, Vector3 position)
    {
        float closestDistance = float.MaxValue;
        string closestBoneName = "";
        for (int i = 0; i < Entity.Bones.Count; i++)
        {
            float distance = Vector3.Distance(Entity.Bones[i].Position, position);
            if (distance < closestDistance)
            {
                closestBoneName = Entity.Bones[i].Name;
                closestDistance = distance;
            }
        }
        return closestBoneName;
    }

    private VehicleSeat[] GetAllFreeSeats(Vehicle vehicle)
    {
        List<VehicleSeat> freeSeats = new List<VehicleSeat>();
        for (int i = 0; i < vehicle.PassengerCapacity + 1; i++)
        {
            if (vehicle.IsSeatFree((VehicleSeat)i))
            {
                freeSeats.Add((VehicleSeat)i);
            }
        }
        return freeSeats.ToArray();
    }

    private static VehicleSeat GetClosestFreeSeat(Vehicle vehicle, VehicleSeat[] freeSeats, Vector3 position)
    {
        VehicleSeat closestFreeSeat = VehicleSeat.None;
        if (freeSeats.Length == 0) { return closestFreeSeat; }

        float closestDistance = float.MaxValue;
        foreach (VehicleSeat seat in freeSeats)
        {
            float distance = World.GetDistance(vehicle.Bones[SeatToSeatBoneName[(int)seat]].Position, position);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestFreeSeat = seat;
            }
        }
        return closestFreeSeat;
    }

    private static string GetClosestVehicleDoorSelective(Vehicle vehicle, Vector3 position)
    {
        float closestDistance = float.MaxValue;
        string closestDoorName = string.Empty;

        foreach (string boneDoorName in new string[] {
            "handle_dside_f",
            "handle_pside_f",
            "handle_dside_r",
            "handle_pside_r"
        })
        {
            EntityBone bone = vehicle.Bones[boneDoorName];
            float distance = Vector3.Distance(vehicle.Bones[boneDoorName].Position, position);
            if (distance < closestDistance)
            {
                closestDoorName = boneDoorName;
                closestDistance = distance;
            }
        }
        return closestDoorName;
    }

    private static VehicleDoorIndex GetClosestVehicleDoorSweep(Vehicle vehicle, Vector3 position)
    {
        float closestDistance = float.MaxValue;
        VehicleDoorIndex closestDoorIndex = (VehicleDoorIndex)(-1);

        VehicleDoorCollection doors = vehicle.Doors;
        foreach (VehicleDoor door in doors)
        {
            VehicleDoorIndex doorIndex = door.Index;
            // Skip if unbreachable door, i.e., VehicleDoorIndex.{Hood, Trunk}
            if (!DoorIndexToSeat.ContainsKey((int)doorIndex)) { continue; }

            string doorBoneName = DoorIndexToDoorBoneName[(int)doorIndex];
            EntityBone doorBone = vehicle.Bones[doorBoneName];
            float distance = Vector3.Distance(vehicle.Bones[doorBoneName].Position, position);
            if (distance < closestDistance)
            {
                closestDoorIndex = doorIndex;
                closestDistance = distance;
            }
        }
        Notification.PostTicker($"closestDoorIndex: {closestDoorIndex}.", true);
        return closestDoorIndex;
    }


    // HITCHHIKING.ATTACH //////////////////////////////////////////////////////
    private static readonly Dictionary<string, Vector3> vehicleBoneSafeGrabOffset = new Dictionary<string, Vector3> {
        { "handle_dside_f", Vector3.RelativeLeft },
        { "handle_pside_f", Vector3.RelativeRight },
        { "handle_dside_r", Vector3.RelativeLeft },
        { "handle_pside_r", Vector3.RelativeRight },
        // { "door_dside_f", Vector3.RelativeLeft },
        // { "door_pside_f", Vector3.RelativeRight },
        // { "door_dside_r", Vector3.RelativeLeft },
        // { "door_pside_r", Vector3.RelativeRight },
        { "bonnet",         Vector3.RelativeTop + Vector3.RelativeFront }, // Needs a larger offset
        { "windscreen",     Vector3.RelativeTop + Vector3.RelativeFront }, // Needs a larger offset
        { "boot",           Vector3.RelativeTop + Vector3.RelativeBack },  // Needs a larger offset
        { "windscreen_r",   (Vector3.RelativeTop + Vector3.RelativeBack).Normalized },
        { "seat_r",         Vector3.Zero },
        { "skid_l",         Vector3.Zero }, // (?) for heli
        { "skid_r",         Vector3.Zero }, // (?) for heli
    };
    private static readonly Dictionary<string, Vector3> vehicleBoneLeftGrabOffset = new Dictionary<string, Vector3> {
        { "handle_dside_f", Vector3.RelativeFront },
        { "handle_pside_f", Vector3.RelativeBack },
        { "handle_dside_r", Vector3.RelativeFront },
        { "handle_pside_r", Vector3.RelativeBack },
        // { "door_dside_f", Vector3.RelativeLeft },
        // { "door_pside_f", Vector3.RelativeRight },
        // { "door_dside_r", Vector3.RelativeLeft },
        // { "door_pside_r", Vector3.RelativeRight },
        { "bonnet",         Vector3.RelativeRight },
        { "windscreen",     Vector3.RelativeRight },
        { "boot",           Vector3.RelativeLeft },
        { "windscreen_r",   Vector3.RelativeLeft },
        { "seat_r",         Vector3.RelativeLeft }, // (?) for motorcycle
        { "skid_l",         Vector3.Zero },         // (?) for heli
        { "skid_r",         Vector3.Zero },         // (?) for heli
    };
    private enum LeftRight : int { Left = 0, Right = 1 }

    private void AttachBoneToVehicleBone(
        Bone boneIndex,                    // Bone index (from enum Bone) of Ped to set attached
        EntityBone vehicleBone,            // Bone index of Entity (likely Vehicle) to attach to
        LeftRight triggerLeftRight,      // Left = 0, Right = 1
        Vector3? handOffset = null
    )
    {
        ////////////////////////////////////////////////////////////////////////
        // private void HandAttach(
        //     Control handLeftControl, 
        //     bool handTrigger, 
        //     Bone boneIndex, 
        //     EntityBone entityBone
        // )
        // {
        //     if (Game.IsControlJustPressed(handLeftControl) && handTrigger)
        //     {
        //         this.GrabbingVehicle.IsPersistent = true;
        //         Vector3 velocity = Game.Player.Character.Velocity;
        //         Vector3 velocity2 = this.GrabbingVehicle.Velocity;
        //         Game.Player.Character.Velocity = (velocity - velocity2).Normalized;
        //         if (!Game.Player.Character.IsRagdoll)
        //         {
        //             Main.GHelper.GiveNMMessage(Game.Player.Character, Main.GHelper.NMMessage.stopAllBehaviours, 1, 0, -1);
        //         }
        //         Main.GHelper.AttachEntityToEntityPhysically(Game.Player.Character, this.GrabbingVehicle, Game.Player.Character.Bones[boneIndex], entityBone, entityBone.Pose, new Vector3(0f, 0f, 0f), Vector3.Zero, this.breakForce, false, true, false);
        //         if (!this.grabtriger)
        //         {
        //             this.GrabbingDriverTasks();
        //             this.grabtriger = !this.grabtriger;
        //         }
        //         if (boneIndex == Bone.PHLeftHand)
        //         {
        //             this.grabLeftHand = true;
        //         }
        //         if (boneIndex == Bone.PHRightHand)
        //         {
        //             this.grabRightHand = true;
        //         }
        //         Script.Wait(200);
        //     }
        // }
        ////////////////////////////////////////////////////////////////////////

        // DEBUG ///////////////////////////////////////////////////////////////
        // if (Game.IsControlJustPressed(handLeftControl) && handTrigger)
        // END-DEBUG ///////////////////////////////////////////////////////////
        {
            // GrabbingVehicle.IsPersistent = true;
            Vehicle vehicle = (Vehicle)vehicleBone.Owner;
            Vector3 offsetVehicleBone = vehicleBone.GetRelativePositionOffset(vehicle.Position);

            player.Velocity = (player.Velocity - vehicle.Velocity).Normalized;
            if (player.IsRagdoll == false)
            {
                player.Task.ClearAllImmediately(); // GiveNMMessage(player, NMMessage.StopAllBehaviours, true, false, -1);
                player.Ragdoll(-1, RagdollType.Balance);
            }

            Vector3 handOffsetProcessed;
            if (handOffset is Vector3)
            {
                handOffsetProcessed = (Vector3)handOffset;
            }
            else
            {
                handOffsetProcessed = 0.1f * vehicleBoneSafeGrabOffset[vehicleBone.Name];
                if (triggerLeftRight == LeftRight.Left)
                {
                    handOffsetProcessed = handOffsetProcessed + 0.3f * vehicleBoneLeftGrabOffset[vehicleBone.Name];
                }
                else
                {
                    handOffsetProcessed = handOffsetProcessed - 0.3f * vehicleBoneLeftGrabOffset[vehicleBone.Name];
                }
            }

            AttachEntityBoneToEntityBonePhysically(
                player.Bones[boneIndex],
                vehicleBone,
                Vector3.Zero,
                handOffsetProcessed,
                Vector3.Zero,
                0f,
                false, // constrainRotation: was fixedRot = false 
                false, // doInitialWarp:     not declared
                true   // collideWithEntity: was collision = true
            );
            // if (!this.grabtriger)
            // {
            //     this.GrabbingDriverTasks();
            //     this.grabtriger = !this.grabtriger;
            // }
            // if (boneIndex == Bone.PHLeftHand)
            // {
            //     this.grabLeftHand = true;
            // }
            // if (boneIndex == Bone.PHRightHand)
            // {
            //     this.grabRightHand = true;
            // }
            // Script.Wait(200);
        }
    }

    public static void AttachEntityBoneToEntityBonePhysically(      // original: AttachEntityToEntityPhysically
        EntityBone boneOfFirstEntity,                               // original: Entity entity1, int boneIndex1
        EntityBone boneOfSecondEntity,                              // original: Entity entity2, int boneIndex2
        Vector3 firstEntityOffset,                                  // original: Vector3 Pos1
        Vector3 secondEntityOffset,                                 // original: Vector3 Pos2
        Vector3 rotation,                                           // original: Vector3 Rot
        float physicalStrength = 0f,                                // original: float breakForce = 0f
        bool constrainRotation = true,                              // original: bool  fixedRot   = true
        bool doInitialWarp = false,                                 // original: (n.a)            = false
        bool collideWithEntity = true,                              // original: bool  collision  = true
        bool addInitialSeparation = false,                          // original: (n.a)            = false
        EulerRotationOrder rotationOrder = EulerRotationOrder.YXZ   // original: (n.a)            = 2
    )
    {
        ////////////////////////////////////////////////////////////////////////
        // public static void AttachEntityToEntityPhysically(Entity entity1, Entity entity2, int boneIndex1, int boneIndex2, Vector3 Pos1, Vector3 Pos2, Vector3 Rot, float breakForce = 0f, bool fixedRot = true, bool collision = true, bool teleport = false)
        // {
        //     Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY_PHYSICALLY, new InputArgument[]
        //     {
        //         entity1, entity2, boneIndex1, boneIndex2,
        //         Pos1.X, Pos1.Y, Pos1.Z,
        //         Pos2.X, Pos2.Y, Pos2.Z,
        //         Rot.X, Rot.Y, Rot.Z,
        //         breakForce, fixedRot, false, collision, 0, 2
        //     });
        // }
        ////////////////////////////////////////////////////////////////////////
        // There's actually no equivalent to this native in `GTA.Entity`. There
        //     are four `GTA.Entity.AttachTo()` methods that also use
        //     `Hash.ATTACH_ENTITY_TO_ENTITY_PHYSICALLY`, with boneIndex1=-1.
        // + Worth taking a look: `GTA.EntityBone.AttachToBone()` but it has a
        //     different function call.
        //      --> Can be use to simplify script later
        // + Variable names follow SVHDN 3.7.0-nightly.19
        Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY_PHYSICALLY, new InputArgument[]
        {
            boneOfFirstEntity.Owner,
            boneOfSecondEntity.Owner,
            boneOfFirstEntity.Index,
            boneOfSecondEntity.Index,
            secondEntityOffset.X, secondEntityOffset.Y, secondEntityOffset.Z,
            firstEntityOffset.X, firstEntityOffset.Y, firstEntityOffset.Z,
            rotation.X, rotation.Y, rotation.Z,
            physicalStrength,
            constrainRotation,
            doInitialWarp,
            collideWithEntity,
            addInitialSeparation,
            rotationOrder
        });
    }


    // HITCHHIKING.NATURALMOTION ///////////////////////////////////////////////
    public enum NMMessage : int
    {
        StopAllBehaviours = 0,
        ArmsWindmill = 372,
        BodyBalance = 466,
        BodyFetal = 507,
        BodyWrithe = 526,
        BraceForImpact = 548,
        CatchFall = 576,
        HighFall = 715,
        PedalLegs = 816,
        RollDownStairs = 941,
        StaggerFall = 1151,
        Teeter = 1221,
        Yanked = 1249,
        Dragged = 597,
        Shot = 983,
        Stumble = 1195
    }
    public static void GiveNMMessage(
        Ped ped,
        NMMessage nmMessage,
        bool toggleRagdoll = true,   // Original: int ragdoll_ON = 1
        bool toggleNMMessage = true, // Original: int nmMessage_ON = 1
        int duration = -1
    )
    {
        ////////////////////////////////////////////////////////////////////////
        // public static void GiveNMMessage(
        //     Ped ped,
        //     Main.GHelper.NMMessage nmMessage,
        //     int ragdoll_ON = 1,
        //     int nmMessage_ON = 1,
        //     int duration = -1
        // )
        // {
        //     int num = 0;
        //     if (num < ragdoll_ON)
        //     {
        //         Function.Call(-5865380420870110134L, ped, duration, duration, 1, 1, 1, 0);
        //     }
        //     if (num < nmMessage_ON)
        //     {
        //         Function.Call(4723979835631036037L, true, (int)nmMessage);
        //         Function.Call(-5667534060467102629L, ped);
        //     }
        // }
        ////////////////////////////////////////////////////////////////////////
        if (toggleRagdoll == true)
        {
            // Originally: Function.Call(Hash.SET_PED_TO_RAGDOLL, ped, duration, duration, 1, 1, 1, 0);
            // Fixed:      Function.Call(Hash.SET_PED_TO_RAGDOLL, ped, duration, duration, 1, 0, 0, 0);
            //                 (These parameters have unknown effects and can be ignored) --> ^  ^
            // But shouldn't affect: https://nativedb.dotindustries.dev/gta5/natives/0xAE99FB955581844A
            ped.SetToRagdoll(duration, duration, RagdollType.ScriptControl, false);
        }
        if (toggleNMMessage == true)
        {
            CreateNMMessage(true, (int)nmMessage);
            GivePedNMMessage(ped);
        }
    }
    private static void CreateNMMessage(bool startImmediately, int messageId)
    {
        // Create a Natural Motion message, likely save in a buffer for immediate use
        Function.Call(Hash.CREATE_NM_MESSAGE, startImmediately, messageId);
    }
    private static void GivePedNMMessage(Ped ped)
    {
        // Give Natural Motion message to ped
        Function.Call(Hash.GIVE_PED_NM_MESSAGE, ped);
    }


    // HITCHHIKING.DEPRECATED //////////////////////////////////////////////////
    private void GrabbingDriverTasks(Vehicle vehicle)
    {
        ////////////////////////////////////////////////////////////////////////
        // private void GrabbingDriverTasks()
        // {
        //     Ped ped = Function.Call<Ped>(Hash.GET_PED_IN_VEHICLE_SEAT, this.GrabbingVehicle, -1);
        //     if (ped != null && ped.Exists() && ped != Game.Player.Character && this.GrabbingVehicle.Exists() && ped.IsInVehicle(this.GrabbingVehicle))
        //     {
        //         this.PedDriver = ped;
        //         ped.BlockPermanentEvents = true;
        //         Function.Call(Hash.SET_IGNORE_LOW_PRIORITY_SHOCKING_EVENTS, ped, true);
        //         Function.Call(Hash.SET_PED_CONFIG_FLAG, ped, PedConfigFlagToggles.DisablePanicInVehicle, true);
        //         Function.Call(Hash.TASK_SHOCKING_EVENT_REACT, ped, false);
        //         Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, ped, 0, 1);
        //         Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped, CombatAttributes.AlwaysFlee, 1);
        //         ped.CanFlyThroughWindscreen = true;
        //         ped.Task.CruiseWithVehicle(this.GrabbingVehicle, 80f, 786492);
        //     }
        // }
        ////////////////////////////////////////////////////////////////////////
        Ped ped = vehicle.GetPedOnSeat(VehicleSeat.Driver);
        if (ped != null && ped.Exists() && ped != Game.Player.Character && vehicle.Exists() && ped.IsInVehicle(vehicle))
        {
            ped.BlockPermanentEvents = true;
            Function.Call(Hash.SET_IGNORE_LOW_PRIORITY_SHOCKING_EVENTS, ped, true);
            ped.SetConfigFlag(PedConfigFlagToggles.DisablePanicInVehicle, true);
            InvokeTaskShockingEventReact(ped, false);
            ped.SetFleeAttributes((FleeAttributes)0, true); // Don't flee
            ped.SetCombatAttribute(CombatAttributes.AlwaysFlee, true);
            ped.CanFlyThroughWindscreen = true;
            ped.Task.CruiseWithVehicle(
                vehicle,
                80f, // todo: make faster, by settings.PARAMETERS
                (
                    VehicleDrivingFlags.SwerveAroundAllVehicles
                    | VehicleDrivingFlags.SteerAroundStationaryVehicles
                    | VehicleDrivingFlags.SteerAroundPeds
                    | VehicleDrivingFlags.SteerAroundObjects
                    | VehicleDrivingFlags.UseShortCutLinks
                    | VehicleDrivingFlags.ChangeLanesAroundObstructions
                )
            );
        }
    }
    private static void InvokeTaskShockingEventReact(Ped ped, bool p1)
    {
        // TASK_SHOCKING_EVENT_REACT: https://alloc8or.re/gta5/nativedb/?n=0x452419CBD838065B
        Function.Call(Hash.TASK_SHOCKING_EVENT_REACT, ped, p1);
    }
}