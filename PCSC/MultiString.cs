using System;
using System.Collections.Generic;
using System.Text;
using System.Collections;

namespace PCSC
{
    public class MultiString : IEnumerable
    {
        private char[] multiString;

        public MultiString(byte[] array)
        {
            ASCIIEncoding encoding = new ASCIIEncoding();
            multiString = encoding.GetChars(array);
        }

        public MultiString(char[] array)
        {
            multiString = array;
        }

        public MultiString(string multiString)
        {
            this.multiString = multiString.ToCharArray();
        }

        public string[] ToArray()
        {
            List<string> stringList = new List<string>(); 
            int i = 0; 
            
            while (i < multiString.Length)
            { 
                int j = i; 
                
                if (multiString[j++] == '\0') 
                    break; 
                
                while (j < multiString.Length) 
                { 
                    if (multiString[j++] == '\0') 
                    { 
                        stringList.Add(new string(multiString, i, j - i - 1)); 
                        i = j; 
                        break; 
                    } 
                } 
            } 
            
            return stringList.ToArray(); 
        }

        public override string ToString()
        {
            ASCIIEncoding encoding = new ASCIIEncoding();
            return encoding.GetString(encoding.GetBytes(multiString));
        }

        public int Count
        {
            get
            {
                List<string> stringList = new List<string>();
                int i = 0;

                while (i < multiString.Length)
                {
                    int j = i;

                    if (multiString[j++] == '\0')
                        break;

                    while (j < multiString.Length)
                    {
                        if (multiString[j++] == '\0')
                        {
                            stringList.Add(new string(multiString, i, j - i - 1));
                            i = j;
                            break;
                        }
                    }
                }

                return stringList.Count;
            }
        }

        public IEnumerator GetEnumerator()
        {
            return this.ToArray().GetEnumerator();
        }
    }
}
