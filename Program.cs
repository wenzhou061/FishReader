using System.Text;
using System.Threading;
using System.Windows;

namespace FishReader;

internal static class Program
{
    private static System.Threading.Mutex? _singleInstance;
    private static EventWaitHandle? _activationEvent;

    [STAThread]
    private static void Main()
    {
        _singleInstance = new System.Threading.Mutex(true, @"Local\FishReader-6D367E8A-322E-4F2C-944B-9DFA50830251", out var created);
        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset,
            @"Local\FishReader-Activate-6D367E8A-322E-4F2C-944B-9DFA50830251");
        if (!created)
        {
            _activationEvent.Set();
            return;
        }

        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var app = new ReaderApplication(_activationEvent);
            app.Run();
        }
        finally
        {
            _activationEvent.Dispose();
            _singleInstance.ReleaseMutex();
        }
    }
}
