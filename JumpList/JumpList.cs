using System.IO;
using JumpList.Automatic;
using JumpList.Custom;

namespace JumpList;

public static class JumpList
{
    static JumpList()
    {
        AppIdList = new AppIdList();
    }

    public static AppIdList AppIdList { get; internal set; }


    // AppIdList is not recreated here: that discarded AppIds added with AppIdList.LoadAppListFromFile
    public static AutomaticDestination LoadAutoJumplist(string autoName, int codepage = 1252)
    {
        var raw = File.ReadAllBytes(autoName);

        return new AutomaticDestination(raw, autoName, codepage);
    }

    public static CustomDestination LoadCustomJumplist(string customName, int codepage = 1252)
    {
        var raw = File.ReadAllBytes(customName);

        return new CustomDestination(raw, customName, codepage);
    }
}