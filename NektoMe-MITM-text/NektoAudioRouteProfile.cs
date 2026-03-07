namespace NektoMe_MITM_text;

public static class NektoAudioRouteProfile
{
    private static AudioRouteProfile? _current;

    public static void Save(string firstMicLabel, string firstOutputLabel, string secondMicLabel, string secondOutputLabel)
    {
        _current = new AudioRouteProfile(firstMicLabel, firstOutputLabel, secondMicLabel, secondOutputLabel, DateTimeOffset.Now);
    }

    public static bool TryGet(out AudioRouteProfile profile)
    {
        if (_current is null)
        {
            profile = default;
            return false;
        }

        profile = _current.Value;
        return true;
    }

    public readonly record struct AudioRouteProfile(
        string FirstMicLabel,
        string FirstOutputLabel,
        string SecondMicLabel,
        string SecondOutputLabel,
        DateTimeOffset SavedAt
    );
}
