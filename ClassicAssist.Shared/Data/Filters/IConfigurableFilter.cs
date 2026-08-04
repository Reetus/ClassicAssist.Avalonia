using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ClassicAssist.Data.Filters
{
    public interface IConfigurableFilter
    {
        /// <summary>
        ///     Opens the filter's configure dialog. Async unlike WPF's void <c>Configure()</c>: Avalonia has
        ///     no blocking <c>ShowDialog()</c>, so the dialog is opened through
        ///     <see cref="ClassicAssist.Shared.IUIInvoker.InvokeDialog{T}" /> and awaited.
        /// </summary>
        Task Configure();

        void Deserialize( JToken token );
        JObject Serialize();
        void ResetOptions();
    }
}
