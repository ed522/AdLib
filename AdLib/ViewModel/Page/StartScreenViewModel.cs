using System;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

using AdLib.Config;
using AdLib.Identities;
using AdLib.View.Page;
using AdLib.ViewModel.Core;
using AdLib.ViewModel.Modal;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using static AdLib.Identities.IdentityStore;

namespace AdLib.ViewModel.Page;

public partial class StartScreenViewModel : PageViewModel
{
    [ObservableProperty] private IdentityLabel _clientSelectedIdentity;
    [ObservableProperty] private IdentityLabel _serverSelectedIdentity;

    // overrides IsWorking
    private bool _isWorking;

    public override bool IsWorking
    {
        get => this._isWorking;
        protected set => this.SetProperty(ref this._isWorking, value);
    }

    public StartScreenViewModel() => this.RefreshIdentities();

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    public override Type ViewType => typeof(StartScreen);

    public override string Title => "Welcome";

    public string ClientTargetIp { get; set; } = "";
    public string ServerSharedFolder { get; set; } = "";

    private readonly IdentityStore _clientStore = new(ConfigDirectories.ClientOwnedIdentitiesPath, true);
    private readonly IdentityStore _serverStore = new(ConfigDirectories.ServerOwnedIdentitiesPath, false);

    private readonly Lock _clientStoreLock = new();
    private readonly Lock _serverStoreLock = new();

    private void RefreshIdentities()
    {
        this.ClientAvailableIdentities.Clear();
        this.ServerAvailableIdentities.Clear();

        foreach (IdentityLabel label in this._clientStore.AvailableIdentities)
        {
            this.ClientAvailableIdentities.Add(label);
        }

        foreach (IdentityLabel label in this._serverStore.AvailableIdentities)
        {
            this.ServerAvailableIdentities.Add(label);
        }
    }

    public ObservableCollection<IdentityLabel> ServerAvailableIdentities { get; } = [];
    public ObservableCollection<IdentityLabel> ClientAvailableIdentities { get; } = [];

    [RelayCommand]
    public async Task CreateNewServerIdentity()
    {
        ModalTransitionInfo transition = await this.OpenModalAsync(MakeIdentityCreationModal());

        if (transition.Modal is not UserPasswordModalViewModel modal)
        {
            throw new InvalidOperationException("Invalid modal received");
        }

        if (transition.Action != ModalViewModel.CloseAction.Submit) return;

        TaskCompletionSource<bool> tcs = new();
        this.IsWorking = true;

        ThreadPool.QueueUserWorkItem(info =>
        {
            string friendlyName = info.Name;
            char[] password = info.Password.ToCharArray();

            lock (this._serverStoreLock)
            {
                Identity ident = this._serverStore.CreateNewIdentity(friendlyName, password);
                IdentityLabel label = new(ident.InternalName, ident.FriendlyName);
                this.ServerAvailableIdentities.Add(label);
                this.ServerSelectedIdentity = label;
            }

            tcs.SetResult(true);
        }, modal, true);

        await tcs.Task;
        this.IsWorking = false;
    }

    [RelayCommand]
    public async Task CreateNewClientIdentity()
    {
        ModalTransitionInfo transition = await this.OpenModalAsync(MakeIdentityCreationModal());

        if (transition.Modal is not UserPasswordModalViewModel modal)
        {
            throw new InvalidOperationException("Invalid modal received");
        }

        if (transition.Action != ModalViewModel.CloseAction.Submit) return;

        TaskCompletionSource<bool> tcs = new();
        this.IsWorking = true;

        ThreadPool.QueueUserWorkItem(info =>
        {
            string friendlyName = info.Name;
            char[] password = info.Password.ToCharArray();

            lock (this._clientStoreLock)
            {
                Identity ident = this._clientStore.CreateNewIdentity(friendlyName, password);
                IdentityLabel label = new(ident.InternalName, ident.FriendlyName);
                this.ClientAvailableIdentities.Add(label);
                this.ClientSelectedIdentity = label;
            }

            tcs.SetResult(true);
        }, modal, true);

        await tcs.Task;
        this.IsWorking = false;
    }

    private static UserPasswordModalViewModel MakeIdentityCreationModal() =>
        new(
            "Create a new identity",
            "Enter your information for the new identity:"
        );

    [RelayCommand]
    public void SelectClientIdentityCommand(IdentityLabel label) => this.ClientSelectedIdentity = label;

    [RelayCommand]
    public void SelectServerIdentityCommand(IdentityLabel label) => this.ServerSelectedIdentity = label;

    [RelayCommand]
    public async Task GoToClientScreen()
    {
        PasswordModalViewModel modal = new("Enter password", $"Enter the password for the identity " +
                                                             $"{this.ServerSelectedIdentity.FriendlyName}:");

        ModalTransitionInfo info = await this.OpenModalAsync(modal);
        char[] password = ((PasswordModalViewModel)info.Modal).Password.ToCharArray();

        // load identity
        Identity identity = Identity.LoadFromFile(
            ConfigDirectories.ServerOwnedIdentitiesPath,
            this.ServerSelectedIdentity.InternalName,
            password
        );

        this.ChangePage(new ClientScreenViewModel(identity, password, this.ClientTargetIp));
    }

    [RelayCommand]
    public async Task GoToServerScreen()
    {
        PasswordModalViewModel modal = new("Enter password", $"Enter the password for the identity " +
                                                             $"{this.ServerSelectedIdentity.FriendlyName}:");

        ModalTransitionInfo info = await this.OpenModalAsync(modal);
        char[] password = ((PasswordModalViewModel)info.Modal).Password.ToCharArray();

        // load identity
        Identity identity = Identity.LoadFromFile(
            ConfigDirectories.ServerOwnedIdentitiesPath,
            this.ServerSelectedIdentity.InternalName,
            password
        );

        this.ChangePage(new ServerViewModel(identity, this.ServerSharedFolder, password));
    }

    [RelayCommand]
    public void GoToAboutScreen() =>
        this.ChangePage(new ErrorViewModel("Not implemented"));

    [RelayCommand]
    public void GoToSettingsScreen() =>
        this.ChangePage(new ErrorViewModel("Not implemented"));
}
