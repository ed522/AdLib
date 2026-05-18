using System;
using System.IO;

namespace AdLib.Config;

public class ConfigDirectories
{
    private static readonly string _configBase =
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    #region Directories

    public static readonly string ConfigDirectory = Path.Combine(_configBase, "adlib");

    public static readonly string IdentitiesPath = Path.Combine(ConfigDirectory, "identity");

    // define path-parts to stay consistent between client/server
    private const string _ownCertsDir = "owned_identities";
    private const string _globalTrustDir = "globally_trusted_identities";
    private const string _localTrustDir = "trusted_identities";

    public static readonly string ServerIdentitiesPath = Path.Combine(IdentitiesPath, "server");

    public static readonly string ServerOwnedIdentitiesPath =
        Path.Combine(ServerIdentitiesPath, _ownCertsDir);

    public static readonly string ServerGloballyTrustedIdentitiesPath =
        Path.Combine(ServerIdentitiesPath, _globalTrustDir);

    public static readonly string ServerLocallyTrustedIdentitiesPath =
        Path.Combine(ServerIdentitiesPath, _localTrustDir);

    public static readonly string ClientIdentitiesPath = Path.Combine(IdentitiesPath, "client");

    public static readonly string ClientOwnedIdentitiesPath =
        Path.Combine(ClientIdentitiesPath, _ownCertsDir);

    public static readonly string ClientGloballyTrustedIdentitiesPath =
        Path.Combine(ClientIdentitiesPath, _globalTrustDir);

    public static readonly string ClientLocallyTrustedIdentitiesPath =
        Path.Combine(ClientIdentitiesPath, _localTrustDir);

    #endregion

    #region Files

    public static readonly string GlobalConfigFilePath = Path.Combine(ConfigDirectory, "config.json");

    #endregion

    public static void SetupConfigDirectories()
    {
        if (!Directory.Exists(ConfigDirectory)) Directory.CreateDirectory(ConfigDirectory);

        if (!Directory.Exists(IdentitiesPath)) Directory.CreateDirectory(IdentitiesPath);

        if (!Directory.Exists(ServerIdentitiesPath)) Directory.CreateDirectory(ServerIdentitiesPath);

        if (!Directory.Exists(ServerOwnedIdentitiesPath))
            Directory.CreateDirectory(ServerOwnedIdentitiesPath);

        if (!Directory.Exists(ServerGloballyTrustedIdentitiesPath))
            Directory.CreateDirectory(ServerGloballyTrustedIdentitiesPath);

        if (!Directory.Exists(ServerLocallyTrustedIdentitiesPath))
            Directory.CreateDirectory(ServerLocallyTrustedIdentitiesPath);

        if (!Directory.Exists(ClientIdentitiesPath)) Directory.CreateDirectory(ClientIdentitiesPath);

        if (!Directory.Exists(ClientOwnedIdentitiesPath))
            Directory.CreateDirectory(ClientOwnedIdentitiesPath);

        if (!Directory.Exists(ClientGloballyTrustedIdentitiesPath))
            Directory.CreateDirectory(ClientGloballyTrustedIdentitiesPath);

        if (!Directory.Exists(ClientLocallyTrustedIdentitiesPath))
            Directory.CreateDirectory(ClientLocallyTrustedIdentitiesPath);
    }
}
