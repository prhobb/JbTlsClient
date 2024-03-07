using NLog;
using NLog.Targets;
using JbTlsClientWinForms.Accesories;
using JbTlsClientWinForms.Services.JBTlsClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JbTlsClientWinForms
{

    internal class Controller : JBTlsClientListener
    {
        private static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();
        JBTlsClient jBTlsClient;
        private const int SERVER_PORT = 4666;
        private const String SERVER_FQDN = "JBTgBotJava8.example.com";


        public Controller() {
            logger.Info("Started");
            jBTlsClient = new JBTlsClient(SERVER_FQDN, SERVER_PORT, this);
            jBTlsClient.Start();
        }


        public void OnSslReceive(byte[] buffer)
        {
            TelegramMessage telegramMessage = TelegramMessage.Deserialize(buffer);
            if (telegramMessage != null && telegramMessage.getType == TelegramMessage.Type.STRING_OBJECT)
            {
                logger.Debug(telegramMessage.ToString());
                telegramMessage.Text = "Received: " + telegramMessage.Text;

                jBTlsClient.Send(telegramMessage.Serialize(), JBTlsClient.MessageType.DATA);
            }
            else
                logger.Error("Recived wrong TelegramMessage");
        }
    }
}
