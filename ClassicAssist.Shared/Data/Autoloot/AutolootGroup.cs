#region License

// Copyright (C) 2021 Reetus
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

#endregion

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ClassicAssist.UI.Misc.DraggableTreeView;

namespace ClassicAssist.Data.Autoloot
{
    public class AutolootGroup : INotifyPropertyChanged, IDraggableGroup
    {
        private ObservableCollection<IDraggable> _children = new ObservableCollection<IDraggable>();
        private bool _enabled = true;
        private string _name;

        public AutolootGroup()
        {
            Children.CollectionChanged += ChildrenOnCollectionChanged;
        }

        public bool Enabled
        {
            get => _enabled;
            set => SetProperty( ref _enabled, value );
        }

        public string Name
        {
            get => _name;
            set => SetProperty( ref _name, value );
        }

        public ObservableCollection<IDraggable> Children
        {
            get => _children;
            set => SetProperty( ref _children, value );
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void ChildrenOnCollectionChanged( object sender, NotifyCollectionChangedEventArgs e )
        {
            if ( e.NewItems != null )
            {
                foreach ( object newItem in e.NewItems )
                {
                    if ( !( newItem is AutolootEntry entry ) )
                    {
                        continue;
                    }

                    entry.Group = this;
                }
            }

            if ( e.OldItems != null )
            {
                foreach ( object oldItem in e.OldItems )
                {
                    if ( !( oldItem is AutolootEntry entry ) )
                    {
                        continue;
                    }

                    entry.Group = null;
                }
            }
        }

        protected virtual void OnPropertyChanged( [CallerMemberName] string propertyName = null )
        {
            PropertyChanged?.Invoke( this, new PropertyChangedEventArgs( propertyName ) );
        }

        // ReSharper disable once RedundantAssignment
        public virtual void SetProperty<T>( ref T obj, T value, [CallerMemberName] string propertyName = "" )
        {
            obj = value;
            OnPropertyChanged( propertyName );
        }
    }
}
