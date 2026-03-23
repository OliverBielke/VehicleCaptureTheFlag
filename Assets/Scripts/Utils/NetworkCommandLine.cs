using System;
using System.Collections.Generic;
using System.Linq;
using PacMan.Game;
using PacMan.Network;
using Scripts.Map;
using UnityEngine;

public static class NetworkCommandLine
{
    public static string RecordingBaseName { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void BeforeSceneLoad()
    {
        if (Application.isEditor) return;

        RecordingBaseName = null;
        var args = GetCommandlineArgs();

        if (args.TryGetValue("-port", out string port))
        {
            var parsedPort = Int32.Parse(port);
            CommunicatorHttpServer.channel = parsedPort;
            CommunicatorHttpClient.channel = parsedPort;
        }

        if (args.TryGetValue("-mode", out string mode))
        {
            switch (mode.ToLower())
            {
                case "server":
                    PacManManagerModeSelector.mode_override = ManagerMode.Server;
                    break;
                case "local":
                    PacManManagerModeSelector.mode_override = ManagerMode.Local;
                    break;
                case "client":
                    PacManManagerModeSelector.mode_override = ManagerMode.Client;
                    break;
            }
        }


        if (args.TryGetValue("-map", out string map))
        {
            PacManManagerModeSelector.map_override = map?.Trim();
        }

        if (args.TryGetValue("-recording", out string recordingBaseName) && !string.IsNullOrWhiteSpace(recordingBaseName))
        {
            RecordingBaseName = recordingBaseName;
        }
    }

    private static Dictionary<string, string> GetCommandlineArgs()
    {
        Dictionary<string, string> argDictionary = new Dictionary<string, string>();

        var args = System.Environment.GetCommandLineArgs();

        for (int i = 0; i < args.Length; ++i)
        {
            var arg = args[i].ToLower();
            if (arg.StartsWith("-"))
            {
                var value = i < args.Length - 1 ? args[i + 1] : null;
                value = (value?.StartsWith("-") ?? false) ? null : value;

                argDictionary.Add(arg, value);
            }
        }

        return argDictionary;
    }
}
