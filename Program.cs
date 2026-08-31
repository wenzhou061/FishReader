using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace FishReader;

internal static class Program
{
    private static System.Threading.Mutex? _singleInstance;
    private static EventWaitHandle? _activationEvent;

    [STAThread]
    private static void Main()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            CrashLogger.Write("未处理异常", e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashLogger.Write("未观察到的任务异常", e.Exception);
            e.SetObserved();
        };

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
            app.DispatcherUnhandledException += (_, e) =>
                CrashLogger.Write("界面线程未处理异常", e.Exception);
            app.Run();
        }
        finally
        {
            _activationEvent.Dispose();
            _singleInstance.ReleaseMutex();
        }
    }
}
