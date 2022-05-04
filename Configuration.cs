using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Dalamud.Configuration;
using Dalamud.Logging;
using HaselTweaks.Tweaks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HaselTweaks;

[Serializable]
internal class Configuration : IPluginConfiguration
{
    [JsonIgnore]
    public Plugin? Plugin { get; set; } = null!;

    public int Version { get; set; } = 1;
    public HashSet<string> EnabledTweaks { get; private set; } = new();
    public TweakConfigs Tweaks { get; init; } = new();

    internal static Configuration Load(Plugin plugin)
    {
        var configPath = plugin.PluginInterface.ConfigFile.FullName;

        string? jsonData = File.Exists(configPath) ? File.ReadAllText(configPath) : null;
        if (string.IsNullOrEmpty(jsonData))
            return new Configuration();

        var config = JObject.Parse(jsonData);
        if (config == null)
            return new Configuration();

        var version = (int?)config[nameof(Version)];
        var enabledTweaks = (JArray?)config[nameof(EnabledTweaks)];
        var tweakConfigs = (JObject?)config[nameof(Tweaks)];

        if (version == null || enabledTweaks == null || tweakConfigs == null)
            return new Configuration();

        var tweakNames = plugin.Tweaks.Select(t => t.InternalName).ToList();

        var renamedTweaks = new Dictionary<string, string>()
        {
            ["KeepInstantProfile"] = "KeepInstantPortrait", // commit 03553bef
            ["RevealDungeonRequirements"] = "RevealDutyRequirements", // commit 7ce9b37b
        };

        var newEnabledTweaks = new JArray();

        foreach (var tweakToken in enabledTweaks)
        {
            var tweakName = (string?)tweakToken;
            if (string.IsNullOrEmpty(tweakName)) continue;

            // re-enable renamed tweaks
            if (renamedTweaks.ContainsKey(tweakName))
            {
                var newTweakName = renamedTweaks[tweakName];

                PluginLog.Log($"Renamed Tweak: {tweakName} => {newTweakName}");
                newEnabledTweaks.Add(newTweakName);

                // copy renamed tweak config
                var tweakConfig = (JObject?)tweakConfigs[tweakName];
                if (tweakConfig != null)
                {
                    // adjust $type
                    var type = (string?)tweakConfig["type"];
                    if (type != null)
                        tweakConfig["type"] = type.Replace(tweakName, newTweakName);

                    tweakConfigs[newTweakName] = tweakConfig;
                    tweakConfigs.Remove(tweakName);
                }
            }

            // only copy valid ones
            if (tweakNames.Contains(tweakName))
                newEnabledTweaks.Add(tweakName);
        }

        config[nameof(EnabledTweaks)] = newEnabledTweaks;

        var configuration = config.ToObject<Configuration>();
        if (configuration == null)
            return new Configuration();

        configuration.Plugin = plugin;

        return configuration;
    }

    internal void Save()
    {
        Plugin?.PluginInterface.SavePluginConfig(this);
    }
}

public class TweakConfigs
{
    public AutoSortArmouryChest.Configuration AutoSortArmouryChest { get; init; } = new();
    public CustomChatTimestamp.Configuration CustomChatTimestamp { get; init; } = new();
    public MinimapAdjustments.Configuration MinimapAdjustments { get; init; } = new();
    public ForcedCutsceneMusic.Configuration ForcedCutsceneMusic { get; init; } = new();
    public ScrollableTabs.Configuration ScrollableTabs { get; init; } = new();
}
