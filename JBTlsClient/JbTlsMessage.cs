
namespace JbTlsClientWinForms.Services.JBTlsClient
{
    
    public class JbTlsMessage
    {
        int messageNumber;
        DateTime date;
        byte[] buffer;

        public JbTlsMessage(int messageNumber, DateTime date, byte[] buffer)
        {
            this.messageNumber = messageNumber;
            this.date = date;
            this.buffer = buffer;
        }

        public DateTime Date { get => date; }
        public int MessageNumber { get => messageNumber;  }
        public byte[] Buffer { get => buffer;  }
    }
}
