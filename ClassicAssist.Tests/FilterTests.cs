using System;
using System.Linq;
using ClassicAssist.Data;
using ClassicAssist.Data.Hotkeys;
using ClassicAssist.Data.Hotkeys.Commands;
using ClassicAssist.Data.Macros;
using ClassicAssist.Shared.Resources;
using ClassicAssist.UI.ViewModels;
using Xunit;
using Newtonsoft.Json.Linq;

namespace ClassicAssist.Tests
{
    public class FilterTests
    {
        [Fact]
        public void MacrosFilterTextFiltersItems()
        {
            MacrosTabViewModel vm = new MacrosTabViewModel();

            MacroEntry macro1 = new MacroEntry { Name = "AlphaMacro", Macro = string.Empty };
            MacroEntry macro2 = new MacroEntry { Name = "BetaMacro", Macro = string.Empty };

            vm.Items.Add( macro1 );
            vm.Items.Add( macro2 );

            Assert.Equal( 2, vm.FilterItems.Count );

            vm.FilterText = "beta";

            Assert.Single( vm.FilterItems );
            Assert.Contains( macro2, vm.FilterItems );

            vm.FilterText = string.Empty;

            Assert.Equal( 2, vm.FilterItems.Count );
        }

        [Fact]
        public void HotkeysFilterTextFiltersChildren()
        {
            HotkeysTabViewModel vm = new HotkeysTabViewModel();
            vm.Deserialize( new JObject(), new Options() );

            HotkeyCommand commandsCategory =
                HotkeyManager.GetInstance().Items.FirstOrDefault( c => c.IsCategory && c.Name == Strings.Commands );

            Assert.NotNull( commandsCategory );
            Assert.True( commandsCategory.Children.Count > 0 );

            HotkeyEntry child = commandsCategory.Children.First();
            string name = child.Name;

            vm.FilterText = name;

            Assert.True( vm.FilterItems.Count > 0 );
            Assert.True( vm.FilterItems.All( c => c.Children.Count > 0 ) );
            Assert.Contains( child, vm.FilterItems.SelectMany( c => c.Children ) );

            vm.FilterText = "zzz-no-such-hotkey-zzz";

            Assert.Empty( vm.FilterItems );

            vm.FilterText = string.Empty;

            Assert.Equal( HotkeyManager.GetInstance().Items.Count, vm.FilterItems.Count );
        }
    }
}
