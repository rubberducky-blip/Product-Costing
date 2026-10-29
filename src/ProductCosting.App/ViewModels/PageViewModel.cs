using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ProductCosting.Core.Data;

namespace ProductCosting.App.ViewModels;

/// <summary>A page shown in the main area.</summary>
public abstract class PageViewModel : ObservableObject
{
    /// <summary>Sidebar section to highlight: products, merchandise, raw, suppliers, settings.</summary>
    public abstract string Section { get; }

    /// <summary>True when the page holds edits that are not saved.</summary>
    public virtual bool IsDirty => false;

    /// <summary>Saves the page. Returns false when it couldn't (validation), so navigation stays.</summary>
    public virtual bool TrySave() => true;

    public virtual void Discard() { }

    /// <summary>Called (on the UI thread) after data changed anywhere in the app.</summary>
    public virtual void OnDataChanged(DataChange change) { }

    public virtual void OnShown() { }

    /// <summary>True when the record behind the page no longer exists, so Back skips it.</summary>
    public virtual bool IsGone => false;

    // ---------- undo (Ctrl+Z), for forms ----------
    // Each burst of edits (typing in a field, until a short pause) is one step; Ctrl+Z restores the state before it.

    readonly Stack<(object State, string Key)> _undo = new();
    (object State, string Key)? _stable;
    bool _inBurst;
    bool _restoring;
    DispatcherTimer? _burstTimer;

    /// <summary>The form's current values (null = the page has no undo).</summary>
    protected virtual object? CaptureState() => null;

    /// <summary>Puts captured values back into the form.</summary>
    protected virtual void RestoreState(object state) { }

    /// <summary>Text that is equal for equal form values.</summary>
    protected virtual string StateKey() => "";

    /// <summary>Starts a fresh undo history (after loading or saving).</summary>
    protected void ResetUndo()
    {
        _undo.Clear();
        _inBurst = false;
        _burstTimer?.Stop();
        _stable = CaptureState() is { } s ? (s, StateKey()) : null;
    }

    /// <summary>Call after every edit of the form.</summary>
    protected void NoteEdit()
    {
        if (_restoring || _stable is not { } stable) return;
        if (!_inBurst)
        {
            if (StateKey() == stable.Key) return;   // nothing actually changed
            _undo.Push(stable);
            _inBurst = true;
        }
        if (_burstTimer is null)
        {
            _burstTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            _burstTimer.Tick += (_, _) => EndBurst();
        }
        _burstTimer.Stop();
        _burstTimer.Start();
    }

    void EndBurst()
    {
        _burstTimer?.Stop();
        _inBurst = false;
        if (CaptureState() is { } s) _stable = (s, StateKey());
    }

    /// <summary>Restores the state before the last burst of edits. False when there is nothing to undo.</summary>
    public bool Undo()
    {
        if (_inBurst) EndBurst();
        while (_undo.Count > 0)
        {
            var (state, key) = _undo.Pop();
            if (key == StateKey()) continue;
            _restoring = true;
            try { RestoreState(state); }
            finally { _restoring = false; }
            _stable = (state, key);
            return true;
        }
        return false;
    }

    public bool SupportsUndo => _stable is not null;
}
