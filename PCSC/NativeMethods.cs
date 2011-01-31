using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;
using System.ComponentModel;

namespace PCSC
{
    class WinSCard
    {
        private WinSCard()
        {
        }

        // Error codes
        internal const int SCARD_S_SUCCESS = 0;

        // Scope
        internal enum Scope
        {
            SCARD_SCOPE_USER = 0,
            SCARD_SCOPE_TERMINAL = 1,
            SCARD_SCOCPE_SYSTEM = 2
        }

        // Share
        internal enum ShareMode
        {
            SCARD_SHARE_EXCLUSIVE = 1,
            SCARD_SHARE_SHARED = 2,
            SCARD_SHARE_DIRECT = 3
        }

        // Protocol
        internal enum Protocol
        {
            SCARD_PROTOCOL_T0 = 1,
            SCARD_PROTOCOL_T1 = 2
        }

        // Disposition
        internal enum Disposition
        {
            SCARD_LEAVE_CARD = 0,
            SCARD_RESET_CARD = 1,
            SCARD_UNPOWER_CARD = 2,
            SCARD_EJECT_CARD = 3
        }

        //CardState enumeration, used by the PC/SC function SCardGetStatusChanged.    
        internal enum CardState
        {
            None = 0,
            Ignore = 1,
            Changed = 2,
            Unknown = 4,
            Unavailable = 8,
            Empty = 16,
            Present = 32,
            AttributeMatch = 64,
            Exclusive = 128,
            InUse = 256,
            Mute = 512,
            Unpowered = 1024
        }

        [StructLayout(LayoutKind.Sequential)]
        internal class SCARD_IO_REQUEST
        {
            internal uint dwProtocol;
            internal int cbPciLength;
            internal SCARD_IO_REQUEST()
            {
                dwProtocol = 0;
            }
        }

        internal static IntPtr SCARD_PCI_T0 = GetPciT0();
        internal static IntPtr SCARD_PCI_T1 = GetPciT1();

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        internal struct SCARD_READERSTATE
        {
            [MarshalAs(UnmanagedType.LPTStr)]
            public string szReader;
            public IntPtr pvUserData;
            public int dwCurrentState;
            public int dwEventState;
            public int cbAtr;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 36)]
            public byte[] rgbAtr;
        }

        // Winscard API's to be imported
        [DllImport("Winscard.dll")]
        internal static extern int SCardEstablishContext(Scope scope, IntPtr reserved1, IntPtr reserved2, out IntPtr context);
        [DllImport("Winscard.dll")]
        internal static extern int SCardReleaseContext(IntPtr context);
        [DllImport("Winscard.dll", CharSet = CharSet.Unicode)]
        internal static extern int SCardConnect(IntPtr context, string reader, ShareMode shareMode, Protocol preferedProtocols, ref IntPtr card, ref uint activeProtocol);
        [DllImport("Winscard.dll")]
        internal static extern int SCardDisconnect(IntPtr card, Disposition disposition);
        [DllImport("Winscard.dll")]
        internal static extern int SCardListReaders(IntPtr context, byte[] groups, byte[] readers, ref uint readersLength);
        [DllImport("Winscard.dll")]
        internal static extern int SCardFreeMemory(IntPtr context, IntPtr mem);
        [DllImport("Winscard.dll")]
        internal static extern int SCardStatus(IntPtr card, byte[] readerName, ref uint readerLen, out uint state, out uint protocol, byte[] atr, ref uint atrLen);
        [DllImport("Winscard.dll")]
        internal static extern int SCardTransmit(IntPtr card, IntPtr sendPci, byte[] sendBuffer, int sendLength, IntPtr recvPci, byte[] recvBuffer, ref int recvLength);
        [DllImport("Winscard.dll", CharSet = CharSet.Unicode)]
        internal static extern int SCardGetStatusChange(IntPtr context, int timeout, [In, Out] SCARD_READERSTATE[] readerStates, int numReaders);
        
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        internal extern static IntPtr LoadLibrary(string libraryName);
        [DllImport("kernel32.dll")]
        internal extern static bool FreeLibrary(IntPtr handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        internal extern static IntPtr GetProcAddress(IntPtr handle, string procName);

        private static IntPtr GetPciT0()
        {
            IntPtr handle = LoadLibrary("Winscard.dll");
            IntPtr pci = GetProcAddress(handle, "g_rgSCardT0Pci");
            FreeLibrary(handle);
            return pci;
        }

        private static IntPtr GetPciT1()
        {
            IntPtr handle = LoadLibrary("Winscard.dll");
            IntPtr pci = GetProcAddress(handle, "g_rgSCardT1Pci");
            FreeLibrary(handle);
            return pci;
        }

        internal static string SCardErrorMessage(int error)
        {
            string errorMessage = new Win32Exception(error).Message;
            return errorMessage;
        }
    }
}
