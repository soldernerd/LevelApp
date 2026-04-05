using CommunityToolkit.Mvvm.ComponentModel;

namespace LevelApp.App.ViewModels;

/// <summary>
/// Base class for all ViewModels. Inherits <see cref="ObservableObject"/> from
/// CommunityToolkit.Mvvm for INotifyPropertyChanged support and source-generated
/// observable properties.
/// </summary>
public abstract class ViewModelBase : ObservableObject
{
}
