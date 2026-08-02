using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using ClassicAssist.Misc;
using ClassicAssist.Shared.Resources;
using ClassicAssist.Shared.UI;

namespace ClassicAssist.UI.ViewModels
{
    /// <summary>
    ///     One entry in an <see cref="ClassicAssist.Shared.Misc.ThreadPriorityQueue{T}" />-backed action queue -
    ///     a long-running operation (move, drop, etc.) rendered as a status row with a cancel button while it
    ///     runs.
    /// </summary>
    public class QueueAction : SetPropertyNotifyChanged
    {
        private ICommand _cancelCommand;
        private string _status;

        public Func<QueueAction, Task<bool>> Action { get; set; }

        public ICommand CancelCommand => _cancelCommand ?? ( _cancelCommand = new RelayCommand( Cancel, o => true ) );

        public CancellationTokenSource CancellationTokenSource { get; set; }

        public string Status
        {
            get => _status;
            set => SetProperty( ref _status, value );
        }

        private void Cancel( object obj )
        {
            CancellationTokenSource.Cancel();
            Status = Strings.Cancel;
        }
    }
}
