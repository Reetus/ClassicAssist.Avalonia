using System;
using System.Collections.ObjectModel;
using ClassicAssist.Data.Autoloot;
using ClassicAssist.Shared.UI;

namespace ClassicAssist.UI.Models
{
    /// <summary>
    ///     A named, saved set of filter conditions for the Entity Collection Viewer. Flat (AND-only) by
    ///     design - unlike old's <c>EntityCollectionFilterEntry</c>, there are no nested boolean groups.
    /// </summary>
    public class FilterProfile : SetPropertyNotifyChanged
    {
        private ObservableCollection<AutolootConstraintEntry> _conditions =
            new ObservableCollection<AutolootConstraintEntry>();

        private string _name;

        public ObservableCollection<AutolootConstraintEntry> Conditions
        {
            get => _conditions;
            set => SetProperty( ref _conditions, value );
        }

        public Guid ID { get; set; } = Guid.NewGuid();

        public string Name
        {
            get => _name;
            set => SetProperty( ref _name, value );
        }
    }
}
