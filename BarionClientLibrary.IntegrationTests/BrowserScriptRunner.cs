using System.IO;
using System.Reflection;
using BarionClientLibrary.Operations.StartPayment;
using NReco.PhantomJS;

namespace BarionClientLibrary.IntegrationTests;

internal sealed class BrowserScriptRunner
{
    public static void RunPaymentScript(StartPaymentOperationResult result)
    {
        var phantomJS = new PhantomJS();

        phantomJS.OutputReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                throw new System.Exception($"Payment script failed: {e.Data}");
            }
        };

        using var fileStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("BarionClientLibrary.IntegrationTests.PaymentScript.js");
        using var streamReader = new StreamReader(fileStream);
        var paymentScript = streamReader.ReadToEnd();

        phantomJS.RunScript(paymentScript, [result.GatewayUrl, AppSettings.BarionPayer, AppSettings.BarionPayerPassword]);
    }
}
