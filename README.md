This is JBTelegramBot Back part on .Net C#. The Front part writed on Java locates here: https://github.com/prhobb/JBTgBotJava8/

Back part connects to Front via SSL.
Don't forget to place Client and Server Certificates in Windows key store

Using:
1. Create JBTlsClient
   jBTlsClient = new JBTlsClient(SERVER_FQDN, SERVER_PORT, this);
2. Start jBTlsClient.Start();
3. Make a JBTlsClientListener.

When JBTlsClient recieves massage it calls JBTlsClientListener.OnSslReceive(byte[] buffer) method
To send message use JBTlsClient.Send(byte[] message, MessageType messageType) method.
MessageType should be DATA. KEEP_ALIVE using internally if this enabled on server side.

You can see using example in Controller class.

Btw, this can be used as simple SSLClient without Telegram. You can Serialize and Deserialize anything, just give byte array as send buffer to JBTlsClient.Send. But it should be connected to JBTlsServer from Front part.
