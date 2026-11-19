using System;
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Guochao
{
    // A finite, opt-in native acceptance run must not be classified as idle
    // while its paused route search is yielding. No production game code uses
    // this lease. It permits system/display sleep and changes no preferences.
    internal sealed class MacDiagnosticActivityLease : IDisposable
    {
        public bool Active { get; private set; }
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
        const string ObjC="/usr/lib/libobjc.A.dylib";
        [DllImport(ObjC)] static extern IntPtr objc_getClass(string name);
        [DllImport(ObjC)] static extern IntPtr sel_registerName(string name);
        [DllImport(ObjC,EntryPoint="objc_msgSend")] static extern IntPtr Send(IntPtr receiver,IntPtr selector);
        [DllImport(ObjC,EntryPoint="objc_msgSend")] static extern IntPtr SendPointer(IntPtr receiver,IntPtr selector,IntPtr argument);
        [DllImport(ObjC,EntryPoint="objc_msgSend")] static extern IntPtr Begin(IntPtr receiver,IntPtr selector,ulong options,IntPtr reason);
        [DllImport(ObjC,EntryPoint="objc_msgSend")] static extern void End(IntPtr receiver,IntPtr selector,IntPtr activity);
        IntPtr process,activity;
#endif
        public static MacDiagnosticActivityLease Acquire()
        {
            var lease=new MacDiagnosticActivityLease();
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
            IntPtr utf8=Marshal.StringToHGlobalAnsi("Finite Guochao gameplay acceptance");
            try
            {
                lease.process=Send(objc_getClass("NSProcessInfo"),sel_registerName("processInfo"));
                IntPtr reason=SendPointer(objc_getClass("NSString"),sel_registerName("stringWithUTF8String:"),utf8);
                // NSProcessInfo.h: UserInitiated minus IdleSystemSleepDisabled.
                const ulong options=0x00FFFFFFUL & ~(1UL<<20);
                lease.activity=Begin(lease.process,sel_registerName("beginActivityWithOptions:reason:"),options,reason);
                if(lease.activity!=IntPtr.Zero)
                {Send(lease.activity,sel_registerName("retain"));lease.Active=true;}
            }
            finally{Marshal.FreeHGlobal(utf8);}
#endif
            return lease;
        }
        public void Dispose()
        {
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
            if(activity!=IntPtr.Zero)
            {
                End(process,sel_registerName("endActivity:"),activity);
                Send(activity,sel_registerName("release"));activity=IntPtr.Zero;
            }
#endif
            Active=false;
        }
    }
}
