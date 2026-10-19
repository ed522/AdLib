using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using AdLib.Config;
using AdLib.Identities;
using AdLib.IO.Files;
using AdLib.IO.Messages;
using AdLib.Model;
using AdLib.View.Page;
using AdLib.ViewModel.Core;

using Avalonia.Controls;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using NLog;

namespace AdLib.ViewModel.Page;

public partial class ClientScreenViewModel : PageViewModel
{
    private static Logger Logger { get; }

    static ClientScreenViewModel() => Logger = LogManager.GetLogger("ClientScreenViewModel");

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    public override Type ViewType => typeof(ClientScreen);

    public override string Title => "Transfer files";

    private readonly FileTransferClient _transferClient;

    [ObservableProperty] private FileTreeNode _localFiles;
    [ObservableProperty] private FileTreeNode _remoteFiles;
    public ObservableCollection<FileTreeNode> LocalSelected { get; } = [];
    public ObservableCollection<FileTreeNode> RemoteSelected { get; } = [];

    [ObservableProperty] private SelectionMode _localSelectionMode = SelectionMode.Single;
    [ObservableProperty] private SelectionMode _remoteSelectionMode = SelectionMode.Single;

    [ObservableProperty] private bool _canPut;
    [ObservableProperty] private bool _canGet;

    private bool _shouldShowLocalHidden = true;
    private bool _shouldShowRemoteHidden = true;

    public bool ShouldShowLocalHidden
    {
        get => this._shouldShowLocalHidden;
        set
        {
            foreach (FileTreeNode node in this.LocalFiles.Children) node.ShouldBeShown = !node.IsHidden || value;
            this.SetProperty(ref this._shouldShowLocalHidden, value);
        }
    }

    public bool ShouldShowRemoteHidden
    {
        get => this._shouldShowRemoteHidden;
        set
        {
            foreach (FileTreeNode node in this.RemoteFiles.Children) node.ShouldBeShown = !node.IsHidden || value;
            this.SetProperty(ref this._shouldShowRemoteHidden, value);
        }
    }

    private bool _isWorking;

    public override bool IsWorking
    {
        get => this._isWorking;
        protected set => this.SetProperty(ref this._isWorking, value);
    }

    private async IAsyncEnumerable<FileTreeNode> LoadLocalFiles(FileTreeNode baseNode)
    {
        string basePath = baseNode.FullPath;

        if (!baseNode.IsDirectory || !Directory.Exists(basePath))
        {
            yield break;
        }

        await foreach (string dir in AsyncFiles.OrderedEnumerateDirectoriesAsync(basePath))
        {
            DirectoryInfo inf = new(dir);

            yield return new FileTreeNode(
                dir,
                Path.GetFileName(dir.TrimEnd(Path.PathSeparator)),
                true,
                (inf.Attributes & FileAttributes.Hidden) != 0 || inf.Name.StartsWith('.'),
                this.LoadLocalFiles
            );
        }

        await foreach (string file in AsyncFiles.OrderedEnumerateFilesAsync(basePath))
        {
            yield return new FileTreeNode(
                file,
                Path.GetFileName(file),
                false,
                File.GetAttributes(file).HasFlag(FileAttributes.Hidden) || Path.GetFileName(file).StartsWith('.'),
                this.LoadLocalFiles
            );
        }
    }

    [RelayCommand]
    public async Task PutFile()
    {
        if (this.RemoteSelected.Count == 1)
        {
            foreach (FileTreeNode node in this.LocalSelected)
            {
                Console.WriteLine($"Putting {node.FullPath} to {this.RemoteSelected.First()}");
            }
        }
        else if (this.LocalSelected.Count == 1)
        {
            foreach (FileTreeNode node in this.RemoteSelected)
            {
                Console.WriteLine($"Putting {this.LocalSelected.First()} to {node.FullPath}");
            }
        }
    }


    [RelayCommand]
    public async Task GetFile()
    {
        if (this.RemoteSelected.Count == 1)
        {
            foreach (FileTreeNode node in this.LocalSelected)
            {
                Console.WriteLine($"Getting {node.FullPath} from {this.RemoteSelected.First()}");
            }
        }
        else if (this.LocalSelected.Count == 1)
        {
            foreach (FileTreeNode node in this.RemoteSelected)
            {
                Console.WriteLine($"Getting {this.LocalSelected.First()} from {node.FullPath}");
            }
        }
    }

