using System;

using CommunityToolkit.Mvvm.ComponentModel;

namespace AdLib.ViewModel;

public abstract class ViewModelBase : ObservableObject
{
    public abstract Type ViewType { get; }
}
