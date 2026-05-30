using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading.Tasks;

using AdLib.Config;
using AdLib.Identities;
using AdLib.IO.Files;
using AdLib.IO.Messages;
using AdLib.Model;
using AdLib.View.Page;
using AdLib.ViewModel.Core;

using CommunityToolkit.Mvvm.ComponentModel;

namespace AdLib.ViewModel.Page;

public partial class ClientScreenViewModel : PageViewModel
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    public override Type ViewType => typeof(ClientScreen);

    public override string Title => "Transfer files";

    [ObservableProperty] private FileTreeNode _localFiles;
    [ObservableProperty] private FileTreeNode _remoteFiles;

    private readonly FileTransferClient _transferClient;

    private bool _isWorking;

    public override bool IsWorking
    {
        get => this._isWorking;
        protected set => this.SetProperty(ref this._isWorking, value);
    }

    private async IAsyncEnumerable<FileTreeNode> LoadLocalFiles(FileTreeNode baseNode)
    {
        string basePath = baseNode.FullPath;

        if (!Directory.Exists(basePath) || !baseNode.IsDirectory)
        {
            yield break;
        }

        await foreach (string dir in AsyncFiles.EnumerateDirectoriesAsync(basePath))
        {
            yield return new FileTreeNode(dir, true, this.LoadLocalFiles);
        }

        await foreach (string file in AsyncFiles.EnumerateFilesAsync(basePath))
        {
            yield return new FileTreeNode(file, false, this.LoadLocalFiles);
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
            yield return new FileTreeNode(entry.Name, entry.IsDirectory, this.LoadRemoteFiles);
        }
    }

    public ClientScreenViewModel(Identity identity, char[] storePassword, string host)
    {
        TrustStore localStore = new();
        localStore.Load(ConfigDirectories.ClientLocallyTrustedIdentitiesPath, storePassword);
        TrustStore globalStore = new();
        globalStore.Load(ConfigDirectories.ClientGloballyTrustedIdentitiesPath, storePassword);

        TrustStore combined = localStore.Combine(globalStore);

        this._transferClient = new FileTransferClient();
        this._transferClient.ConnectAndListen(host, identity, combined);

        this.LocalFiles = new FileTreeNode(
            Path.GetPathRoot(Directory.GetCurrentDirectory())!,
            true,
            this.LoadLocalFiles
        );

        this.RemoteFiles = new FileTreeNode(
            this._transferClient.ServerFolder ?? "",
            true,
            this.LoadRemoteFiles
        );
    }

    public async Task Initialize() => await this.LocalFiles.LoadSubchildren();
}
