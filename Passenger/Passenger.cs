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