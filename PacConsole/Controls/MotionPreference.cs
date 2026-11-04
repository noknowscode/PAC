namespace PacConsole.Controls;

/// <summary>The OS's reduced-motion setting, read where decorative motion starts (the plant's camera, the status dots).</summary>
internal static class MotionPreference
{
    /// <summary>False when the OS asks for less motion.</summary>
    public static bool AnimationsEnabled()
    {
        try
        {
            return new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
        }
        catch
        {
            // Not every host implements UISettings; default to motion on.
            return true;
        }
    }
}
