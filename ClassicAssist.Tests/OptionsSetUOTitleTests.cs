using ClassicAssist.Data;
using ClassicAssist.UI.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace ClassicAssist.Tests
{
    [TestClass]
    public class OptionsSetUOTitleTests
    {
        [TestMethod]
        public void WillRoundTripSetUOTitleOption()
        {
            Options options = new Options();
            OptionsTabViewModel vm = new OptionsTabViewModel();

            vm.Deserialize( new JObject { ["Options"] = new JObject { ["SetUOTitle"] = false } }, options );

            Assert.IsFalse( options.SetUOTitle );

            JObject json = new JObject();
            vm.Serialize( json );

            Assert.IsFalse( json["Options"]["SetUOTitle"].ToObject<bool>() );
        }

        [TestMethod]
        public void WillDefaultSetUOTitleToTrue()
        {
            Options options = new Options();
            OptionsTabViewModel vm = new OptionsTabViewModel();

            vm.Deserialize( new JObject { ["Options"] = new JObject() }, options );

            Assert.IsTrue( options.SetUOTitle );
        }
    }
}
