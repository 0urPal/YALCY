namespace YALCY;

// Single source of truth for default DMX channel assignments.
// Fixtures follow a 7-channel layout (dimmer, red, blue, yellow, green, fog, strobe).
// Fixtures 1-8 use 1-56 and the state channels use 57-76, so fixtures 9-16 resume the
// 7-channel sequence at 78 to stay clear of them.
internal static class DmxDefaults
{
    public const int BpmChannel = 57;
    public const int CueChangeChannel = 58;
    public const int BeatLineChannel = 59;
    public const int BonusEffectChannel = 60;
    public const int KeyFrameChannel = 61;
    public const int DrumNoteChannel = 62;
    public const int PostProcessingChannel = 63;
    public const int GuitarNoteChannel = 64;
    public const int BassNoteChannel = 65;
    public const int CurrentSpotlightChannel = 66;
    public const int CurrentSingalongChannel = 67;
    public const int KeysNoteChannel = 68;
    public const int VocalsNoteChannel = 69;
    public const int Harmony0NoteChannel = 70;
    public const int Harmony1NoteChannel = 71;
    public const int Harmony2NoteChannel = 72;
    public const int CurrentSceneChannel = 73;
    public const int VenueSizeChannel = 74;
    public const int PauseStateChannel = 75;
    public const int SongSectionChannel = 76;
    public const int BroadcastUniverse = 1;

    // Slot counts the DMX tab shows; shorter saved arrays are padded to these on load
    public const int MasterDimmerSlots = 16;
    public const int GroupSlots = 8;

    // Arrays are returned as fresh copies because the UI edits them in place.
    public static int[] MasterDimmerChannels() => new[] { 1, 8, 15, 22, 29, 36, 43, 50, 78, 85, 92, 99, 106, 113, 120, 127 };
    public static int[] MasterDimmerValues() => new[] { 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 };
    public static int[] RedChannels() => new[] { 2, 9, 16, 23, 30, 37, 44, 51 };
    public static int[] BlueChannels() => new[] { 3, 10, 17, 24, 31, 38, 45, 52 };
    public static int[] YellowChannels() => new[] { 4, 11, 18, 25, 32, 39, 46, 53 };
    public static int[] GreenChannels() => new[] { 5, 12, 19, 26, 33, 40, 47, 54 };
    public static int[] FogChannels() => new[] { 6, 13, 20, 27, 34, 41, 48, 55 };
    public static int[] StrobeChannels() => new[] { 7, 14, 21, 28, 35, 42, 49, 56 };
}
