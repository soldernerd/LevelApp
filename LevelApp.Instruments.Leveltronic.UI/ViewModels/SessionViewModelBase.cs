using CommunityToolkit.Mvvm.ComponentModel;
using LevelApp.Instruments.Leveltronic;

namespace LevelApp.Instruments.Leveltronic.UI.ViewModels;

/// <summary>
/// A view model that works on the <see cref="LeveltronicSession"/> the management view has open.
/// The management view model calls <see cref="Attach"/> when it connects and <see cref="Detach"/>
/// when the session is released or lost; both are called on the UI thread.
/// </summary>
public interface ISessionAware
{
    void Attach(LeveltronicSession session);
    void Detach();
}

/// <summary>
/// Common plumbing for the per-tab view models (live screen, oscilloscope, diagnostics).
/// Create instances on the UI thread so <see cref="Post"/> can marshal back to it.
/// </summary>
public abstract partial class SessionViewModelBase : ObservableObject, ISessionAware
{
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;

    /// <summary>The open session, or null while disconnected.</summary>
    protected LeveltronicSession? Session { get; private set; }

    /// <summary>True while a session is attached.</summary>
    [ObservableProperty]
    private bool _isConnected;

    public void Attach(LeveltronicSession session)
    {
        Session = session;
        IsConnected = true;
        OnAttached(session);
    }

    public void Detach()
    {
        OnDetached();
        Session = null;
        IsConnected = false;
    }

    /// <summary>Subscribe to the session's feed / API here.</summary>
    protected virtual void OnAttached(LeveltronicSession session) { }

    /// <summary>Unsubscribe and cancel anything running here.</summary>
    protected virtual void OnDetached() { }

    /// <summary>Runs <paramref name="action"/> on the UI thread (inline if there is none, e.g. in tests).</summary>
    protected void Post(Action action)
    {
        if (_ui is null || ReferenceEquals(SynchronizationContext.Current, _ui)) action();
        else _ui.Post(_ => action(), null);
    }
}
