

namespace JbTlsClientWinForms.Services.JBTlsClient
{
    public interface JBTlsClientListener
    {
        public void OnSslReceive(byte[] buffer);
    }
}
