using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace AdLib.Identities;

public class IdentityStore
{
    public readonly record struct IdentityLabel(Guid InternalName, string FriendlyName);
    
    private readonly bool _isClient;
    private readonly Dictionary<Guid, IdentityMetadata> _availableIdentities = [];
    private readonly Dictionary<Guid, IdentityLabel> _availableIdentityLabels = [];
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
            this._availableIdentities.Add(Identity.GetCertificateGuid(meta.Certificate), meta);
        }
    }

    public string FolderPath { get; }
    public ReadOnlyCollection<Identity> UnlockedIdentities => this._unlockedIdentities.AsReadOnly();
    public ICollection<IdentityLabel> AvailableIdentities => this._availableIdentityLabels.Values;

    public Identity UnlockIdentity(Guid internalName, char[] password)
    {
        if (!this._availableIdentities.TryGetValue(internalName, out IdentityMetadata? metadata))
        {
            throw new InvalidOperationException($"Identity {internalName} not found");
        }

        Identity? possibleExisting = this._unlockedIdentities.FirstOrDefault(i => i.InternalName == internalName);

        if (possibleExisting is not null)
        {
            return possibleExisting;
        }

        Identity identity = new(metadata, password);
        this._unlockedIdentities.Add(identity);
        return identity;
    }

    public bool TryGetIdentityLabel(Guid internalName, out IdentityLabel label) =>
        this._availableIdentityLabels.TryGetValue(internalName, out label);
}
