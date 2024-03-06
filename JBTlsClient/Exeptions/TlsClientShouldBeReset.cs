using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JbTlsClientWinForms.Exeptions
{
    
    class TlsClientShouldBeReset : Exception
    {
        public TlsClientShouldBeReset(String error) : base(error) { }
  
    }
}
