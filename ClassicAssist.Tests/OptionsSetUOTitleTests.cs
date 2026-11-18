using ClassicAssist.Data;
using ClassicAssist.UI.ViewModels;
using Xunit;
using Newtonsoft.Json.Linq;

namespace ClassicAssist.Tests
{
    public class OptionsSetUOTitleTests
    {
        [Fact]
        public void WillRoundTripSetUOTitleOption()
        {
            Options options = new Options();
            OptionsTabViewModel vm = new OptionsTabViewModel();

            vm.Deserialize( new JObject { ["Options"] = new JObject { ["SetUOTitle"] = false } }, options );

            Assert.False( options.SetUOTitle );

            JObject json = new JObject();
            vm.Serialize( json );

            Assert.False( json["Options"]["SetUOTitle"].ToObject<bool>() );
        }

        [Fact]
        public void WillDefaultSetUOTitleToTrue()
        {
            Options options = new Options();
            OptionsTabViewModel vm = new OptionsTabViewModel();

            vm.Deserialize( new JObject { ["Options"] = new JObject() }, options );

            Assert.True( options.SetUOTitle );
        }
    }
}
