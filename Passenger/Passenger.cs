using System.Windows.Forms;
using GTA;
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
                {"distanceClosestVehicle",   10.0f},
                {"timeoutEnterVehicle",       5   },
                {"timeoutAutoValidateMod",   10   },
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
    private DateTime timeAutoValidateMod = DateTime.Now;
    private DateTime timeAttemptedEnterVehicle = DateTime.Now;
    private Vehicle? targetVehicle;
    private ModState modState = ModState.Detached;
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
            case ModState.AttemptingEnter:
                this.modState = CheckEnterSuccessful();
                break;
            case ModState.Seated:
                this.modState = AutoValidateMod();
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
        string subtitle = "";
        subtitle += $"modState: {this.modState}\n";
        if (this.targetVehicle != null)
            subtitle += $"targetVehicle: {this.targetVehicle}\n";
        subtitle += $"timeAttemptedEnterVehicle (s): {(DateTime.Now - this.timeAttemptedEnterVehicle).Seconds}\n";
        subtitle += $"timeAutoValidateMod (s): {(DateTime.Now - this.timeAutoValidateMod).Seconds}\n";
        GTA.UI.Screen.ShowSubtitle(subtitle, (int)this.settings["SETTINGS"]["Interval"]);
    }

    // PASSENGER ///////////////////////////////////////////////////////////////
    private ModState ResetMod()
    {
        this.targetVehicle = null;
        this.timeAutoValidateMod = DateTime.Now;
        // this.timeAttemptedEnterVehicle = DateTime.Now; // Not necessary
        player.Task.ClearAll();
        return ModState.Detached;
    }

    private ModState AutoValidateMod()
    {
        // Check: Player actively exited target vehicle
        if (
            Game.IsControlJustPressed(GTA.Control.VehicleExit)
            | Game.IsControlPressed(GTA.Control.VehicleExit)
        )
        {
            if ((int)this.settings["SETTINGS"]["verbose"] >= Verbosity.DEBUG)
                Notification.PostTicker("Player actively exited vehicle.", true);
            return ResetMod();
        }

        // Check: Player not in target vehicle by Timeout
        if ((DateTime.Now - this.timeAutoValidateMod).Seconds > (int)settings["PARAMETERS"]["timeoutAutoValidateMod"])
        {
            if ((int)this.settings["SETTINGS"]["verbose"] >= Verbosity.DEBUG)
                Notification.PostTicker("AutoValidateMod timer reset.", true);
            this.timeAutoValidateMod = DateTime.Now;
            {
                // Check: Target vehicle is not null
                if (this.targetVehicle == null)
                    return ResetMod();
                // Check: Player is in target vehicle
                if (!player.IsInVehicle(this.targetVehicle))
                    return ResetMod();
            }
        }
        return this.modState;
    }

    private ModState AttemptEnterClosestVehicleAsPassenger()
    {
        float distanceClosestVehicle = (float)this.settings["PARAMETERS"]["distanceClosestVehicle"];
        Ped player = Game.Player.Character;
        this.targetVehicle = World.GetClosestVehicle(player.Position, distanceClosestVehicle);
        this.timeAttemptedEnterVehicle = DateTime.Now;

        if (this.targetVehicle != null && this.targetVehicle.IsDriveable)
        {
            EnterVehicleFlags enterVehicleFlags;
            VehicleSeat bestSeat;

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

            player.Task.EnterVehicle(
                this.targetVehicle,
                bestSeat,
                -1,
                2f,
                enterVehicleFlags
            );

            if ((int)this.settings["SETTINGS"]["verbose"] >= Verbosity.INFO)
                Notification.PostTicker($"Attempt seat {bestSeat}.", true);
        }

        return ModState.AttemptingEnter;
    }

    private ModState CheckEnterSuccessful()
    {
        // Check: Target vehicle is not null
        if (this.targetVehicle == null)
        {
            return ResetMod();
        }
        // Check: Target vehicle still exists (in game)
        if (!this.targetVehicle.Exists())
        {
            return ResetMod();
        }
        // Check: Target vehicle is still closeby (in game)
        if ((this.targetVehicle.Position - player.Position).Length() > 4 * (float)this.settings["PARAMETERS"]["distanceClosestVehicle"])
        {
            return ResetMod();
        }
        // Check: Timeout
        if ((DateTime.Now - this.timeAttemptedEnterVehicle).Seconds > (int)this.settings["PARAMETERS"]["timeoutEnterVehicle"])
        {
            if ((int)this.settings["SETTINGS"]["verbose"] >= Verbosity.DEBUG) Notification.PostTicker("Timeout entering vehicle.", true);
            return ResetMod();
        }

        // Succesfully entered vehicle
        if (player.IsInVehicle(this.targetVehicle))
        {
            if ((int)this.settings["SETTINGS"]["verbose"] >= Verbosity.DEBUG) Notification.PostTicker($"Entered vehicle.", true);
            return ModState.Seated;
        }
        // Keep attempting to enter vehicle
        return ModState.AttemptingEnter;
    }

    private ModState InteractAsPasseger()
    {
        // Check: Target vehicle is not null
        if (this.targetVehicle == null)
            return ResetMod();
        // Check: Player is in target vehicle
        if (!player.IsInVehicle(this.targetVehicle))
            return ResetMod();

        this.timeAutoValidateMod = DateTime.Now;
        // Interact as passenger
        if (!player.IsAiming == true)
            CycleFreeSeatsWhileOnVehicle();
        else
        {
            ThreatenOccupants();
            Ped driver = player.CurrentVehicle.Driver;
            if (driver != null)
            {
                MakeDriverDriveOrCruise(driver);
                MakeDriverReckless(driver);
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
                    Notification.PostTicker($"Switch to {(VehicleSeat)idxNextSeat}.", true);
                return;
            }
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
}