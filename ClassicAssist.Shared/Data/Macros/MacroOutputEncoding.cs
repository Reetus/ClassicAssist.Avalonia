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

using System.Runtime.InteropServices;
using System.Text;

namespace ClassicAssist.Data.Macros;

/// <summary>
///     The encoding the DLR hands macro output to the stream differs per platform: on Windows it
///     writes UTF-16 bytes, everywhere else UTF-8. The stream that decodes that output (see
///     <see cref="TextStream" />) must use the matching encoding or plain ASCII print output comes
///     back mis-decoded (H\0e\0l\0l\0o\0 pairs on Windows, CJK garbage elsewhere).
/// </summary>
public static class MacroOutputEncoding
{
    public static Encoding Current => RuntimeInformation.IsOSPlatform( OSPlatform.Windows )
        ? Encoding.Unicode
        : Encoding.UTF8;
}
