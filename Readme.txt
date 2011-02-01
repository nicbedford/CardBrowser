For a little while now I have been working on a utility to allow you to query and view the contents of the chip on your Chip & PIN / EMV smart card.

I've been working in .net which meant lots of P/Invoke calls to the Microsoft Smartcard API's to access the PC/SC reader hardware. There were a few samples out there and http://www.pinvoke.net/ is always a great resource, but most of the P/Invoke implementations had problems in one way or another, notably using Int rather than IntPtr which meant things didn't work correctly on 64 bit windows. Anyway after a while I managed to create a PCSC interop assembly that allowed me to communicate with my smart card reader correctly in both 32 bit and 64 bit Windows.

Next I had to read through my copies of the EMVCo and ISO 7816 specs and figure out exactly what calls I had to make through my PCSC interop assembly to get all the information. Needless to say I've been keeping busy in my spare time. 
