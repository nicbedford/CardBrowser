//
// ASN1.cs: Abstract Syntax Notation 1 - micro-parser and generator
//
// Authors:
//	Sebastien Pouliot  <sebastien@ximian.com>
//	Jesper Pedersen  <jep@itplus.dk>
//
// (C) 2002, 2003 Motus Technologies Inc. (http://www.motus.com)
// Copyright (C) 2004 Novell, Inc (http://www.novell.com)
// (C) 2004 IT+ A/S (http://www.itplus.dk)
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
// Modified by Nic Bedford (http://nicbedford.co.uk) to support multiple
// length tags.
//

using System;
using System.Collections;
using System.IO;
using System.Text;

namespace Mono.Security
{

    // References:
    // a.	ITU ASN.1 standards (free download)
    //	http://www.itu.int/ITU-T/studygroups/com17/languages/

#if INSIDE_CORLIB
	internal
#else
    public
#endif
    class ASN1 : IEnumerable
    {
        private byte[] m_aTag;
        private byte[] m_aValue;
        private ArrayList elist;

        public ASN1() : this(0x00, null) { }

        public ASN1(byte tag) : this(tag, null) { }

        public ASN1(byte tag, byte[] data) : this(new byte[] { tag }, data) { }

        public ASN1(byte[] tag, byte[] data)
        {
            m_aTag = new byte[tag.Length];
            Buffer.BlockCopy(tag, 0, m_aTag, 0, tag.Length);
            m_aValue = data;
        }

        public ASN1(byte[] data)
        {
            int pos = 0;
            int nTagLength = 0;

            if (data == null)
            {
                throw new ArgumentNullException();
            }

            // Check for multi byte tags
            if ((data[nTagLength++] & 0x1f) == 0x1f)
            {
                // Tag number is encoded in the following bytes as a sequence of seven bit bytes
                // The high bit of these bytes is used as a flag to indicate whether there's more tag available
                while ((data[nTagLength++] & 0x80) == 0x80)
                {
                }
            }

            m_aTag = new byte[nTagLength];
            Buffer.BlockCopy(data, 0, m_aTag, 0, nTagLength);

            pos = nTagLength;
            int nLenLength = 0;
            int nLength = data[pos++];

            if (nLength > 0x80)
            {
                // Composed length
                nLenLength = nLength - 0x80;
                nLength = 0;
                for (int i = 0; i < nLenLength; i++)
                {
                    nLength *= 256;
                    nLength += data[i + pos];
                }
            }
            else if (nLength == 0x80)
            {
                // Undefined length encoding
                throw new NotSupportedException("Undefined length encoding.");
            }

            m_aValue = new byte[nLength];
            Buffer.BlockCopy(data, (pos + nLenLength), m_aValue, 0, nLength);

            if ((m_aTag[0] & 0x20) == 0x20)
            {
                int nStart = (pos + nLenLength);
                Decode(data, ref nStart, data.Length);
            }
        }

        public int Count
        {
            get
            {
                if (elist == null)
                    return 0;
                return elist.Count;
            }
        }

        public byte[] Tag
        {
            get { return m_aTag; }
        }

        public int Length
        {
            get
            {
                if (m_aValue != null)
                    return m_aValue.Length;
                else
                    return 0;
            }
        }

        public byte[] Value
        {
            get
            {
                if (m_aValue == null)
                    GetBytes();
                return (byte[])m_aValue.Clone();
            }
            set
            {
                if (value != null)
                    m_aValue = (byte[])value.Clone();
            }
        }

        private bool CompareArray(byte[] array1, byte[] array2)
        {
            bool bResult = (array1.Length == array2.Length);
            if (bResult)
            {
                for (int i = 0; i < array1.Length; i++)
                {
                    if (array1[i] != array2[i])
                        return false;
                }
            }
            return bResult;
        }

        public bool Equals(byte[] asn1)
        {
            return CompareArray(this.GetBytes(), asn1);
        }

        public bool CompareValue(byte[] value)
        {
            return CompareArray(m_aValue, value);
        }

        public bool CompareTag(byte[] tag)
        {
            return CompareArray(m_aTag, tag);
        }

        public ASN1 Add(ASN1 asn1)
        {
            if (asn1 != null)
            {
                if (elist == null)
                    elist = new ArrayList();
                elist.Add(asn1);
            }
            return asn1;
        }

