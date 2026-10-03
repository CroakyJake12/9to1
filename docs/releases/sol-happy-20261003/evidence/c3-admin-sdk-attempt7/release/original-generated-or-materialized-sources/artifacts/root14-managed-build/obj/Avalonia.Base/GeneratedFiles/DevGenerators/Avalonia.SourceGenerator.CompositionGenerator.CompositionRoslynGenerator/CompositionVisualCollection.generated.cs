
#nullable enable
#pragma warning disable CS0108, CS0114

using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using Avalonia.Rendering.Composition.Server;
using Avalonia.Rendering.Composition.Transport;
using Avalonia.Rendering.Composition.Animations;

namespace Avalonia.Rendering.Composition
{
    public unsafe partial class CompositionVisualCollection : CompositionObject, ServerListProxyHelper<CompositionVisual, ServerCompositionVisual>.IRegisterForSerialization, IList<CompositionVisual>
    {
        void InitializeDefaults()
        {
            InitializeDefaultsExtra();
            _list = new ServerListProxyHelper<CompositionVisual, ServerCompositionVisual>(this);
        }

        partial void InitializeDefaultsExtra();
        private ServerListProxyHelper<CompositionVisual, ServerCompositionVisual> _list = null!;
        void ServerListProxyHelper<CompositionVisual, ServerCompositionVisual>.IRegisterForSerialization.RegisterForSerialization() => RegisterForSerialization();
        public List<CompositionVisual>.Enumerator GetEnumerator() => _list.GetEnumerator();
        IEnumerator<CompositionVisual> IEnumerable<CompositionVisual>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)_list).GetEnumerator();
        public void Add(CompositionVisual item)
        {
            OnBeforeAdded(item);
            _list.Add(item);
            OnAdded(item);
        }

        public void Clear()
        {
            OnBeforeClear();
            _list.Clear();
            OnClear();
        }

        public bool Contains(CompositionVisual item) => _list.Contains(item);
        public void CopyTo(CompositionVisual[] array, int arrayIndex) => _list.CopyTo(array, arrayIndex);
        public bool Remove(CompositionVisual item)
        {
            var removed = _list.Remove(item);
            if (removed)
                OnRemoved(item);
            return removed;
        }

        public int Count => _list.Count;
        public bool IsReadOnly => _list.IsReadOnly;

        public int IndexOf(CompositionVisual item) => _list.IndexOf(item);
        public void Insert(int index, CompositionVisual item)
        {
            OnBeforeAdded(item);
            _list.Insert(index, item);
            OnAdded(item);
        }

        public void RemoveAt(int index)
        {
            var item = _list[index];
            _list.RemoveAt(index);
            OnRemoved(item);
        }

        public CompositionVisual this[int index]
        {
            get => _list[index];
            set
            {
                var old = _list[index];
                OnBeforeReplace(old, value);
                _list[index] = value;
                OnReplace(old, value);
            }
        }

        partial void OnBeforeAdded(CompositionVisual item);
        partial void OnAdded(CompositionVisual item);
        partial void OnRemoved(CompositionVisual item);
        partial void OnBeforeClear();
        partial void OnBeforeReplace(CompositionVisual oldItem, CompositionVisual newItem);
        partial void OnReplace(CompositionVisual oldItem, CompositionVisual newItem);
        partial void OnClear();
        private protected override void SerializeChangesCore(BatchStreamWriter writer)
        {
            {
                _list.Serialize(writer);
                base.SerializeChangesCore(writer);
            }
        }
    }
}