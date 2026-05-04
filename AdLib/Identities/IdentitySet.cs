using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace AdLib.Identities;

public class IdentitySet
{
    private readonly List<Identity> _unlockedIdentities = [];
    private readonly Dictionary<string, IdentityMetadata> _availableIdentities = [];

    public string FolderPath { get; }
    public ReadOnlyCollection<Identity> UnlockedIdentities => this._unlockedIdentities.AsReadOnly();
    public ICollection<string> AvailableIdentities => this._availableIdentities.Keys;
    
    public IdentitySet(string folderPath)
    {
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

        Identity identity = new(metadata, friendlyName, password);
        this._unlockedIdentities.Add(identity);
        return identity;
    }
}
