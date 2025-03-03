using GTA.Math;
using GTA.UI;
using GTA;
using System.Drawing;


public static class DevUtils
{
    public static void EnsureSettingsFile(
        string path,
        Dictionary<string, Dictionary<string, object>> defaultSettingsDict,
        int verbose = 1
    )
    {
        if (File.Exists(path) == false)
        {
            try
            {
                var settings = ScriptSettings.Load(path);
                foreach (string section in defaultSettingsDict.Keys)
                {
                    foreach (KeyValuePair<string, object> ds in defaultSettingsDict[section])
                    {
                        settings.SetValue(section, ds.Key, ds.Value);
                    }
                }
                settings.Save();
                if (verbose >= Verbosity.INFO){ Notification.PostTicker("Config file created with default values.", true);}
            }
            catch (Exception ex)
            {
                if (verbose >= Verbosity.ERROR){ Notification.PostTicker("Error creating config: " + ex.Message, true);}
            }
        }
    }

    public static ScriptSettings LoadSettings(
        string path,
        Dictionary<string, Dictionary<string, object>> defaultSettingsDict,
        int verbose = 1
    )
    {
        var settings = ScriptSettings.Load(path);
        // Remove unrecognized settings
        foreach (string sc in settings.GetAllSectionNames())
        {
            if (defaultSettingsDict.ContainsKey(sc) == false)
            {
                settings.RemoveSection(sc);
                if (verbose >= Verbosity.WARNING) { Notification.PostTicker($"Unrecognized section {sc} removed.", true);}
            }
            else
            {
                foreach (string st in settings.GetAllKeyNames(sc))
                {
                    if (defaultSettingsDict[sc].ContainsKey(st) == false)
                    {
                        settings.RemoveKey(sc, st);
                        if (verbose >= Verbosity.WARNING) { Notification.PostTicker($"Unrecognized key {sc}.{st} removed.", true);}
                    }
                }
            }
        }
        // Add missing settings
        foreach (string sc in defaultSettingsDict.Keys)
        {
            foreach (KeyValuePair<string, object> ds in defaultSettingsDict[sc])
            {
                var st = ds.Key;
                if (settings.ContainsKey(sc, st) == false)
                {
                    settings.SetValue(sc, st, ds.Value);
                    if (verbose >= Verbosity.WARNING) { Notification.PostTicker($"Missing key {sc}.{st}={ds.Value} added.", true);}
                }
            }
        }
        if (verbose >= Verbosity.INFO) { Notification.PostTicker($"Settings successfully loaded.", true);}
        return settings;
    }

    public static void PrintSettings(ScriptSettings settings)
    {
        foreach (string sc in settings.GetAllSectionNames())
        {
            foreach (string st in settings.GetAllKeyNames(sc))
            {
                Notification.PostTicker($"{sc}.{st}={settings.GetValue(sc, st, "NotFound")}", true);
            }
        }
    }

    public static void PrintPosition()
    {
        var position = Game.Player.Character.Position;
        var heading = Game.Player.Character.Heading;

        Notification.PostTicker($"Position: X: {position.X}, Y: {position.Y}, Z: {position.Z}, H: {heading}", true);
    }

    public static void DrawCylinder(Vector3 location, Vector3 scale, Color color)
    {
        World.DrawMarker(
            MarkerType.Cylinder, // Cylinder marker type
            location,            // Position in world space
            Vector3.Zero,        // Direction (not needed)
            Vector3.Zero,        // Rotation (not needed)
            scale,               // Scale (X and Y control the radius, Z controls the height)
            color,               // Color (ARGB format)
            true,                // BobUpAndDown (whether the marker moves up and down)
            false,               // FaceCamera
            false,               // RotateY (not needed for cylinders)
            null,                // TextureDictionary
            null                 // TextureName
        );
    }
}