    public async IAsyncEnumerable<FileTreeNode> LoadRemoteFiles(FileTreeNode baseNode)
    {
        if (!this._transferClient.IsConnected) yield break;

        this._transferClient.AddRequest(new ClientRequest
        {
            Path = baseNode.FullPath,
            Type = ClientRequestType.ListFiles,
        });

        TaskCompletionSource<FileEntry[]> filesTask = new();

        this._transferClient.FileListingReceived += (_, args) =>
        {
            // does not consume event - if it's a different path, ignore
            if (args.Path == baseNode.FullPath)
            {
                filesTask.SetResult(args.Files);
            }
        };

        foreach (FileEntry entry in await filesTask.Task)
        {
            yield return new FileTreeNode(
                entry.Name,
                GetFileName(entry.Name, this._transferClient.ServerPathSeparator),
                entry.IsDirectory,
                entry.Name.StartsWith('.'),
                this.LoadRemoteFiles
            );
        }
    }

    public ClientScreenViewModel(Identity identity, char[] storePassword, string host)
    {
        TrustStore localStore = new();
        localStore.Load(ConfigDirectories.ClientLocallyTrustedIdentitiesPath, storePassword);
        TrustStore globalStore = new();
        globalStore.Load(ConfigDirectories.ClientGloballyTrustedIdentitiesPath, storePassword);

        string homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        this.LocalFiles = new FileTreeNode(
            homePath,
            Path.GetFileName(homePath.TrimEnd(Path.PathSeparator)),
            true,
            false,
            this.LoadLocalFiles
        );

        this._transferClient = new FileTransferClient(host, identity, globalStore.Combine(localStore));
        // TODO wait for client init to finish so we can actually get the server folder/path sep
        string serverFolder = this._transferClient.ServerFolder ?? "";

        this.RemoteFiles = new FileTreeNode(
            serverFolder,
            serverFolder.Split(this._transferClient.ServerPathSeparator).Last(s => !string.IsNullOrEmpty(s)),
            true,
            false,
            this.LoadRemoteFiles
        );

        this.LocalSelected.CollectionChanged += (_, _) => { this.UpdateSelections(); };

        this.RemoteSelected.CollectionChanged += (_, _) => { this.UpdateSelections(); };
    }

    private void UpdateSelections()
    {
        if (this.RemoteSelected.FirstOrDefault()?.IsDirectory ?? false)
        {
            // many files to one folder
            this.CanPut = this.RemoteSelected.Count == 1 && this.LocalSelected.Count > 0;
        }
        else
        {
            // overwrite one file
            this.CanPut = this.RemoteSelected.Count == 1 && this.LocalSelected.Count == 1;
        }

        if (this.LocalSelected.FirstOrDefault()?.IsDirectory ?? false)
        {
            this.CanGet = this.LocalSelected.Count == 1 && this.RemoteSelected.Count > 0;
        }
        else
        {
            this.CanGet = this.LocalSelected.Count == 1 && this.RemoteSelected.Count == 1;
        }

        this.RemoteSelectionMode = this.LocalSelected.Count < 2 ? SelectionMode.Multiple : SelectionMode.Single;
        this.LocalSelectionMode = this.RemoteSelected.Count < 2 ? SelectionMode.Multiple : SelectionMode.Single;
    }

