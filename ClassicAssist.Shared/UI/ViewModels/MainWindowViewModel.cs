﻿using System;
using System.Windows.Input;
using ClassicAssist.Shared;
using ClassicAssist.Data;
using ClassicAssist.Shared.Resources;
using ClassicAssist.UI.Misc;

namespace ClassicAssist.UI.ViewModels
{
    public class MainWindowViewModel : BaseViewModel
    {
        private bool _alwaysOnTop;
        private ICommand _debugCommand;
        //TODO UI
        //private DebugWindow _debugWindow;
        private string _status = Strings.Ready___;
        private string _title = Strings.ProductName;

        public MainWindowViewModel()
        {
            Engine.UpdateWindowTitleEvent += OnUpdateWindowTitleEvent;
        }

        [OptionsBinding( Property = "AlwaysOnTop" )]
        public bool AlwaysOnTop
        {
            get => _alwaysOnTop;
            set => SetProperty( ref _alwaysOnTop, value );
        }

        public ICommand DebugCommand =>
            _debugCommand ?? ( _debugCommand = new RelayCommand( ShowDebugWindow, o => true ) );

        public string Status
        {
            get => _status;
            set => SetProperty( ref _status, value );
        }

        public string Title
        {
            get => _title;
            set => SetProperty( ref _title, value );
        }

        private void OnUpdateWindowTitleEvent()
        {
            Title = string.IsNullOrEmpty( Engine.Player?.Name )
                ? Strings.ProductName
                : $"{Engine.Player?.Name} - {( Options.CurrentOptions.ShowProfileNameWindowTitle ? $"({Options.CurrentOptions.Name}) - " : "" )}{Strings.ProductName}";
        }

        private static void ShowDebugWindow( object obj )
        {
            Engine.UIInvoker.Invoke( "DebugWindow" );
        }
    }
}