        public virtual byte[] GetBytes()
        {
            byte[] val = null;

            if (Count > 0)
            {
                int esize = 0;
                ArrayList al = new ArrayList();
                foreach (ASN1 a in elist)
                {
                    byte[] item = a.GetBytes();
                    al.Add(item);
                    esize += item.Length;
                }
                val = new byte[esize];
                int pos = 0;
                for (int i = 0; i < elist.Count; i++)
                {
                    byte[] item = (byte[])al[i];
                    Buffer.BlockCopy(item, 0, val, pos, item.Length);
                    pos += item.Length;
                }
            }
            else if (m_aValue != null)
            {
                val = m_aValue;
            }

            byte[] der;
            int nLengthLen = 0;

            if (val != null)
            {
                int nLength = val.Length;
                // Special for length > 127
                if (nLength > 127)
                {
                    if (nLength <= Byte.MaxValue)
                    {
                        der = new byte[3 + nLength];
                        Buffer.BlockCopy(val, 0, der, 3, nLength);
                        nLengthLen = 0x81;
                        der[2] = (byte)(nLength);
                    }
                    else if (nLength <= UInt16.MaxValue)
                    {
                        der = new byte[4 + nLength];
                        Buffer.BlockCopy(val, 0, der, 4, nLength);
                        nLengthLen = 0x82;
                        der[2] = (byte)(nLength >> 8);
                        der[3] = (byte)(nLength);
                    }
                    else if (nLength <= 0xFFFFFF)
                    {
                        // 24 bits
                        der = new byte[5 + nLength];
                        Buffer.BlockCopy(val, 0, der, 5, nLength);
                        nLengthLen = 0x83;
                        der[2] = (byte)(nLength >> 16);
                        der[3] = (byte)(nLength >> 8);
                        der[4] = (byte)(nLength);
                    }
                    else
                    {
                        // Max (Length is an integer) 32 bits
                        der = new byte[6 + nLength];
                        Buffer.BlockCopy(val, 0, der, 6, nLength);
                        nLengthLen = 0x84;
                        der[2] = (byte)(nLength >> 24);
                        der[3] = (byte)(nLength >> 16);
                        der[4] = (byte)(nLength >> 8);
                        der[5] = (byte)(nLength);
                    }
                }
                else
                {
                    // Basic case (no encoding)
                    der = new byte[m_aTag.Length + 1 + nLength];
                    Buffer.BlockCopy(val, 0, der, m_aTag.Length + 1, nLength);
                    nLengthLen = nLength;
                }
                if (m_aValue == null)
                    m_aValue = val;
            }
            else
                der = new byte[m_aTag.Length + 1];

            Buffer.BlockCopy(m_aTag, 0, der, 0, m_aTag.Length);
            der[m_aTag.Length] = (byte)nLengthLen;

            return der;
        }

        // Note: Recursive
        protected void Decode(byte[] asn1, ref int anPos, int anLength)
        {
            byte[] aTag;
            int nTagLength = 0;
            int nLength;
            byte[] aValue;
            
            // Check for multi byte tags
            if ((asn1[anPos + nTagLength++] & 0x1f) == 0x1f)
            {
                // Tag number is encoded in the following bytes as a sequence of seven bit bytes
                // The high bit of these bytes is used as a flag to indicate whether there's more tag available
                while ((asn1[anPos + nTagLength++] & 0x80) == 0x80)
                {
                }
            }

            aTag = new byte[nTagLength];
            Buffer.BlockCopy(asn1, anPos, aTag, 0, nTagLength);

            // Minimum is 2 bytes (tag + length of 0)
            while (anPos < anLength - 1)
            {
                DecodeTLV(asn1, ref anPos, out aTag, out nLength, out aValue);

                // Sometimes we get trailing 0
                if (aTag[0] == 0)
                    continue;

                ASN1 elm = Add(new ASN1(aTag, aValue));

                if ((aTag[0] & 0x20) == 0x20)
                {
                    int nConstructedPos = anPos;
                    elm.Decode(asn1, ref nConstructedPos, nConstructedPos + nLength);
                }
                anPos += nLength; // Value length
            }
        }

