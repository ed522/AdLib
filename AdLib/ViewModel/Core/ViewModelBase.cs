using System;

using CommunityToolkit.Mvvm.ComponentModel;

namespace AdLib.ViewModel.Core;

public abstract class ViewModelBase : ObservableObject
{
    public abstract Type ViewType { get; }
}
