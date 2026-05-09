using System;

using AdLib.ViewModel;

using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace AdLib;

public class ViewLocator : IDataTemplate
{
    public bool Match(object? data) => data is ViewModelBase;

    public Control? Build(object? data)
    {
        if (data is not ViewModelBase viewModel)
        {
            return null;
        }

        Type type = viewModel.ViewType;

        return Activator.CreateInstance(type) as Control ??
               new TextBlock { Text = $"Could not instantiate type {type.FullName!}" };
    }
}
