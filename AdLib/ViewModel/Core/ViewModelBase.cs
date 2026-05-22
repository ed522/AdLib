using System;
using System.Diagnostics.CodeAnalysis;

using CommunityToolkit.Mvvm.ComponentModel;

namespace AdLib.ViewModel.Core;

public abstract class ViewModelBase : ObservableObject
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    public abstract Type ViewType { get; }
}
