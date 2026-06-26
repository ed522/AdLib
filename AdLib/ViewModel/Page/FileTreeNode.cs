using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using AdLib.AssetManagement;

using Avalonia.Media.Imaging;

using CommunityToolkit.Mvvm.ComponentModel;

using Newtonsoft.Json;

using NLog;

namespace AdLib.ViewModel.Page;

public sealed partial class FileTreeNode : ObservableObject, IComparable<FileTreeNode>
{
    private const string ResolutionId = "128x128";
    private static readonly Logger Log = LogManager.GetLogger(nameof(FileTreeNode));
    private static readonly Dictionary<string, FileClass> ExtensionToClassMap = new();
    private static readonly Dictionary<FileClass, Bitmap> ClassIconMap = new();

    private static bool _hasLoaded;

    private static void LoadIcons()
    {
        JsonArray? array = AssetManager.FileIconsAssetGroup?.GetJsonAsset("meta_filetype")?.AsArray();
        if (array is null) throw new InvalidOperationException("Failed to get meta_filetype.json");

        foreach (JsonNode? node in array)
        {
            JsonObject? obj = node?.AsObject();

            string? type = obj?["type"]?.AsValue().GetValue<string>();
            string? icon = obj?["icon"]?.AsValue().GetValue<string>();
            JsonArray? extensions = obj?["extensions"]?.AsArray();

            if (type is null || icon is null || extensions is null)
            {
                throw new JsonException("Failed to parse meta_filetype.json: missing required property");
            }

            if (AssetManager.FileIconsAssetGroup is null)
            {
                throw new InvalidOperationException("Assets are not loaded or the file icons group is missing");
            }

            FileClass fileClass = FileClass.Of(type);

            ClassIconMap[fileClass] = AssetManager.FileIconsAssetGroup.GetBitmapAsset(icon, ResolutionId);
            Log.Info($"Registered icon {icon} for {type}");

            foreach (string? extension in extensions)
            {
                if (extension is null) continue;
                ExtensionToClassMap[extension] = fileClass;
            }
        }

        Log.Info($"Registered {ExtensionToClassMap.Count} file extensions");
        _hasLoaded = true;
    }

    public delegate IAsyncEnumerable<FileTreeNode> FileTreeLoader(FileTreeNode baseNode);

    public string PartName { get; }
    public string FullPath { get; }
    public bool IsDirectory { get; }
    public bool IsHidden { get; }
    public FileClass Class { get; }
    public Bitmap Icon { get; }

    public ObservableCollection<FileTreeNode> Children { get; } = [];

    [ObservableProperty] private bool _shouldBeShown = true;
    [ObservableProperty] private bool _isExpanded;

    partial void OnIsExpandedChanged(bool value)
    {
        if (value) _ = this.LoadSubchildren();
    }

    private bool _isShallowPopulated; // = false
    private bool _isFullyPopulated; // = false

    private readonly FileTreeLoader _loader;

    public FileTreeNode(string fullPath, string partName, bool isDirectory, bool isHidden, FileTreeLoader loader)
    {
        if (!_hasLoaded) LoadIcons();

        this._loader = loader;
        this.FullPath = fullPath;
        this.IsDirectory = isDirectory;
        this.IsHidden = isHidden;
        this.PartName = partName;

        if (isDirectory)
        {
            this.Class = FileClass.Folder;
            this.Icon = ClassIconMap[FileClass.Folder];
        }
        else
        {
            this.Class = ExtensionToClassMap.GetValueOrDefault(
                Path.GetExtension(fullPath).TrimStart('.'),
                FileClass.Unknown
            );

            this.Icon = ClassIconMap[this.Class];
        }
    }

    public async Task LoadChildren()
    {
        if (this._isShallowPopulated) return;

        this.Children.Clear();

        await foreach (FileTreeNode node in this._loader.Invoke(this))
        {
            this.Children.Add(node);
        }

        this._isShallowPopulated = true;
    }

    public async Task LoadSubchildren()
    {
        if (this._isFullyPopulated) return;

        if (!this._isShallowPopulated) await this.LoadChildren();
        await Task.WhenAll(this.Children.Select(c => c.LoadChildren()));
        this._isFullyPopulated = true;
    }

