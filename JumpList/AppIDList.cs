using System;
using System.Collections.Generic;
using System.IO;
using JumpList.Properties;
using Serilog;

namespace JumpList;

public class AppIdList
{
    private readonly Dictionary<string, string> _appIDs;

    public AppIdList()
    {
        Log.Debug("Loading AppIds...");
        //load included. Split on \n and let IterateLines trim \r, so this works whatever line endings the
        //resource was built with
        var lines = Resources.AppIDs.Split('\n');

        _appIDs = new Dictionary<string, string>();    
       
        IterateLines(lines);
    }

    /// <summary>
    ///     Description of an AppId (case-insensitive), or "Unknown AppId"
    /// </summary>
    public string GetDescriptionFromId(string id)
    {
        if (id != null && _appIDs.TryGetValue(id.ToLowerInvariant(), out var desc))
        {
            return desc;
        }

        return "Unknown AppId";
    }

    /// <summary>
    ///     Number of AppIds loaded
    /// </summary>
    public int Count => _appIDs.Count;

    private int IterateLines(IEnumerable<string> lines)
    {
        var added = 0;

        foreach (var line in lines)
        {
            Log.Verbose("Processing line {Line}",line);
            var segs = line.Split('|');

            if (segs.Length != 2)
            {
                continue;
            }

            var id = segs[0].Trim().ToLowerInvariant().TrimStart('"').TrimEnd('"');
            var desc = segs[1].Trim().TrimStart('"').TrimEnd('"');
            
            if (_appIDs.ContainsKey(id) == false)
            {
                _appIDs.Add(id, desc);
                added += 1;
            }
            else
            {
                //key exists, so replace description
                _appIDs[id] = desc;
            }
        }


        return added;
    }

    public int LoadAppListFromFile(string filename)
    {
        //open file, parse, add to dictionary if key isnt already there, replace if it is there

        var lines = File.ReadAllLines(filename);
        
        Log.Debug("Processing additional AppIds in {File}",filename);

        return IterateLines(lines);
    }
}