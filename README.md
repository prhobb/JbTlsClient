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

You can look for using example in Controller class.


There can be only one Backend connected to JBTelegramBotFront. If new Backend connects old Backend will be disconnected.

Otpfile and Database will be created with first run.

You can add OTP to Otpfile mannualy or via JbTgAuthentication.AddOtp(String otp) method.

Keepalive for Backend enabled by default. You can mange it with JbTlsClientSocket.setKeepalive(boolean keepalive) method.
   
