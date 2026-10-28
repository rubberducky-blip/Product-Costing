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
}
