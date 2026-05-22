using System;
using System.IO;

namespace AdLib.Config;

public class ConfigDirectories
{
    private static readonly string _configBase =
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    #region Directories

    public static readonly string ConfigDirectory = Path.Combine(_configBase, ".adlib");

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
        Directory.CreateDirectory(ConfigDirectory);

        Directory.CreateDirectory(IdentitiesPath);

        Directory.CreateDirectory(ServerIdentitiesPath);

        Directory.CreateDirectory(ServerOwnedIdentitiesPath);
        Directory.CreateDirectory(ServerGloballyTrustedIdentitiesPath);
        Directory.CreateDirectory(ServerLocallyTrustedIdentitiesPath);

        Directory.CreateDirectory(ClientIdentitiesPath);

        Directory.CreateDirectory(ClientOwnedIdentitiesPath);
        Directory.CreateDirectory(ClientGloballyTrustedIdentitiesPath);
        Directory.CreateDirectory(ClientLocallyTrustedIdentitiesPath);
    }
}
