using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.IO;

namespace PCSC
{
    public class PCSCReader : IDisposable
    {
        bool connected = false;
        private IntPtr context = IntPtr.Zero;
        private IntPtr card = IntPtr.Zero;
        private uint activeProtocol = 0;
        private byte[] atr;
        private MultiString availableReaders;
        private BackgroundWorker monitorThread = null;
        private WinSCard.SCARD_READERSTATE[] readerStates = null;

        // Debug
        private bool outputDebugString = true;
        
        public delegate void CardInsertedEventHandler(string reader, byte[] atr);
        public delegate void CardRemovedEventHandler(string reader);

        public event CardInsertedEventHandler CardInserted = null;
        public event CardRemovedEventHandler CardRemoved = null;
        
        public PCSCReader()
        {
            int result = 0;

            result = WinSCard.SCardEstablishContext(WinSCard.Scope.SCARD_SCOCPE_SYSTEM, IntPtr.Zero, IntPtr.Zero, out context);

            if (result != WinSCard.SCARD_S_SUCCESS)
            {
                Debug.WriteLine(WinSCard.SCardErrorMessage(result));
            }

            byte[] readers = null;
            uint readerCount = 0;
            result = WinSCard.SCardListReaders(context, null, readers, ref readerCount);
            
            readers = new byte[readerCount];
            result = WinSCard.SCardListReaders(context, null, readers, ref readerCount);
            availableReaders = new MultiString(readers);

            if (result != WinSCard.SCARD_S_SUCCESS)
            {
                Debug.WriteLine(WinSCard.SCardErrorMessage(result));
            }

            //Start a background worker thread which monitors available card readers.
            if ((availableReaders.Count > 0))
            {
                readerStates = new WinSCard.SCARD_READERSTATE[availableReaders.Count];

                for (int i = 0; i < readerStates.Length; i++)
                {
                    readerStates[i].szReader = availableReaders.ToArray()[i];
                }

                monitorThread = new BackgroundWorker();
                monitorThread.WorkerSupportsCancellation = true;
                monitorThread.DoWork += WaitChangeStatus;
                monitorThread.RunWorkerAsync();
            }
        }

        public bool Connect(string reader)
        {
            int result = WinSCard.SCardConnect(context, reader, WinSCard.ShareMode.SCARD_SHARE_SHARED, WinSCard.Protocol.SCARD_PROTOCOL_T0 | WinSCard.Protocol.SCARD_PROTOCOL_T1, ref card, ref activeProtocol);

            if (result != WinSCard.SCARD_S_SUCCESS)
            {
                throw new PCSCException(result);
            }
            else
            {
                connected = true;
                atr = GetAnswerToReset();
            }

            return (result == WinSCard.SCARD_S_SUCCESS) ? true : false;
        }

        public bool Disconnect()
        {
            int result = WinSCard.SCardDisconnect(card, WinSCard.Disposition.SCARD_UNPOWER_CARD);

            if (result != WinSCard.SCARD_S_SUCCESS)
            {
                throw new PCSCException(result);
            }
            else
            {
                connected = false;
                atr = null;
            }

            return (result == WinSCard.SCARD_S_SUCCESS) ? true : false;
        }

        public IEnumerable<string> Readers
        {
            get
            {
                return availableReaders.ToArray();
            }
        }

        public byte[] ATR
        {
            get
            {
                return atr;
            }
        }

        private byte[] GetAnswerToReset()
        {
            int result = 0;
            byte[] readerName = null;
            uint readerLen = 0;
            uint state = 0;
            uint protocol = 0;
            byte[] atr = null;
            uint atrLen = 0;

            result = WinSCard.SCardStatus(card, readerName, ref readerLen, out state, out protocol, atr, ref atrLen);

            if (result != WinSCard.SCARD_S_SUCCESS)
            {
                throw new PCSCException(result);
            }

            readerName = new byte[readerLen];
            atr = new byte[atrLen];
            result = WinSCard.SCardStatus(card, readerName, ref readerLen, out state, out protocol, atr, ref atrLen);

            if (result != WinSCard.SCARD_S_SUCCESS)
            {
                throw new PCSCException(result);
            }

            #region Debug output
#if DEBUG
            if (outputDebugString)
            {
                StringBuilder sb = new StringBuilder();

                for (int i = 0; i < atrLen; i++)
                {
                    sb.AppendFormat("{0:X2}", atr[i]);
                }

                Debug.WriteLine(sb.ToString());
            }
#endif
            #endregion

            MultiString msReaderName = new MultiString(readerName);

            if (result != WinSCard.SCARD_S_SUCCESS)
            {
                throw new PCSCException(result);
            }

            return atr;
        }

