using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace AdLib.Identities;

public class IdentityStore
{
    private readonly bool _isClient;
    private readonly Dictionary<string, IdentityMetadata> _availableIdentities = [];
    private readonly List<Identity> _unlockedIdentities = [];

    public IdentityStore(string folderPath, bool isClient)
    {
        this._isClient = isClient;
        const string fileExtension = IdentityMetadata.FILE_EXTENSION;
        this.FolderPath = folderPath;

        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
            return; // do not search
        }
        // discover identities

        IEnumerable<string> files =
            from string f in Directory.GetFiles(folderPath)
            where f.EndsWith(fileExtension)
            select f;

        foreach (string file in files)
        {
            IdentityMetadata meta = IdentityMetadata.LoadMetadata(folderPath, file);
            this._availableIdentities.Add(file[..^fileExtension.Length], meta);
        }
    }

    public string FolderPath { get; }
    public ReadOnlyCollection<Identity> UnlockedIdentities => this._unlockedIdentities.AsReadOnly();
    public ICollection<string> AvailableIdentities => this._availableIdentities.Keys;

    public Identity UnlockIdentity(string friendlyName, char[] password)
    {
        if (!this._availableIdentities.TryGetValue(friendlyName, out IdentityMetadata? metadata))
        {
            throw new InvalidOperationException($"Identity {friendlyName} not found.");
        }

        Identity? possibleExisting =
            this._unlockedIdentities.FirstOrDefault(i => i.FriendlyName == friendlyName);

        if (possibleExisting is not null)
        {
            return possibleExisting;
        }

        Identity identity = Identity.CreateNew(this.FolderPath, friendlyName, password, this._isClient);
        this._unlockedIdentities.Add(identity);
        return identity;
    }
}
