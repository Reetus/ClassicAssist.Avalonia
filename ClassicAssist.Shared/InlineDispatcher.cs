#region License

// Copyright (C) 2025 Reetus
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY

#endregion

using System;
using System.Threading.Tasks;

namespace ClassicAssist.Shared;

/// <summary>
///     Runs work on the calling thread. Used as the default for <see cref="Engine.Dispatcher" /> so that
///     headless consumers - tests, tooling - don't have to stand up an Avalonia dispatcher, and so a view
///     model constructed before the UI exists can't null-reference on <c>_dispatcher.Invoke</c>.
/// </summary>
public class InlineDispatcher : IDispatcher
{
    public void Invoke( Action action )
    {
        action();
    }

    public Task InvokeAsync( Action action )
    {
        action();

        return Task.CompletedTask;
    }

    public bool CheckAccess()
    {
        return true;
    }
}
