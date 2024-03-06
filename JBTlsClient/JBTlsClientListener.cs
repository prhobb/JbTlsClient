

namespace JbTlsClientWinForms.Services.JBTlsClient
{
    internal interface JBTlsClientListener
    {
        public void OnSslReceive(byte[] buffer);
    }
}
