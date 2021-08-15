using System.Runtime.InteropServices;

namespace AutoCore.Utils
{
    public delegate bool ExitEventHandler(byte sig);

    public abstract class ExitableProgram
    {
        protected static ExitEventHandler ExitHandler { get; set; }

        protected static void Initialize(ExitEventHandler handler)
        {
            ExitHandler += handler;

            NativeMethods.SetConsoleCtrlHandler(ExitHandler, true);
        }
    }

    internal class NativeMethods
    {
        [DllImport("Kernel32")]
        public static extern bool SetConsoleCtrlHandler(ExitEventHandler handler, bool add);
    }
}