        public APDUResponse Transmit(APDUCommand apdu)
        {
            byte[] recvBuffer = new byte[256];
            int recvLength = recvBuffer.Length;
            IntPtr sendPci = IntPtr.Zero;

            switch ((WinSCard.Protocol)activeProtocol)
            {
                case WinSCard.Protocol.SCARD_PROTOCOL_T0:
                    sendPci = WinSCard.SCARD_PCI_T0;
                    break;
                case WinSCard.Protocol.SCARD_PROTOCOL_T1:
                    sendPci = WinSCard.SCARD_PCI_T1;
                    break;
            }

            #region Debug output
#if DEBUG
            if (outputDebugString)
            {
                StringBuilder sb = new StringBuilder();

                foreach (byte b in apdu.ToArray())
                {
                    sb.AppendFormat("{0:X2}", b);
                }

                Debug.WriteLine(sb.ToString());
            }
#endif
            #endregion

            int result = WinSCard.SCardTransmit(card, sendPci, apdu.ToArray(), apdu.ToArray().Length, IntPtr.Zero, recvBuffer, ref recvLength);           

            if (result != WinSCard.SCARD_S_SUCCESS)
            {
                throw new PCSCException(result);
            }

            #region Debug output
#if DEBUG
            if (outputDebugString)
            {
                StringBuilder sb = new StringBuilder();

                for (int i = 0; i < recvLength; i++)
                {
                    sb.AppendFormat("{0:X2}", recvBuffer[i]);
                }

                Debug.WriteLine(sb.ToString());
            }
#endif
            #endregion

            byte[] response = new byte[recvLength];
            Buffer.BlockCopy(recvBuffer, 0, response, 0, recvLength);

            return new APDUResponse(response);
        }

        private void WaitChangeStatus(object sender, DoWorkEventArgs e)
        {
            while (!e.Cancel)
            {
                if (context == IntPtr.Zero)
                {
                    return;
                }

                int result = WinSCard.SCardGetStatusChange(context, 1000, readerStates, readerStates.Length);

                for (int i = 0; i < readerStates.Length; i++)
                {
                    // Check if the state changed from the last time.
                    if ((readerStates[i].dwEventState & (int)WinSCard.CardState.Changed) == (int)WinSCard.CardState.Changed)
                    {
                        // Check what changed
                        WinSCard.CardState state = WinSCard.CardState.None;
                        if ((readerStates[i].dwEventState & (int)WinSCard.CardState.Present) == (int)WinSCard.CardState.Present
                            && (readerStates[i].dwCurrentState & (int)WinSCard.CardState.Present) != (int)WinSCard.CardState.Present)
                        {
                            // The card was inserted                            
                            state = WinSCard.CardState.Present;
                        }
                        else if ((readerStates[i].dwEventState & (int)WinSCard.CardState.Empty) == (int)WinSCard.CardState.Empty
                            && (readerStates[i].dwCurrentState & (int)WinSCard.CardState.Empty) != (int)WinSCard.CardState.Empty)
                        {
                            // The card was removed
                            state = WinSCard.CardState.Empty;
                        }

                        if (state != WinSCard.CardState.None && readerStates[i].dwCurrentState != (int)WinSCard.CardState.None)
                        {
                            switch (state)
                            {
                                case WinSCard.CardState.Present:
                                    if (CardInserted != null)
                                    {
                                        Connect(readerStates[i].szReader);
                                        CardInserted(readerStates[i].szReader, ATR);
                                        Disconnect();
                                    }
                                    break;

                                case WinSCard.CardState.Empty:
                                    if (CardRemoved != null)
                                    {
                                        CardRemoved(readerStates[i].szReader);
                                    }
                                    break;
                            }
                        }

                        // Update the current state for the next time they are checked
                        readerStates[i].dwCurrentState = readerStates[i].dwEventState;
                    }
                }
            }
        }

        public void Dispose()
        {
            monitorThread.CancelAsync();
            monitorThread.Dispose();
            
            int result = WinSCard.SCardReleaseContext(context);

            if (result != WinSCard.SCARD_S_SUCCESS)
            {
                throw new PCSCException(result);
            }
        }
    }
}