    public int CompareTo(FileTreeNode? other)
    {
        if (other is null) return 1;
        if (this.IsDirectory && !other.IsDirectory) return -1;

        return string.Compare(this.PartName, other.PartName, StringComparison.CurrentCultureIgnoreCase);
    }

    public override string ToString() => this.FullPath;

    /**
     * Get the hash code for this node. Does not include children, since that
     * would require a full tree expansion, which is not always possible or
     * reasonable.
     */
    public override int GetHashCode() => HashCode.Combine(this.FullPath, this.IsDirectory, this.IsHidden, this.Class);

    public override bool Equals(object? obj) => obj is FileTreeNode node && this.Equals(node);
    public bool Equals(FileTreeNode? other) => other is not null && this.FullPath == other.FullPath;
}

#pragma warning disable CA1034
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)]
public class FileClass
{
    static FileClass()
    {
        FieldInfo[] fields = typeof(FileClass).GetFields(BindingFlags.Public | BindingFlags.Static);

        foreach (FieldInfo field in fields)
        {
            FileClass fileClass = (FileClass)field.GetValue(null)!;
            Map.Add(fileClass.Value, fileClass);
        }
    }

    private static readonly Dictionary<string, FileClass> Map = new();

    public static FileClass Of(string value) => Map.GetValueOrDefault(value, Unknown);

    public string Value { get; }

    private FileClass(string value) => this.Value = value;

    public override string ToString() => this.Value;
    public override int GetHashCode() => this.Value.GetHashCode();

    public static readonly FileClass Unknown = new("unknown");
    public static readonly FileClass Folder = new("folder");
    public static readonly FileClass Binary = new("binary");

    public static readonly FileClass PlainText = new("document_plain_text");
    public static readonly FileClass Markdown = new("document_markdown");

    public static readonly FileClass ZipArchive = new("archive_zip");
    public static readonly FileClass SpecialArchive = new("archive_special");
    public static readonly FileClass ImageArchive = new("archive_image");
    public static readonly FileClass TarArchive = new("archive_tar");

    public static readonly FileClass Document = new("office_document");
    public static readonly FileClass PdfDocument = new("office_pdf");
    public static readonly FileClass Spreadsheet = new("office_spreadsheet");
    public static readonly FileClass Presentation = new("office_presentation");
    public static readonly FileClass Database = new("database");

    public static readonly FileClass LossyAudio = new("audio_lossy");
    public static readonly FileClass LosslessAudio = new("audio_lossless");
    public static readonly FileClass SequencedAudio = new("audio_sequenced");
    public static readonly FileClass RawAudio = new("audio_raw");

    public static readonly FileClass Video = new("video");

    public static readonly FileClass Model3D = new("3dmodel");

    public static readonly FileClass PhotographicImage = new("image_photographic_raster");
    public static readonly FileClass GraphicalImage = new("image_graphical_raster");
    public static readonly FileClass RawImage = new("image_raw_raster");
    public static readonly FileClass ProjectImage = new("image_project");
    public static readonly FileClass VectorImage = new("image_vector_illustration");

    public static readonly FileClass WindowsExecutable = new("executable_windows");
    public static readonly FileClass CompiledLibrary = new("compiled_library");
    public static readonly FileClass ObjectFile = new("compiled_object");
    public static readonly FileClass WindowsInstaller = new("installer_windows");
    public static readonly FileClass UnixInstaller = new("installer_unix");
    public static readonly FileClass MacInstaller = new("installer_mac");

    public static readonly FileClass Font = new("font");

    public static readonly FileClass EncryptedFile = new("cryptographic_encrypted");
    public static readonly FileClass KeysFile = new("cryptographic_keys");

    public static readonly FileClass UnixScript = new("script_unix");
    public static readonly FileClass WindowsScript = new("script_windows");
    public static readonly FileClass OtherScript = new("script_other");

    public static readonly FileClass PlainConfigFile = new("config_plain");
    public static readonly FileClass JsonFile = new("config_json");
    public static readonly FileClass MarkupConfigFile = new("config_markup");

    public static readonly FileClass HtmlFile = new("markup_html");
    public static readonly FileClass GenericXmlFile = new("markup_xml");
}