        // TLV : Tag - Length - Value
        protected void DecodeTLV(byte[] asn1, ref int pos, out byte[] tag, out int length, out byte[] content)
        {
            int nTagLength = 0;

            // Check for multi byte tags
            if ((asn1[pos + nTagLength++] & 0x1f) == 0x1f)
            {
                // Tag number is encoded in the following bytes as a sequence of seven bit bytes
                // The high bit of these bytes is used as a flag to indicate whether there's more tag available
                while ((asn1[pos + nTagLength++] & 0x80) == 0x80)
                {
                }
            }

            tag = new byte[nTagLength];
            Buffer.BlockCopy(asn1, pos, tag, 0, nTagLength);
            pos += nTagLength;
            length = asn1[pos++];

            // Special case where L contains the Length of the Length + 0x80
            if ((length & 0x80) == 0x80)
            {
                int nLengthLen = length & 0x7F;
                length = 0;
                for (int i = 0; i < nLengthLen; i++)
                    length = length * 256 + asn1[pos++];
            }

            content = new byte[length];
            Buffer.BlockCopy(asn1, pos, content, 0, length);
        }

        protected void DecodeTLV(byte[] asn1, ref int pos, out ushort tag, out int length, out byte[] content)
        {
            tag = asn1[pos++];

            // Check for 2 byte tags
            switch (tag)
            {
                case 0x5f:
                case 0x9f:
                case 0xbf:
                    tag <<= 8;
                    tag |= asn1[pos++];
                    break;
            }

            length = asn1[pos++];

            // Special case where L contains the Length of the Length + 0x80
            if ((length & 0x80) == 0x80)
            {
                int nLengthLen = length & 0x7F;
                length = 0;
                for (int i = 0; i < nLengthLen; i++)
                    length = length * 256 + asn1[pos++];
            }

            content = new byte[length];
            Buffer.BlockCopy(asn1, pos, content, 0, length);
        }

        public ASN1 this[int index]
        {
            get
            {
                try
                {
                    if ((elist == null) || (index >= elist.Count))
                        return null;
                    return (ASN1)elist[index];
                }
                catch (ArgumentOutOfRangeException)
                {
                    return null;
                }
            }
        }

        public ASN1 Element(int index, byte[] aTag)
        {
            try
            {
                if ((elist == null) || (index >= elist.Count))
                    return null;

                ASN1 elm = (ASN1)elist[index];
                if (elm.Tag == aTag)
                    return elm;
                else
                    return null;
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        public override string ToString()
        {
            StringBuilder hexLine = new StringBuilder();

            // Add tag
            hexLine.AppendFormat("Tag: ");
            for (int i = 0; i < Tag.Length; i++)
            {
                hexLine.AppendFormat("{0} ", Tag[i].ToString("X2"));
                if ((i + 1) % 16 == 0)
                    hexLine.AppendFormat(Environment.NewLine);
            }

            // Add length
            hexLine.AppendFormat("Length: {0} {1}", Value.Length, Environment.NewLine);

            // Add value
            hexLine.Append("Value: ");
            hexLine.Append(Environment.NewLine);
            for (int i = 0; i < Value.Length; i++)
            {
                hexLine.AppendFormat("{0}", Value[i].ToString("X2"));
                if ((i + 1) % 16 == 0)
                    hexLine.AppendFormat(Environment.NewLine);
            }
            return hexLine.ToString();
        }

        public void SaveToFile(string filename)
        {
            if (filename == null)
                throw new ArgumentNullException("filename");

            using (FileStream fs = File.OpenWrite(filename))
            {
                byte[] data = GetBytes();
                fs.Write(data, 0, data.Length);
                fs.Flush();
                fs.Close();
            }
        }

        public IEnumerator GetEnumerator()
        {
            return elist.GetEnumerator();
        }

        public ASN1 Find(byte[] tag)
        {
            bool found = false;
            return Find(this, tag, ref found);
        }

        protected ASN1 Find(ASN1 asn, byte[] tag, ref bool found)
        {
            ASN1 result = null;

            if(CompareArray(asn.Tag, tag))
            {
                found = true;
                return asn;
            }

            if (asn.Count > 0)
            {
                foreach (ASN1 asn1 in asn.elist)
                {
                    result = Find(asn1, tag, ref found);

                    if (found)
                        break;
                }
            }

            return result;
        }

        public ASN1 Find(byte tag)
        {
            return Find(new byte[] { tag });
        }
    }
}
