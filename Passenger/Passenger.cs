using System.Windows.Forms;
using GTA;
using GTA.UI;

public class Passenger : Script
{
    // Define a static readonly dictionary
    public static readonly Dictionary<string, object> metadata = new Dictionary<string, object>
    {
        {"name",      "Drive Me By"},
        {"developer", "votrinhan88"},
        {"version",   "1.0"},
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

    private ScriptSettings settings;
    private static Keys keyPassenger;
    private static Ped player => Game.Player.Character;


    public Passenger()
    {
        DevUtils.EnsureSettingsFile(
            (string)metadata["iniPath"],
            defaultSettingsDict,
            (int)defaultSettingsDict["SETTINGS"]["verbose"]
        );
        settings = DevUtils.LoadSettings(
            (string)metadata["iniPath"],
            defaultSettingsDict,
            (int)defaultSettingsDict["SETTINGS"]["verbose"]
        );
        settings.Save();
        if (settings.GetValue("SETTINGS", "verbose", 0) >= Verbosity.INFO) { Notification.PostTicker($"~b~{metadata["name"]} ~g~{metadata["version"]}~w~ has been loaded.", true); }

        // Config keyPassenger
        string keyPassengerString = (string)settings.GetValue("SETTINGS", "keyPassenger", defaultSettingsDict["SETTINGS"]["keyPassenger"]);
        if (Enum.TryParse(keyPassengerString, out Keys _keyPassenger))
        {
            keyPassenger = _keyPassenger;
            if (settings.GetValue("SETTINGS", "verbose", 0) >= Verbosity.INFO) { Notification.PostTicker($"keyPassenger set to {keyPassenger}.", true); }
        }

        KeyDown += OnKeyDown;
        Interval = settings.GetValue("SETTINGS", "Interval", (int)defaultSettingsDict["SETTINGS"]["Interval"]);
    }


    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == keyPassenger)
        {
            if (player.IsInVehicle() == false)
            {
                EnterNearestVehicleAsPassenger();
            }
            else
            {
                if (player.IsAiming == true) {
                    ThreatenOccupants();
                    MakeDriverReckless();
                }
                else {
                    CycleSeatsWhileOnVehicle();
                }
            }
        }
    }

    private void EnterNearestVehicleAsPassenger()
    {
        var distanceClosestVehicle = settings.GetValue("PARAMETERS", "distanceClosestVehicle", (float)defaultSettingsDict["PARAMETERS"]["distanceClosestVehicle"]);
        var timeoutEnterVehicle = settings.GetValue("PARAMETERS", "timeoutEnterVehicle", (int)defaultSettingsDict["PARAMETERS"]["timeoutEnterVehicle"]);
        Ped player = Game.Player.Character;
        Vehicle nearestVehicle = World.GetClosestVehicle(player.Position, distanceClosestVehicle);

        if (nearestVehicle != null && nearestVehicle.IsDriveable)
        {
            VehicleSeat? freeSeat = FindFreePassengerSeat(nearestVehicle);
            if (freeSeat == null)
            {
                player.Task.EnterVehicle(
                    nearestVehicle,
                    VehicleSeat.Passenger,
                    timeoutEnterVehicle,
                    2f,
                    EnterVehicleFlags.None
                );
            }
            else
            {
                player.Task.EnterVehicle(
                    nearestVehicle,
                    freeSeat ?? VehicleSeat.None,
                    timeoutEnterVehicle,
                    2f,
                    EnterVehicleFlags.DontJackAnyone
                );
            }

            if (settings.GetValue("SETTINGS", "verbose", 0) >= Verbosity.INFO) { Notification.PostTicker($"Enter seat {freeSeat ?? VehicleSeat.Passenger}.", true); }
        }
    }

    private void CycleSeatsWhileOnVehicle()
    {
        Vehicle vehicle = player.CurrentVehicle;
        if ((vehicle.GetPedOnSeat(VehicleSeat.Driver) != null) && (vehicle.PassengerCount == vehicle.PassengerCapacity))
        {
            if (settings.GetValue("SETTINGS", "verbose", 0) >= Verbosity.WARNING) { Notification.PostTicker("No free seat available.", true); }
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
                if (settings.GetValue("SETTINGS", "verbose", 0) >= Verbosity.INFO) { Notification.PostTicker($"Switch to {(VehicleSeat)idxNextSeat}.", true); }
                return;
            }
        }
    }

    private void ThreatenOccupants()
    {
        bool notify = false;
        foreach (Ped ped in player.CurrentVehicle.Occupants)
        {
            if (ped != player)
            {
                ped.BlockPermanentEvents = true;
                if (notify == false)
                {
                    Notification.PostTicker($"Peds threatened not to leave vehficle.", true);
                    notify = true;
                }
            }
        }
    }

    private void MakeDriverReckless()
    {
        Ped driver = player.CurrentVehicle.GetPedOnSeat(VehicleSeat.Driver);
        if (driver != null)
        {
            if (driver.IsAlive & (driver != player))
            {
                driver.Task.ClearAll();
                driver.Task.CruiseWithVehicle(driver.CurrentVehicle, 9999f, VehicleDrivingFlags.DrivingModeAvoidVehiclesReckless);
                driver.DrivingAggressiveness = 1.0f;
                driver.DrivingSpeed = 9999f;
                driver.MaxDrivingSpeed = 9999f;
                driver.SetCombatAttribute(CombatAttributes.UseVehicleAttack, true);
                driver.SetCombatAttribute(CombatAttributes.FleeWhilstInVehicle, true);
                driver.SetFleeAttributes(FleeAttributes.UseVehicle, true);
                driver.SetFleeAttributes(FleeAttributes.CanScream, true);
                Notification.PostTicker($"Driver became reckless.", true);
            }
        }
    }

    private VehicleSeat? FindFreePassengerSeat(Vehicle vehicle)
    {
        VehicleSeat? freeSeat = null;
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