    private async IAsyncEnumerable<FileTreeNode> TestLoadRemoteFiles(FileTreeNode node)
    {
        switch (node.FullPath)
        {
            case "/home/user":
                yield return MakeFile(".bashrc");
                yield return MakeFile(".profile");
                yield return MakeFile("passwords.csv.gpg");

                yield return MakeDir(".config");
                yield return MakeDir(".ssh");
                yield return MakeDir("Desktop");
                yield return MakeDir("Documents");
                yield return MakeDir("Downloads");
                yield return MakeDir("Music");
                yield return MakeDir("Pictures");
                yield return MakeDir("Videos");
                break;

            case "/home/user/.ssh":
                yield return MakeFile("authorized_keys");
                yield return MakeFile("id_rsa");
                yield return MakeFile("id_rsa.pub");
                yield return MakeFile("known_hosts");
                break;

            case "/home/user/.config":
                yield return MakeFile("mimeapps.list");
                yield return MakeFile("user-dirs.dirs");

                yield return MakeDir("autostart");
                yield return MakeDir("gtk-3.0");
                break;

            case "/home/user/.config/gtk-3.0":
                yield return MakeFile("bookmarks");
                yield return MakeFile("colors.css");
                yield return MakeFile("settings.ini");
                break;

            case "/home/user/.config/autostart":
                yield return MakeFile("dropbox.desktop");
                yield return MakeFile("nextcloud.desktop");
                yield return MakeFile("telegram.desktop");
                break;

            case "/home/user/Desktop":
                yield return MakeFile("budget-shortcut.lnk");
                yield return MakeFile("README.txt");
                yield return MakeFile("Resume (draft).pdf");
                yield return MakeFile("todo.md");
                break;

            case "/home/user/Documents":
                yield return MakeFile("cover-letter.docx");
                yield return MakeFile("notes.md");
                yield return MakeFile("resume.pdf");

                yield return MakeDir("Archive");
                yield return MakeDir("Personal");
                yield return MakeDir("Work");
                break;

            case "/home/user/Documents/Work":
                yield return MakeFile("deadlines.ods");
                yield return MakeFile("project-proposal.pptx");
                yield return MakeFile("sprint-retro.md");
                yield return MakeFile("team-contacts.csv");
                break;

            case "/home/user/Documents/Personal":
                yield return MakeFile("budget.xlsx");
                yield return MakeFile("journal-2024.txt");
                yield return MakeFile("lease-agreement.pdf");
                yield return MakeFile("recipes.docx");

                yield return MakeDir("Medical Records");
                break;

            case "/home/user/Documents/Personal/Medical Records":
                yield return MakeFile("blood-test-2024-03.pdf");
                yield return MakeFile("prescription-ibuprofen.pdf");
                yield return MakeFile("vaccination-card-scan.png");
                break;

            case "/home/user/Documents/Archive":
                yield return MakeFile("old-cv-2019.docx");
                yield return MakeFile("Photos 2018.zip");
                yield return MakeFile("tax-returns-2020.pdf");
                yield return MakeFile("thesis-final.pdf");
                break;

            case "/home/user/Downloads":
                yield return MakeFile("archive.7z");
                yield return MakeFile("Clean Code.epub");
                yield return MakeFile("invoice.pdf");
                yield return MakeFile("firefox-installer.tar.gz");
                yield return MakeFile("ubuntu-24.04-desktop-amd64.iso");
                break;

            case "/home/user/Music":
                yield return MakeFile("demo.mid");
                yield return MakeFile("liked-songs.xspf");
                yield return MakeFile("playlist.m3u");
                yield return MakeFile("Voice Recording.wav");
                break;

            case "/home/user/Pictures":
                yield return MakeFile("dog.jpeg");
                yield return MakeFile("profile-photo.png");
                yield return MakeFile("wallpaper.jpg");

                yield return MakeDir("Scanned");
                yield return MakeDir("Screenshots");
                yield return MakeDir("Holidays");
                break;

            case "/home/user/Pictures/Scanned":
                yield return MakeFile("Birth certificate.pdf");
                yield return MakeFile("old-family-photo-1987.tiff");
                yield return MakeFile("Passport.pdf");
                break;

            case "/home/user/Pictures/Screenshots":
                yield return MakeFile("bug report.png");
                yield return MakeFile("screenshot-2025-01-08-143012.png");
                yield return MakeFile("screenshot-2025-02-14-091547.png");
                yield return MakeFile("screenshot-2025-03-30-174201.png");
                break;

            case "/home/user/Videos":
                yield return MakeFile("Birthday2024.mov");
                yield return MakeFile("screen-recording-demo.mp4");
                yield return MakeFile("Tutorial (backup).mkv");
                break;
        }

        yield break;

        FileTreeNode MakeFile(string name)
        {
            return new FileTreeNode(node.FullPath.TrimEnd('/') + "/" + name, name, false, name.StartsWith('.'),
                this.TestLoadRemoteFiles);
        }

        FileTreeNode MakeDir(string name)
        {
            return new FileTreeNode(node.FullPath.TrimEnd('/') + "/" + name, name, true, name.StartsWith('.'),
                this.TestLoadRemoteFiles);
        }
    }

    [RelayCommand]
    public async Task Initialize()
    {
        try
        {
            this.IsWorking = true;
            await Task.WhenAll(this.LocalFiles.LoadChildren(), this.RemoteFiles.LoadChildren());
            this.IsWorking = false;
            // fire-and-forget subchildren since it's not as disruptive
            _ = this.LocalFiles.LoadSubchildren();
            _ = this.RemoteFiles.LoadSubchildren();
        }
        catch (Exception e)
        {
            Logger.Fatal(e, "Initialization failed");
        }
    }

    private static string GetFileName(string path, char separator) =>
        path.Split(separator).Last(s => !string.IsNullOrEmpty(s));
}