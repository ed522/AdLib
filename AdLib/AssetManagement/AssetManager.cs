using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using Avalonia.Platform;

using NLog;

namespace AdLib.AssetManagement;

public static class AssetManager
{
    public static readonly Uri AssetBaseUri = new("avares://AdLib/Assets/");
    public static readonly Uri GlobalManifestUri = new(AssetBaseUri, "manifest.json");
    public static readonly Uri GlobalManifestSchemaUri = new(AssetBaseUri, "global-manifest.schema.json");
    public static readonly Uri GroupManifestSchemaUri = new(AssetBaseUri, "group-manifest.schema.json");

    private static readonly Logger Logger = LogManager.GetLogger(nameof(AssetManager));
    private static readonly Dictionary<string, AssetGroup> AssetGroups = [];
    public static AssetGroup? FileIconsAssetGroup => AssetGroups.GetValueOrDefault("file_icons");
    public static AssetGroup? InterfaceAssetGroup => AssetGroups.GetValueOrDefault("interface");

    public static event EventHandler? AssetsLoaded;

    public static async Task LoadAssets()
    {
        Logger.Info("Loading assets");

        if (!AssetLoader.Exists(GlobalManifestUri))
        {
            throw new FileNotFoundException($"Cannot find global manifest at {GlobalManifestUri}");
        }

        if (!AssetLoader.Exists(GlobalManifestSchemaUri))
        {
            throw new FileNotFoundException($"Cannot find global manifest schema at {GlobalManifestSchemaUri}");
        }

        if (!AssetLoader.Exists(GroupManifestSchemaUri))
        {
            throw new FileNotFoundException($"Cannot find global manifest schema at {GroupManifestSchemaUri}");
        }

        AssetGroups.Clear();
        // load every icon in manifest
        await using Stream manifestStream = AssetLoader.Open(GlobalManifestUri);
        JsonNode? node = await JsonNode.ParseAsync(manifestStream);

        if (node is null || node.GetValueKind() != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Failed to parse manifest.json");
        }

        JsonObject manifest = node.AsObject();
        JsonArray groups = GetArray(manifest, "groups");

        List<(string name, Task<AssetGroup> group)> loadTasks = [];

        foreach (JsonNode? group in groups)
        {
            if (group is null || group.GetValueKind() != JsonValueKind.Object)
            {
                throw new InvalidOperationException($"Invalid master manifest.json - unexpected array element type " +
                                                    $"in 'groups': expected Object, got {group?.GetValueKind()}");
            }

            JsonObject groupObj = group.AsObject();
            string name = GetString(groupObj, "name");
            string groupDir = GetString(groupObj, "dirname");

            Uri groupUri = new(AssetBaseUri, groupDir + "/");
            Uri manifestUri = new(groupUri, "manifest.json");
            Logger.Debug($"Found {name} at {groupUri} ({manifestUri})");

            loadTasks.Add((name, AssetGroup.Create(manifestUri, groupUri, name)));
        }

        foreach ((string name, Task<AssetGroup> group) in loadTasks)
        {
            AssetGroups[name] = await group;
        }

        Logger.Info("Assets successfully loaded");
        AssetsLoaded?.Invoke(null, EventArgs.Empty);
    }

    private static string GetString(JsonObject obj, string name)
    {
        if (!obj.TryGetPropertyValue(name, out JsonNode? node))
        {
            throw new InvalidOperationException($"Invalid master manifest.json - missing option '{name}'");
        }

        if (node is null || node.GetValueKind() != JsonValueKind.String)
        {
            throw new InvalidOperationException(
                $"Invalid master manifest.json - unexpected type in '{name}': expected String, got {node?.AsValue().GetValueKind()}");
        }

        return node.GetValue<string>();
    }

    private static JsonArray GetArray(JsonObject obj, string name)
    {
        if (!obj.TryGetPropertyValue(name, out JsonNode? node))
        {
            throw new InvalidOperationException($"Invalid master manifest.json - missing option '{name}'");
        }

        if (node is null || node.GetValueKind() != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                $"Invalid master manifest.json - unexpected type in '{name}': expected Array, got {node?.AsValue().GetValueKind()}");
        }

        return node.AsArray();
    }

    public static AssetGroup GetAssetGroup(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return AssetGroups[name];
    }
}