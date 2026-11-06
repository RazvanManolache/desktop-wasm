// Legacy-StatusBarPanelCollection overlay: replaces the runtimes/winforms
// binary-compat stub (Controls/Unsupported/StatusBar/
// StatusBar.StatusBarPanelCollection.cs, Compile-Removed in the csproj),
// whose constructor and every accessor unconditionally throw. Working
// List-backed model with owner Parent wiring so Panels.Add/Count/indexer
// behave like the framework control. Signatures mirror the upstream stub
// exactly (virtuals preserved for subclass overrides).
using System.Collections;
using System.ComponentModel;

namespace System.Windows.Forms;

public partial class StatusBar
{
    /// <summary>
    ///  This type is provided for binary compatibility with .NET Framework and is not intended to be used directly from your code.
    /// </summary>
    [Obsolete(
        Obsoletions.StatusBarMessage,
        error: false,
        DiagnosticId = Obsoletions.UnsupportedControlsDiagnosticId,
        UrlFormat = Obsoletions.SharedUrlFormat)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Browsable(false)]
    [ListBindable(false)]
    public class StatusBarPanelCollection : IList
    {
        private readonly List<StatusBarPanel> _panels = new();
        private readonly StatusBar _owner;

        public StatusBarPanelCollection(StatusBar owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        public virtual StatusBarPanel this[int index]
        {
            get => _panels[index];
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                _panels[index].Parent = null;
                _panels[index] = value;
                value.Parent = _owner;
            }
        }

        object? IList.this[int index]
        {
            get => this[index];
            set => this[index] = (StatusBarPanel)value!;
        }

        public virtual StatusBarPanel? this[string key] => _panels.FirstOrDefault(p => p.Name == key);

        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public int Count => _panels.Count;

        object ICollection.SyncRoot => this;

        bool ICollection.IsSynchronized => false;

        bool IList.IsFixedSize => false;

        public bool IsReadOnly => false;

        public virtual StatusBarPanel Add(string text)
        {
            var panel = new StatusBarPanel { Text = text };
            Add(panel);
            return panel;
        }

        public virtual int Add(StatusBarPanel value)
        {
            ArgumentNullException.ThrowIfNull(value);
            value.Parent = _owner;
            _panels.Add(value);
            return _panels.Count - 1;
        }

        int IList.Add(object? value) => Add((StatusBarPanel)value!);

        public virtual void AddRange(StatusBarPanel[] panels)
        {
            ArgumentNullException.ThrowIfNull(panels);
            foreach (var panel in panels)
                Add(panel);
        }

        public bool Contains(StatusBarPanel panel) => panel is not null && _panels.Contains(panel);

        bool IList.Contains(object? panel) => panel is StatusBarPanel p && Contains(p);

        public virtual bool ContainsKey(string key) => IndexOfKey(key) >= 0;

        public int IndexOf(StatusBarPanel panel) => panel is null ? -1 : _panels.IndexOf(panel);

        int IList.IndexOf(object? panel) => panel is StatusBarPanel p ? IndexOf(p) : -1;

        public virtual int IndexOfKey(string key)
        {
            for (int i = 0; i < _panels.Count; i++)
            {
                if (_panels[i].Name == key)
                    return i;
            }

            return -1;
        }

        public virtual void Insert(int index, StatusBarPanel value)
        {
            ArgumentNullException.ThrowIfNull(value);
            value.Parent = _owner;
            _panels.Insert(index, value);
        }

        void IList.Insert(int index, object? value) => Insert(index, (StatusBarPanel)value!);

        public virtual void Clear()
        {
            foreach (var panel in _panels)
                panel.Parent = null;
            _panels.Clear();
        }

        public virtual void Remove(StatusBarPanel value)
        {
            if (value is null)
                return;
            if (_panels.Remove(value))
                value.Parent = null;
        }

        void IList.Remove(object? value)
        {
            if (value is StatusBarPanel panel)
                Remove(panel);
        }

        public virtual void RemoveAt(int index)
        {
            var panel = _panels[index];
            _panels.RemoveAt(index);
            panel.Parent = null;
        }

        public virtual void RemoveByKey(string key)
        {
            int at = IndexOfKey(key);
            if (at >= 0)
                RemoveAt(at);
        }

        void ICollection.CopyTo(Array dest, int index) => ((ICollection)_panels).CopyTo(dest, index);

        public IEnumerator GetEnumerator() => _panels.GetEnumerator();
    }
}
