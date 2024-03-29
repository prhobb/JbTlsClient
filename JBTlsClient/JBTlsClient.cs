﻿using JbTlsClientWinForms.Exeptions;
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace JbTlsClientWinForms.Services.JBTlsClient
{
    public class JBTlsClient
    {
        public enum MessageType : byte
        {
            NONE=0,
            DATA = 1,
            KEEP_ALIVE = 2,
            ACK = 3
        }
        const int PAYLOAD_SIZE_POSITION = 0;
        const int HASH_POSITION = 4;
        const int MESSAGETYPE_POSITION = 36;
        const int MESSAGENUMBER_POSITION = 37;
        const int PAYLOAD_POSITION = 41;
        const int READ_TIMEOUT = 70000;
        const int MAX_PENDINGMESSAGE_AGE_SEC = 180;

        private const int bufferSize = 2048;
        private static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();
        static object locker = new object();
        private static Hashtable certificateErrors = new Hashtable();

        JBTlsClientListener listener;
        string serverAddress;
        int serverPort;
        TcpClient tcpClient;
        SslStream sslStream;
        bool isSslStreamListening;
        CancellationTokenSource cts_sslStreamListenThread;
        Dictionary<int, JbTlsMessage> pendingMessages;
        private int messageNumber;

        object sendLocker;

        public JBTlsClient(string serverAddress, int serverPort, JBTlsClientListener listener)
        {
            isSslStreamListening = false;
            this.serverAddress = serverAddress;
            this.serverPort = serverPort;
            this.listener = listener;
            sendLocker = new object();
            messageNumber = 0;
            pendingMessages = new Dictionary<int, JbTlsMessage>();
        }


        // The following method is invoked by the RemoteCertificateValidationDelegate.
        public static bool ValidateServerCertificate(
              object sender,
              X509Certificate certificate,
              X509Chain chain,
              SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors == SslPolicyErrors.None)
                return true;

            logger.Warn("Certificate error: {0}", sslPolicyErrors);

            // Do not allow this client to communicate with unauthenticated servers.
            return false;
        }
        public static X509Certificate SelectLocalCertificate(
            object sender,
            string targetHost,
            X509CertificateCollection localCertificates,
            X509Certificate remoteCertificate,
            string[] acceptableIssuers)
        {
            return GetCertificateFromStore("evebot.aprohorov.ru");
        }

        private static X509Certificate2 GetCertificateFromStore(string certName)
        {

            // Get the certificate store for the current user.
            X509Store store = new X509Store(StoreLocation.CurrentUser);
            try
            {
                store.Open(OpenFlags.ReadOnly);

                // Place all certificates in an X509Certificate2Collection object.
                X509Certificate2Collection certCollection = store.Certificates;
                // If using a certificate with a trusted root you do not need to FindByTimeValid, instead:
                // currentCerts.Find(X509FindType.FindBySubjectDistinguishedName, certName, true);
                X509Certificate2Collection currentCerts = certCollection.Find(X509FindType.FindByTimeValid, DateTime.Now, false);
                X509Certificate2Collection signingCert = currentCerts.Find(X509FindType.FindBySubjectName, certName, false);
                if (signingCert.Count == 0)
                    return null;
                // Return the first certificate in the collection, has the right name and is current.
                return signingCert[0];
            }
            finally
            {
                store.Close();
            }
        }

        
        public bool Start()
        {
            lock (locker)
            {
                if ((tcpClient == null || !tcpClient.Connected) && !isSslStreamListening)
                {
                    return RunClient(serverAddress, serverPort);
                }
                else if (isSslStreamListening)
                    logger.Error("Can't run JbTlsClient. Stream in listening state.");
                else if (tcpClient.Connected)
                    logger.Error("Can't run JbTlsClient. Already connected.");                
            }
            return false;
        }

        public void Stop()
        {
            lock (locker)
            {
                if (cts_sslStreamListenThread != null)
                {
                    cts_sslStreamListenThread.Cancel();
                    CloseTcpClient();
                    do { Thread.Sleep(100); } while (isSslStreamListening);
                    cts_sslStreamListenThread.Dispose();
                }
                else
                    CloseTcpClient();
            }
        }

        private void CloseTcpClient()
        {
            if (sslStream != null)
            {
                try
                {
                    sslStream.Close();
                }
                catch (Exception ex)
                {
                    logger.Error(ex);
                }
                logger.Warn("JbTlsClient stopped");
            }
            if (tcpClient != null && tcpClient.Connected)
            {
                tcpClient.Close();
                logger.Warn("JbTlsClient stopped");
            }
        }

      
        public bool Reconnect()
        {
            Stop();
            return Start();
        }


        private bool RunClient(string serverAddress, int serverPort)
        {
            if (cts_sslStreamListenThread != null)
            {
                try
                {
                    cts_sslStreamListenThread.Cancel();
                    cts_sslStreamListenThread.Dispose();
                }
                catch (ObjectDisposedException) { }
            }

            cts_sslStreamListenThread = new CancellationTokenSource();

            // Create a TCP/IP client socket.
            try
            {
                tcpClient = new TcpClient(serverAddress, serverPort);

                if (tcpClient.Connected)
                {
                    logger.Warn("TcpClient connected.");
                    sslStream = new SslStream(
                        tcpClient.GetStream(),
                        false,
                        new RemoteCertificateValidationCallback(ValidateServerCertificate),
                        new LocalCertificateSelectionCallback(SelectLocalCertificate)
                        );
                    sslStream.ReadTimeout = 8000;

                    // The server name must match the name on the server certificate.
                    try
                    {
                        sslStream.AuthenticateAsClient(serverAddress);
                        // sslStream.ReadTimeout = System.Threading.Timeout.Infinite;
                        sslStream.ReadTimeout = READ_TIMEOUT;
                    }
                    catch (AuthenticationException e)
                    {
                        logger.Warn("Exception: {0}", e.Message);
                        if (e.InnerException != null)
                        {
                            logger.Warn("Inner exception: {0}", e.InnerException.Message);
                        }
                        logger.Warn("Authentication failed - closing the connection.");
                        tcpClient.Close();
                        return false;
                    }
                    catch (IOException ex)
                    {
                        logger.Error("Exception: {0}", ex.Message);
                        tcpClient.Close();
                        return false;
                    }



                    logger.Warn("JbTlsClient started");
                    //Start receive listening
                    Task.Run(() => SslStreamListenThread(cts_sslStreamListenThread.Token));
                    //Resend Pending Messages
                    try
                    {
                        foreach (var pendingmessage in pendingMessages)
                            if ((DateTime.Now - pendingmessage.Value.Date).TotalSeconds < MAX_PENDINGMESSAGE_AGE_SEC)
                            {
                                logger.Info("Resend message. ID: {pendingmessage.Key} Date:{pendingmessage.Value.Date}", pendingmessage.Key, pendingmessage.Value.Date);
                                Send(pendingmessage.Value.Buffer, MessageType.DATA, pendingmessage.Key, false);
                            }
                            else
                            {
                                pendingMessages.Remove(pendingmessage.Key);
                                logger.Info("Removed message. ID: {pendingmessage.Key} Date:{pendingmessage.Value.Date}", pendingmessage.Key, pendingmessage.Value.Date);
                            }
                    }
                    catch (TlsClientShouldBeReset ex)
                    {
                        logger.Error(ex);
                        Reconnect();
                    }



                    return true;

                }
            }
            catch (SocketException ex)
            {
                logger.Error(ex);
            }
            return false;

        }

        private void SslStreamListenThread(CancellationToken cancellationToken)
        {
            isSslStreamListening = true;
            logger.Warn("SslStreamListenThread started");
            int receivedByte;
            try
            {
                while (isSslStreamListening && !cancellationToken.IsCancellationRequested)
                {

                    byte[] sizeBuffer = new byte[4];
                    byte[] hashBuffer = new byte[32];
                    byte[] messagenumberBuffer = new byte[4];
                    byte[] payloadBuffer = new byte[0];
                    int messagenumber = 0;
                    MessageType messageType = MessageType.NONE;

                    for (int i = 0, paketLength = PAYLOAD_POSITION; i < paketLength; i++)
                    {
                        if (sslStream != null && sslStream.CanRead)
                        {
                            receivedByte = sslStream.ReadByte();
                            if (receivedByte < 0)
                                throw new TlsClientShouldBeReset("Unexpected sslStream end");
                            //FirstByte
                            if (i == 0)
                            {
                                if (receivedByte < 0) //First 4 bytes - size. This is int and should be positive, so first byte MUST > 0
                                    throw new TlsClientShouldBeReset("FirstByte should be positive");
                            }

                            //Read size
                            if (i < PAYLOAD_SIZE_POSITION + sizeBuffer.Length && i >= PAYLOAD_SIZE_POSITION)
                            {
                                sizeBuffer[i - PAYLOAD_SIZE_POSITION] = (byte)receivedByte;
                                if (i == PAYLOAD_SIZE_POSITION + sizeBuffer.Length - 1)
                                {

                                    if (BitConverter.IsLittleEndian)
                                        Array.Reverse(sizeBuffer);
                                    int size = BitConverter.ToInt32(sizeBuffer, 0);
                                    if (size < 0) throw new TlsClientShouldBeReset("Buffer size should be positive");
                                    payloadBuffer = new byte[size];
                                    paketLength = PAYLOAD_POSITION + size;
                                }
                            }
                            //Read hash
                            else if (i < HASH_POSITION + hashBuffer.Length && i >= HASH_POSITION)
                            {
                                hashBuffer[i - HASH_POSITION] = (byte)receivedByte;
                            }
                            //Read MessageType
                            else if (i == MESSAGETYPE_POSITION)
                            {
                                messageType = (MessageType)(byte)receivedByte;
                            }
                            //Read messagenumber
                            else if (i < MESSAGENUMBER_POSITION + messagenumberBuffer.Length && i >= MESSAGENUMBER_POSITION)
                            {
                                messagenumberBuffer[i - MESSAGENUMBER_POSITION] = (byte)receivedByte;
                                if (i == MESSAGENUMBER_POSITION + sizeBuffer.Length - 1)
                                {
                                    if (BitConverter.IsLittleEndian)
                                        Array.Reverse(messagenumberBuffer);
                                    messagenumber = BitConverter.ToInt32(messagenumberBuffer, 0);
                                    if (messagenumber < 0) throw new TlsClientShouldBeReset("Messagenumber should be positive");
                                }
                            }

                            //Read payload
                            else if (i < PAYLOAD_POSITION + payloadBuffer.Length && i >= PAYLOAD_POSITION)
                            {
                                payloadBuffer[i - PAYLOAD_POSITION] = (byte)receivedByte;
                            }

                            //Checking after full message received
                            if (i == paketLength - 1)
                            {

                                switch (messageType)
                                {
                                    case MessageType.DATA:
                                        //Check received data hash
                                        if (!checkHash(payloadBuffer, hashBuffer))
                                            throw new TlsClientShouldBeReset("Wrong hash.");
                                        //Run listener if all is fine
                                        listener.OnSslReceive(payloadBuffer);
                                        Send(null, MessageType.ACK, messagenumber, true);
                                        break;
                                    case MessageType.KEEP_ALIVE:
                                        Send(null, MessageType.KEEP_ALIVE, 0, true);
                                        break;
                                    case MessageType.ACK:
                                        OnAckReceive(messagenumber);
                                        break;
                                }
                            }

                        }
                        else
                            throw new TlsClientShouldBeReset("sslStream is null or can't be read");
                    }


                    /*
                    if (sslStream != null && sslStream.CanRead)
                    {
                        //Read new Packet
                        receivedByte = sslStream.ReadByte();
                        if (receivedByte < 0) //First 4 bytes - size. This is int and should be positive, so first byte MUST > 0
                            throw new TlsClientShouldBeReset("FirstByte should be positive");

                        byte[] sizeBuffer = new byte[4];
                        byte[] hashBuffer = new byte[32];
                        //Read size
                        sizeBuffer[0] = (byte)receivedByte;
                        for (int i = 1; i < sizeBuffer.Length; i++)
                        {
                            receivedByte = sslStream.ReadByte();
                            if (receivedByte < 0)
                                throw new TlsClientShouldBeReset("Unexpected sslStream end");
                            sizeBuffer[i] = (byte)receivedByte;
                        }
                        if (BitConverter.IsLittleEndian)
                            Array.Reverse(sizeBuffer);
                        int size = BitConverter.ToInt32(sizeBuffer, 0);

                        //(int)new BigInteger(sizeBuffer);
                        if (size < 0) throw new TlsClientShouldBeReset("Buffer size should be positive");
                        byte[] buffer = new byte[size];

                        //Read hash
                        for (int i = 0; i < hashBuffer.Length; i++)
                        {
                            receivedByte = sslStream.ReadByte();
                            if (receivedByte < 0)
                                throw new TlsClientShouldBeReset("Unexpected sslStream end");
                            hashBuffer[i] = (byte)receivedByte;
                        }

                        //ReadMessageType


                        //Read Data
                        for (int i = 0; i < buffer.Length; i++)
                        {
                            receivedByte = sslStream.ReadByte();
                            if (receivedByte < 0)
                                throw new TlsClientShouldBeReset("Unexpected sslStream end");
                            buffer[i] = (byte)receivedByte;
                        }

                        //Check received data hash
                        if (!checkHash(buffer, hashBuffer))
                            throw new TlsClientShouldBeReset("Wrong hash.");
                        //Run listener if all is fine
                        listener.OnSslReceive(buffer);
                    }
                    else
                        throw new TlsClientShouldBeReset("sslStream is null or can't be read");
                    */
                }
            }
            catch (TlsClientShouldBeReset ex)
            {
                logger.Error(ex);
                isSslStreamListening = false;
                if (!cancellationToken.IsCancellationRequested)
                    Reconnect();
            }
            catch (IOException ex)
            {
                logger.Error(ex);
                isSslStreamListening = false;
                if (!cancellationToken.IsCancellationRequested)
                    Reconnect();
            }
            catch (Exception ex)
            {
                logger.Error(ex);
                isSslStreamListening = false;
            }

            isSslStreamListening = false;
            logger.Warn("SslStreamListenThread stopped");
        }
        public void OnAckReceive(int messagenumber)
        {
            pendingMessages.Remove(messagenumber);
        }




        async public void SendAsync(byte[] message)
        {
            if (messageNumber > 214748364)
                messageNumber = 0;
            else
                messageNumber++;
            pendingMessages.Add(messageNumber, new JbTlsMessage(messageNumber, DateTime.Now, message));

            await Task.Run(() => Send(message, MessageType.DATA, messageNumber, false));


        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        /// <param name="messageType"></param>
        /// <param name="messageNumber"></param>
        /// <exception cref="TlsClientShouldBeReset"></exception>
        private void Send(byte[] message, MessageType messageType, int messageNumber, bool throwExeption)
        {
            lock (sendLocker)
            {

                if (message == null && messageType == MessageType.DATA)
                    logger.Error("Can't send null message");
                else
                {
                    if (sslStream == null || !sslStream.CanWrite)
                    {

                        if (throwExeption)
                            throw new TlsClientShouldBeReset("Can't send message. Try to reconnet TlsClient");
                        else
                            logger.Error("Can't send message. Try to reconnet TlsClient");
                    }

                    byte[] buffer = GetSendBuffer(message, messageType, messageNumber);
                    if (buffer != null)
                    {
                        logger.Debug("Send data: {buffer}", buffer);
                        try
                        {
                            sslStream.Write(buffer);
                            sslStream.Flush();
                        }
                        catch (Exception e)
                        {
                            logger.Error("Exception while sending message. Try to reconndect... ex: ", e);
                            if (throwExeption)
                                throw new TlsClientShouldBeReset("Exception while sending message. Try to reconndect...");

                        }
                    }
                    else
                        logger.Error("Can't send null buffer");
                }
            }
        }
        private static byte[] GetSendBuffer(byte[] message, MessageType messageType, int messageNumber)
        {
            //Структура. 0-byte--3byte: Payload size, 4-byte--35byte: Hash, 36byte: MessageType, 37-byte--40byte:messagenumber 41-byte--end: Payload
            if (message == null && messageType != MessageType.DATA)
                message = new byte[1];
            //Calculate Hash
            byte[] hash = getHash256(message);
            if (hash != null)
            {

                //Paylod Length
                byte[] result = new byte[message.Length + PAYLOAD_POSITION];
                logger.Debug("Send data Length: {Length}", message.Length);
                result[0] = (byte)(message.Length >> 24);
                result[1] = (byte)(message.Length >> 16);
                result[2] = (byte)(message.Length >> 8);
                result[3] = (byte)message.Length;

                //Write Hash
                hash.CopyTo(result, HASH_POSITION);

                //Write MessageType
                result[MESSAGETYPE_POSITION] = (byte)messageType;

                //Write messagenumber
                result[MESSAGENUMBER_POSITION] = (byte)(messageNumber >> 24);
                result[MESSAGENUMBER_POSITION + 1] = (byte)(messageNumber >> 16);
                result[MESSAGENUMBER_POSITION + 2] = (byte)(messageNumber >> 8);
                result[MESSAGENUMBER_POSITION + 3] = (byte)messageNumber;


                //Cope payload to result send buffer
                message.CopyTo(result, PAYLOAD_POSITION);

                return result;
            }

            return null;
        }


        public static byte[] getHash256(byte[] message)
        {

            if (message != null)
            {
                using (SHA256 sha256Hash = SHA256.Create())
                {
                    return sha256Hash.ComputeHash(message);
                }
            }
            return null;
        }

        private bool checkHash(byte[] message, byte[] hash)
        {
            //Correct Check
            if (message == null || hash == null || message.Length == 0 || hash.Length != 32)
            {
                logger.Error("Hash function arguments are incorrect");
                return false;
            }

            return hash.SequenceEqual(getHash256(message));
        }



    }

}
