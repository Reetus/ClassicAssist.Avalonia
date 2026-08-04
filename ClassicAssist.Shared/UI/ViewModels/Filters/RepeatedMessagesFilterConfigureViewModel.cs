#region License

// Copyright (C) 2026 Reetus
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

using ClassicAssist.Data.Filters;
using ClassicAssist.UI.ViewModels;

namespace ClassicAssist.Shared.UI.ViewModels.Filters
{
    /// <summary>
    ///     Edits the live <see cref="RepeatedMessagesFilter.FilterOptions" /> in place - the dialog has only
    ///     a Close button, matching WPF.
    /// </summary>
    public class RepeatedMessagesFilterConfigureViewModel : BaseViewModel
    {
        private RepeatedMessagesFilter.MessageFilterOptions _options =
            new RepeatedMessagesFilter.MessageFilterOptions();

        public RepeatedMessagesFilterConfigureViewModel()
        {
        }

        public RepeatedMessagesFilterConfigureViewModel( RepeatedMessagesFilter.MessageFilterOptions options )
        {
            Options = options;
        }

        public RepeatedMessagesFilter.MessageFilterOptions Options
        {
            get => _options;
            set => SetProperty( ref _options, value );
        }
    }
}
