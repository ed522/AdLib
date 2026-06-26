using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using Avalonia.Media.Imaging;
using Avalonia.Platform;

using NJsonSchema;
using NJsonSchema.Validation;

using NLog;

namespace AdLib.AssetManagement;

public class AssetGroup
{
    private static readonly Logger Log = LogManager.GetLogger(nameof(AssetGroup));
    private static JsonSchema? _schema;

    private readonly Dictionary<(string, string), Bitmap> _bitmapAssets = [];
    private readonly Dictionary<string, JsonNode?> _jsonAssets = [];

    private AssetGroup() { }

    public static async Task<AssetGroup> Create(Uri manifestResource, Uri folderResource, string name)
    {
        Log.Debug($"Loading asset group {name} from {manifestResource}");
        AssetGroup group = new();

        if (_schema == null)
        {
            await using Stream schemaStream = AssetLoader.Open(AssetManager.GroupManifestSchemaUri);
            _schema = await JsonSchema.FromJsonAsync(schemaStream);
        }

        // validate schema
        Stream dataStream = AssetLoader.Open(manifestResource);
        byte[] tmp = new byte[dataStream.Length];
        await dataStream.ReadExactlyAsync(tmp, 0, tmp.Length);
        string data = Encoding.UTF8.GetString(tmp);
        ICollection<ValidationError> errors = _schema.Validate(data);

        if (errors.Count != 0)
        {
            string message = string.Join(
                ", ",
                errors.Select(e => $"[{e.LineNumber}:{e.LinePosition}: {e.Kind} ({e.Path})]")
            );

            throw new JsonException($"Invalid manifest.json for asset group {name}: {message}");
        }

        // load manifest
        JsonNode? node = JsonNode.Parse(data);
        JsonObject? manifest = node?.AsObject();

        if (manifest is null)
        {
            throw new JsonException($"Invalid manifest.json for asset group {name}: manifest must not be null");
        }

        foreach (JsonNode set in EnumerateNotNull(name, "sets", manifest))
        {
            JsonObject setObj = set.AsObject();

            const string resolutionsKey = "resolutions";
            string[] files = EnumerateNotNull<string>(name, "files", setObj).ToArray();

            foreach (JsonNode formatNode in EnumerateNotNull(name, "formats", setObj))
            {
                JsonObject formatObject = formatNode.AsObject();
                string type = GetNotNull<string>(name, formatObject, "filetype");
                string fileClass = GetNotNull<string>(name, formatObject, "class");

                switch (fileClass)
                {
                    // load all files
                    case "bitmap":
                    {
                        // images are grouped by resolution
                        foreach (string resolution in EnumerateNotNull<string>(name, resolutionsKey, formatObject))
                        {
                            Uri resolutionBase = new(folderResource, resolution + "/");

                            foreach (string file in files)
                            {
                                Log.Debug($"Loading bitmap {file} ({type}, @{resolution})");
                                await using Stream stream = AssetLoader.Open(new Uri(resolutionBase, $"{file}.{type}"));

                                Bitmap bitmap = new(stream);
                                group._bitmapAssets[(file, resolution)] = bitmap;
                            }
                        }

                        break;
                    }

                    case "json":
                    {
                        if (formatObject[resolutionsKey]?.AsArray().Count != 0)
                        {
                            throw new InvalidOperationException("Resolutions are not supported for JSON assets");
                        }

                        foreach (string file in files)
                        {
                            Log.Debug($"Loading JSON {file} ({type})");
                            await using Stream stream = AssetLoader.Open(new Uri(folderResource, $"{file}.{type}"));
                            group._jsonAssets[file] = await JsonNode.ParseAsync(stream);
                        }

                        break;
                    }
                }
            }
        }

        Log.Debug($"Loaded asset group {name}");
        return group;
    }

    private static T GetNotNull<T>(string groupName, JsonObject obj, string property) where T : class =>
        GetNotNull(groupName, obj, property).GetValue<T>();

    private static JsonNode GetNotNull(string groupName, JsonObject obj, string property)
    {
        JsonNode? node = obj[property];

        if (node is null)
        {
            throw new JsonException($"Invalid manifest.json for asset group {groupName}: missing required property " +
                                    $"{property}");
        }

        return node;
    }

    private static IEnumerable<JsonNode> EnumerateNotNull(string groupName, string arrayName, JsonObject obj)
    {
        JsonArray? array = obj[arrayName]?.AsArray();

        if (array is null)
        {
            throw new JsonException(
                $"Invalid manifest.json for asset group {groupName}: array {arrayName} is unexpectedly null");
        }

        foreach ((int index, JsonNode? item) in array.Index())
        {
            if (item is null)
            {
                throw new JsonException(
                    $"Invalid manifest.json for asset group {groupName}: null item at {index} in {arrayName}");
            }

            yield return item;
        }
    }

    private static IEnumerable<T> EnumerateNotNull<T>(string groupName, string arrayName, JsonObject obj)
        where T : class
    {
        return EnumerateNotNull(groupName, arrayName, obj).Select(i => i.GetValue<T>());
    }

    public Bitmap GetBitmapAsset(string name, string resolutionId) => this._bitmapAssets[(name, resolutionId)];
    public JsonNode? GetJsonAsset(string name) => this._jsonAssets[name];
}