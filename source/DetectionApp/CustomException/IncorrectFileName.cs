using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DetectionApp.CustomException
{
    class IncorrectFileName : ApplicationException
    {
        public IncorrectFileName(string message):base(message)
        { }
        public override string Message
        {
            get
            {
                return "Incorrect file format, usage: [channel]-filename.[image format] - " + base.Message;
            }
        }

    }
}
