using System;
using System.Linq;
using ClassicAssist.Data;
using ClassicAssist.Data.Hotkeys;
using ClassicAssist.Data.Hotkeys.Commands;
using ClassicAssist.Data.Macros;
using ClassicAssist.Shared.Resources;
using ClassicAssist.UI.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace ClassicAssist.Tests
{
    [TestClass]
    public class FilterTests
    {
        [TestMethod]
        public void MacrosFilterTextFiltersItems()
        {
            MacrosTabViewModel vm = new MacrosTabViewModel();

            MacroEntry macro1 = new MacroEntry { Name = "AlphaMacro", Macro = string.Empty };
            MacroEntry macro2 = new MacroEntry { Name = "BetaMacro", Macro = string.Empty };

            vm.Items.Add( macro1 );
            vm.Items.Add( macro2 );

            Assert.AreEqual( 2, vm.FilterItems.Count );

            vm.FilterText = "beta";

            Assert.AreEqual( 1, vm.FilterItems.Count );
            Assert.IsTrue( vm.FilterItems.Contains( macro2 ) );

            vm.FilterText = string.Empty;

            Assert.AreEqual( 2, vm.FilterItems.Count );
        }

        [TestMethod]
        public void HotkeysFilterTextFiltersChildren()
        {
            HotkeysTabViewModel vm = new HotkeysTabViewModel();
            vm.Deserialize( new JObject(), new Options() );

            HotkeyCommand commandsCategory =
                HotkeyManager.GetInstance().Items.FirstOrDefault( c => c.IsCategory && c.Name == Strings.Commands );

            Assert.IsNotNull( commandsCategory );
            Assert.IsTrue( commandsCategory.Children.Count > 0 );

            HotkeyEntry child = commandsCategory.Children.First();
            string name = child.Name;

            vm.FilterText = name;

            Assert.IsTrue( vm.FilterItems.Count > 0 );
            Assert.IsTrue( vm.FilterItems.All( c => c.Children.Count > 0 ) );
            Assert.IsTrue( vm.FilterItems.SelectMany( c => c.Children ).Contains( child ) );

            vm.FilterText = "zzz-no-such-hotkey-zzz";

            Assert.AreEqual( 0, vm.FilterItems.Count );

            vm.FilterText = string.Empty;

            Assert.AreEqual( HotkeyManager.GetInstance().Items.Count, vm.FilterItems.Count );
        }
    }
}
