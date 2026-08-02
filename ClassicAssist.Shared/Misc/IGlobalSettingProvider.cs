using ClassicAssist.Data;
using Newtonsoft.Json.Linq;

namespace ClassicAssist.Misc
{
    /// <summary>
    ///     An <see cref="ISettingProvider" /> that additionally persists a profile-independent slice of
    ///     its settings to a separate file in the global directory, shared across every profile.
    /// </summary>
    public interface IGlobalSettingProvider : ISettingProvider
    {
        string GetGlobalFilename();

        void Serialize( JObject json, bool global );

        void Deserialize( JObject json, Options options, bool global );
    }
}
