using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;
using GTA.UI;

public class Passenger : Script
{
    // CONSTRUCTOR /////////////////////////////////////////////////////////////
    public static readonly Dictionary<string, string> metadata = new Dictionary<string, string>
    {
        {"name",      "Passenger"},
        {"developer", "votrinhan88"},
        {"version",   "1.2"},
        {"iniPath",   @"scripts\Passenger.ini"}
    };
    private static readonly Dictionary<string, Dictionary<string, object>> defaultSettingsDict = new Dictionary<string, Dictionary<string, object>>
    {
        {
            "SETTINGS", new Dictionary<string, object>
            {
                {"verbose",      Verbosity.WARNING},
                {"Interval",     200},
                {"keyPassenger", "G"},
            }
        },
        {
            "PARAMETERS", new Dictionary<string, object>
            {
                {"distanceClosestVehicle",             10.0f},
                {"timeoutAutoValDetachedPassiveEnter", 10   },
                {"timeoutAutoValSeatedPassiveExit",    10   },
                {"timeoutEnterVehicle",                 5   },
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
                Notification.PostTicker($"keyPassenger set to {keyPassenger}.", true);
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
            Notification.PostTicker($"~b~{metadata["name"]} ~g~{metadata["version"]}~w~ has been loaded.", true);
        return settings;
    }


    // VARIABLES ///////////////////////////////////////////////////////////////
    private static Ped player => Game.Player.Character;
    private DateTime timeAutoValDetachedPassiveEnter = DateTime.Now - TimeSpan.FromSeconds(10);
    private DateTime timeAutoValSeatedPassiveExit = DateTime.Now - TimeSpan.FromSeconds(10);
    private DateTime timeAttemptedEnterVehicle = DateTime.Now - TimeSpan.FromSeconds(10);
    private Vehicle? targetVehicle;
    private SeatGraph seatGraph = new SeatGraph();
    private ModState modState = ModState.Detached;
    private string debugSubtitle = "";
    private enum ModState : int
    {
        // CurrentState             // --> NextStates                           // TODO All NextStates
        Unknown = -1,
        Detached = 0,
        AttemptingEnter = 1,
        // OneHanded = 10
        // TwoHanded = 11
        // OneHandedTop = 20
        // TwoHandedTop = 21
        // AttemptedOpenDoor = 22
        // BrokenDoor = 23
        // SnatchedPassenger = 24
        // WarpedIn = 30
        Seated = 31,
    }

    private void OnTick(object sender, EventArgs e)
    {
        if ((int)this.settings["SETTINGS"]["verbose"] >= Verbosity.DEBUG)
            ShowDebugInfo();

        switch (this.modState)
        {
            case ModState.Detached:
                this.modState = AutoValDetached();
                break;
            case ModState.AttemptingEnter:
                this.modState = CheckEnterSuccessful();
                break;
            case ModState.Seated:
                this.modState = AutoValSeated();
                break;
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (this.modState)
        {
            case ModState.Detached:
                if (e.KeyCode == keyPassenger)
                    this.modState = AttemptEnterClosestVehicleAsPassenger();
                break;
            case ModState.Seated:
                if (e.KeyCode == keyPassenger)
                    this.modState = InteractAsPasseger();
                break;
        }
    }

    private void ShowDebugInfo()
    {
        this.debugSubtitle = "";
        this.debugSubtitle += $"modState={this.modState}\n";
        this.debugSubtitle += (
            $"avD={(DateTime.Now - this.timeAutoValDetachedPassiveEnter).Seconds}s, " +
            $"avA={(DateTime.Now - this.timeAttemptedEnterVehicle).Seconds}s, " +
            $"avS={(DateTime.Now - this.timeAutoValSeatedPassiveExit).Seconds}s\n"
        );
        if (this.targetVehicle != null)
        {
            this.debugSubtitle += $"V={this.targetVehicle.DisplayName}, KO={player.KnockOffVehicleType}\n";
            string bones = "";
            foreach (EntityBone bone in this.targetVehicle.Bones)
            {
                if (bone.Name.Contains("seat"))
                    bones += $"{bone.Name.Replace("seat", "*")}, ";
            }
            this.debugSubtitle += $"Bones: {bones}\n";
        }
        // if (this.seatGraph.Count > 0)
        // {
        //     for (int i = 0; i < this.seatGraph.Count; i++)
        //         this.debugSubtitle += $"seat[{i}/{this.seatGraph.Count}): {this.seatGraph.graph[i]}\n";
        // }

        GTA.UI.Screen.ShowSubtitle(this.debugSubtitle, (int)this.settings["SETTINGS"]["Interval"]);
    }

    // PASSENGER ///////////////////////////////////////////////////////////////
    private ModState SoftReset()
    {
        this.targetVehicle = (Vehicle?)player.CurrentVehicle;
        this.seatGraph.Clear();
        // this.timeAutoValDetachedPassiveEnter = DateTime.Now; // Don't reset
        // this.timeAutoValSeatedPassiveExit = DateTime.Now; // Don't reset
        // this.timeAttemptedEnterVehicle = DateTime.Now; // Not necessary
        player.KnockOffVehicleType = KnockOffVehicleType.Default;
        player.Task.ClearAll();
        return ModState.Detached;
    }

    private ModState AutoValDetached()
    {
        // Check: Timeout
        if ((DateTime.Now - this.timeAutoValDetachedPassiveEnter).Seconds < (int)settings["PARAMETERS"]["timeoutAutoValDetachedPassiveEnter"])
            return this.modState;
        this.timeAutoValDetachedPassiveEnter = DateTime.Now;

        // If player is already in vehicle, shortcut straight to interact ModState.Seated
        if (player.IsInVehicle())
        {
            this.targetVehicle = player.CurrentVehicle;
            this.seatGraph.Build(this.targetVehicle);
            return ModState.Seated;
        }
        this.targetVehicle = null;
        this.seatGraph.Clear();
        return ModState.Detached;
    }

    private ModState AutoValSeated()
    {
        // Check: Player actively exited target vehicle
        if (
            Game.IsControlJustPressed(GTA.Control.VehicleExit)
            | Game.IsControlPressed(GTA.Control.VehicleExit)
        )
        {
            if ((int)this.settings["SETTINGS"]["verbose"] >= Verbosity.DEBUG)
                Notification.PostTicker("Player actively exited vehicle.", true);
            return SoftReset();
        }


        // Passive validation
        if ((DateTime.Now - this.timeAutoValSeatedPassiveExit).Seconds > (int)settings["PARAMETERS"]["timeoutAutoValSeatedPassiveExit"])
        {
            this.timeAutoValSeatedPassiveExit = DateTime.Now;
            {
                // Check: Player passively exited vehicle by Timeout
                // E.g.: By being knocked off driver, jacked out, etc.
                // Check: Target vehicle is not null
                if (this.targetVehicle == null)
                    return SoftReset();
                // Check: Player is in target vehicle
                if (!player.IsInVehicle(this.targetVehicle))
                    return SoftReset();

                // Check: Player is a bike passenger, then turn off KnockOff
                if (this.targetVehicle.IsMotorcycle && (player.SeatIndex != VehicleSeat.Driver))
                    player.KnockOffVehicleType = KnockOffVehicleType.Never;
                else
                    player.KnockOffVehicleType = KnockOffVehicleType.Default;
            }
        }
        return this.modState;
    }

    private ModState AttemptEnterClosestVehicleAsPassenger()
    {
        float distanceClosestVehicle = (float)this.settings["PARAMETERS"]["distanceClosestVehicle"];
        Vehicle candidateVehicle = World.GetClosestVehicle(player.Position, distanceClosestVehicle);
        this.timeAttemptedEnterVehicle = DateTime.Now;

        // Check: Candidate vehicle nearby
        if (candidateVehicle == null)
        {
            if ((int)this.settings["SETTINGS"]["verbose"] >= Verbosity.DEBUG)
                Notification.PostTicker("No vehicle found.", true);
            return SoftReset();
        }
        // Check: Candidate vehicle has passenger seat(s)
        if (candidateVehicle.PassengerCapacity == 0)
        {
            if ((int)this.settings["SETTINGS"]["verbose"] >= Verbosity.DEBUG)
                Notification.PostTicker("No passenger seat available.", true);
            return SoftReset();
        }


        this.targetVehicle = candidateVehicle;
        // If player is already in vehicle, shortcut straight to interact
        if (player.IsInVehicle(candidateVehicle))
        {
            this.modState = CheckEnterSuccessful();
            return InteractAsPasseger();
        }
        
        // Attempt to enter vehicle
        EnterVehicleFlags enterVehicleFlags;
        VehicleSeat bestSeat;float speed = 1f;
        if (player.IsRunning | player.IsSprinting) speed = 2f;

        VehicleSeat freeSeat = FindFirstFreePassengerSeat(this.targetVehicle);
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

        // Attempt to enter vehicle. KnockOffVehicleType will be handled in AutoValSeated
        player.Task.EnterVehicle(
            this.targetVehicle,
            bestSeat,
            -1,
            speed,
            enterVehicleFlags
        );

        if ((int)this.settings["SETTINGS"]["verbose"] >= Verbosity.INFO)
            Notification.PostTicker($"Attempt seat {bestSeat}.", true);

        return ModState.AttemptingEnter;
    }

    private ModState CheckEnterSuccessful()
    {
        // Check: Target vehicle is not null
        if (this.targetVehicle == null)
        {
            return SoftReset();
        }
        // Check: Target vehicle still exists (in game)
        if (!this.targetVehicle.Exists())
        {
            return SoftReset();
        }
        // Check: Target vehicle is still closeby (in game)
        if ((this.targetVehicle.Position - player.Position).Length() > 4 * (float)this.settings["PARAMETERS"]["distanceClosestVehicle"])
        {
            return SoftReset();
        }
        // Check: Timeout
        if ((DateTime.Now - this.timeAttemptedEnterVehicle).Seconds > (int)this.settings["PARAMETERS"]["timeoutEnterVehicle"])
        {
            if ((int)this.settings["SETTINGS"]["verbose"] >= Verbosity.DEBUG) Notification.PostTicker("Timeout entering vehicle.", true);
            return SoftReset();
        }

        // Succesfully entered vehicle
        if (player.IsInVehicle(this.targetVehicle))
        {
            if ((int)this.settings["SETTINGS"]["verbose"] >= Verbosity.DEBUG) Notification.PostTicker($"Entered vehicle.", true);
            this.seatGraph.Build(this.targetVehicle);
            return ModState.Seated;
        }
        // Keep attempting to enter vehicle
        return ModState.AttemptingEnter;
    }

    private ModState InteractAsPasseger()
    {
        // Check: Target vehicle is not null
        if (this.targetVehicle == null)
            return SoftReset();
        // Check: Player is in target vehicle
        if (!player.IsInVehicle(this.targetVehicle))
            return SoftReset();

        this.timeAutoValSeatedPassiveExit = DateTime.Now;
        // Interact as passenger
        if (!player.IsAiming == true)
            CycleFreeSeatsWhileOnVehicle();
        else
        {
            ThreatenOccupants();
            Ped driver = player.CurrentVehicle.Driver;
            if (driver != null)
            {
                if (driver.IsAlive & driver != player) {
                    MakeDriverDriveOrCruise(driver);
                    MakeDriverReckless(driver);
                }
            }
        }
        return ModState.Seated;
    }

    // PASSENGER.ACTIONS ///////////////////////////////////////////////////////
    private void CycleFreeSeatsWhileOnVehicle()
    {
        Vehicle vehicle = player.CurrentVehicle;
        if ((vehicle.GetPedOnSeat(VehicleSeat.Driver) != null) && (vehicle.PassengerCount == vehicle.PassengerCapacity))
        {
            if ((int)this.settings["SETTINGS"]["verbose"] >= Verbosity.WARNING)
                Notification.PostTicker("No free seat available.", true);
        }

        var idxCurrentSeat = (int)player.SeatIndex;
        int idxNextSeat = idxCurrentSeat;

        // Find next free seat
        for (int i = 0; i < vehicle.PassengerCapacity + 1; i++)
        {
            // Re-index as a shifted circular queue
            if (idxNextSeat + 1 <= vehicle.PassengerCapacity)
                idxNextSeat = idxNextSeat + 1;
            else
                idxNextSeat = (int)VehicleSeat.Driver;

            // Check: Next seat is free
            if (!vehicle.IsSeatFree((VehicleSeat)idxNextSeat))
                continue;

            if (vehicle.IsMotorcycle)
            {
                if (idxNextSeat != (int)VehicleSeat.Driver)
                {
                    // Staying on motorbikes Passenger seat needs temporary GodMode
                    player.KnockOffVehicleType = KnockOffVehicleType.Never;
                    player.Task.WarpIntoVehicle(vehicle, (VehicleSeat)idxNextSeat);
                }
                else
                {
                    player.KnockOffVehicleType = KnockOffVehicleType.Default;
                    player.Task.WarpIntoVehicle(vehicle, (VehicleSeat)idxNextSeat);
                }
            }
            else
            {
                player.Task.WarpIntoVehicle(vehicle, (VehicleSeat)idxNextSeat);
            }

            if ((int)this.settings["SETTINGS"]["verbose"] >= Verbosity.INFO)
                Notification.PostTicker($"Switch to {(VehicleSeat)idxNextSeat}.", true);
            return;
        }
    }

    private void ThreatenOccupants()
    {
        bool notify = false;
        foreach (Ped ped in player.CurrentVehicle.Occupants)
        {
            if (ped == player)
            {
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

    private void MakeDriverDriveOrCruise(Ped driver)
    {
        VehicleDrivingFlags drivingFlags = (
            VehicleDrivingFlags.SwerveAroundAllVehicles
            | VehicleDrivingFlags.SteerAroundStationaryVehicles
            | VehicleDrivingFlags.SteerAroundPeds
            | VehicleDrivingFlags.SteerAroundObjects
            | VehicleDrivingFlags.DontSteerAroundPlayerPed
            | VehicleDrivingFlags.GoOffRoadWhenAvoiding
            | VehicleDrivingFlags.UseShortCutLinks
            | VehicleDrivingFlags.ChangeLanesAroundObstructions
            // | VehicleDrivingFlags.UseStringPullingAtJunctions
            | VehicleDrivingFlags.StopAtDestination
        );
        driver.PlayAmbientSpeech("GENERIC_FRIGHTENED_HIGH", SpeechModifier.ShoutedCritical);

        driver.Task.ClearAll();
        if (Game.IsWaypointActive)
        {
            driver.Task.DriveTo(driver.CurrentVehicle, World.WaypointPosition, 9999f, drivingFlags, 0f);
            if ((int)this.settings["SETTINGS"]["verbose"] >= Verbosity.INFO)
                Notification.PostTicker($"Driver going to Waypoint.", true);
        }
        else
        {
            driver.Task.CruiseWithVehicle(driver.CurrentVehicle, 9999f, drivingFlags);
        }
    }

    private void MakeDriverReckless(Ped driver)
    {
        driver.VehicleDrivingFlags = ((
            VehicleDrivingFlags.SwerveAroundAllVehicles
            | VehicleDrivingFlags.SteerAroundStationaryVehicles
            | VehicleDrivingFlags.SteerAroundPeds
            | VehicleDrivingFlags.SteerAroundObjects
            | VehicleDrivingFlags.DontSteerAroundPlayerPed
            | VehicleDrivingFlags.GoOffRoadWhenAvoiding
            | VehicleDrivingFlags.UseShortCutLinks
            | VehicleDrivingFlags.ChangeLanesAroundObstructions
            // | VehicleDrivingFlags.UseStringPullingAtJunctions
            | VehicleDrivingFlags.StopAtDestination
        ));
        driver.SetConfigFlag(PedConfigFlagToggles.IsAgitated, true);
        driver.SetFleeAttributes(FleeAttributes.UseVehicle, true);
        driver.SetCombatAttribute(CombatAttributes.FleeWhilstInVehicle, true);
        driver.DrivingAggressiveness = 1.0f;
        driver.DrivingSpeed = 9999f;
        driver.MaxDrivingSpeed = 9999f;
        driver.DecisionMaker = new DecisionMaker(DecisionMakerTypeHash.Gang);
        driver.CanBeTargetted = true;
        if ((int)this.settings["SETTINGS"]["verbose"] >= Verbosity.INFO)
            Notification.PostTicker($"Driver became reckless.", true);
    }

    // PASSENGER.GETTERS ///////////////////////////////////////////////////////
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

    // PASSENGER.SEATGRAPH /////////////////////////////////////////////////////
    public static readonly Dictionary<int, string> SeatToSeatBoneName = new Dictionary<int, string> {
        // Not correct for motorbikes
        { (int)VehicleSeat.Driver,     "seat_dside_f"}, // VehicleSeat.LeftFront
        { (int)VehicleSeat.Passenger,  "seat_pside_f"}, // VehicleSeat.RightFront
        { (int)VehicleSeat.LeftRear,   "seat_dside_r"},
        { (int)VehicleSeat.RightRear,  "seat_pside_r"},
        { (int)VehicleSeat.ExtraSeat1, "seat_dside_r1"},
        { (int)VehicleSeat.ExtraSeat2, "seat_pside_r1"},
        { (int)VehicleSeat.ExtraSeat3, "seat_dside_r2"},
        { (int)VehicleSeat.ExtraSeat4, "seat_pside_r2"},
    };
}

public class SeatNode
{
    public VehicleSeat seat;
    public Vector2 offset;

    public SeatNode? seatUp;
    public SeatNode? seatDown;
    public SeatNode? seatLeft;
    public SeatNode? seatRight;


    public SeatNode(VehicleSeat seat, Vector2 offset)
    {
        this.seat = seat;
        this.offset = offset;
    }

    public override string ToString()
    {
        string[] directions = { "up", "down", "left", "right" };
        string[] directionsAbbrev = { "U", "D", "L", "R" };

        string reprString = $"[{this.seat}:{offset}][";
        for (int i = 0; i < directions.Length; i++)
        {
            string dir = directions[i];
            string dirAbbrev = directionsAbbrev[i];

            SeatNode? seatNode = this.Get(dir);
            if (seatNode == null)
                reprString += $"{dirAbbrev}:-, ";
            else
                reprString += $"{dirAbbrev}:{seatNode.seat}, ";
        }
        reprString.Remove(reprString.Length - 2);
        reprString += "]";
        return reprString;
    }

    private void ConnectSeatNode(SeatNode seatNode, string direction)
    {
        switch (direction)
        {
            case "up":
                this.seatUp = seatNode;
                break;
            case "down":
                this.seatDown = seatNode;
                break;
            case "left":
                this.seatLeft = seatNode;
                break;
            case "right":
                this.seatRight = seatNode;
                break;
        }
    }

    public SeatNode? Get(string direction)
    {
        switch (direction)
        {
            case "up":
                return this.seatUp;
            case "down":
                return this.seatDown;
            case "left":
                return this.seatLeft;
            case "right":
                return this.seatRight;
            default:
                return null;
        }
    }
}

public class SeatGraph
{
    public bool IsBuilt = false;
    public Vehicle? vehicle = null;
    public List<SeatNode> graph = new List<SeatNode>();

    public void Build(Vehicle vehicle)
    {
        this.vehicle = vehicle;
        // Build graph
        for (int i = 0; i < vehicle.PassengerCapacity + 1; i++)
        {
            int seatIndex = i - 1;

            if (Passenger.SeatToSeatBoneName.TryGetValue(seatIndex, out string seatBoneName))
            {
                Vector3 offset = vehicle.Bones[seatBoneName].GetPositionOffset(vehicle.Position);
                this.graph.Add(new SeatNode(
                    (VehicleSeat)seatIndex,
                    new Vector2((float)Math.Round(offset.X, 2), (float)Math.Round(offset.Y, 2)))
                );
            }
            else
            {
                this.graph.Add(new SeatNode((VehicleSeat)seatIndex, new Vector2(0, 0)));
            }
        }
        this.IsBuilt = true;
    }

    public void Clear()
    {
        this.vehicle = null;
        this.graph.Clear();
        this.IsBuilt = false;
    }

    public int Count => this.graph.Count;
}