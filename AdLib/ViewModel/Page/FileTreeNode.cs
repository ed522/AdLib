using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;

namespace AdLib.ViewModel.Page;

public partial class FileTreeNode : ObservableObject
{
    public delegate IAsyncEnumerable<FileTreeNode> FileTreeLoader(FileTreeNode baseNode);

    public string FullPath { get; }
    public bool IsDirectory { get; }

    [ObservableProperty] private bool _isExpanded;

    partial void OnIsExpandedChanged(bool value)
    {
        if (value) _ = this.LoadSubchildren();
    }

    private bool _isShallowPopulated; // = false
    private bool _isFullyPopulated; // = false

    private readonly FileTreeLoader _loader;

    /// <inheritdoc />
    public FileTreeNode(string fullPath, bool isDirectory, FileTreeLoader loader)
    {
        this._loader = loader;
        this.FullPath = fullPath;
        this.IsDirectory = isDirectory;
    }

    public ObservableCollection<FileTreeNode> Children { get; } = [];

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
}
