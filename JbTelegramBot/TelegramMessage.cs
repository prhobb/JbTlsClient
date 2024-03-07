using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace JbTlsClientWinForms.Accesories
{
   
    internal class TelegramMessage
    {
        private static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        const int TYPE_POSITION = 0;
        const int CHAT_ID_POSITION = 1;
        const int MESSAGE_POSITION = 9;

        public enum Type : byte
        {
            STRING_OBJECT = 1,
            BITMAP_OBJECT = 2
        }


        TelegramMessage.Type type;
        Bitmap image;
        String text;
        long chatId;

        public Type getType { get => type; }
        public string Text { get => text; set => text = value; }
        public Bitmap Image { get => image; set => image = value; }
        public long ChatId { get => chatId; set => chatId = value; }

        private TelegramMessage() { }

        /// <summary>
        /// Check if null!!!
        /// </summary>
        /// <returns>Gets TelegramMessage or null if smth incorrect</returns>
        public static TelegramMessage GetTelegramMessage(Object obj, long chatId)
        {
            //Obj должен быть либо текстом либо Bitmap
            if (obj == null) return null;
            TelegramMessage telegramMessage=new TelegramMessage();            

            if (obj.GetType() == typeof(string))
            {
                telegramMessage.text = (String)obj;
                telegramMessage.type = TelegramMessage.Type.STRING_OBJECT;
            }
            else if (obj.GetType() == typeof(Bitmap))
            {
                telegramMessage.image= (Bitmap)obj;
                telegramMessage.type = TelegramMessage.Type.BITMAP_OBJECT;
            }
            else
                return null;

            telegramMessage.chatId = chatId;
            return telegramMessage;
        }
        public byte[] Serialize()
        {
            //Structure. 0 - bit: type, 1 - bit--8bit: chatId(can be 0), 10 - bit--end: payload(text or photo)
            byte[] result;
            byte[] temp;
            switch (type)
            {
                case TelegramMessage.Type.STRING_OBJECT:
                    temp = Encoding.UTF8.GetBytes(text);
                    break;
                case TelegramMessage.Type.BITMAP_OBJECT:
                    ImageConverter converter = new ImageConverter();
                    temp = (byte[])converter.ConvertTo(image, typeof(byte[]));
                    break;
                default:
                    temp = new byte[0];
                    break;
            }
            
            result = new byte[temp.Length + MESSAGE_POSITION];

            result[TYPE_POSITION] = (byte)type;
            result[CHAT_ID_POSITION] = (byte)(chatId >> 56);
            result[CHAT_ID_POSITION+1] = (byte)(chatId >> 48);
            result[CHAT_ID_POSITION + 2] = (byte)(chatId >> 40);
            result[CHAT_ID_POSITION + 3] = (byte)(chatId >> 32);
            result[CHAT_ID_POSITION + 4] = (byte)(chatId >> 24);
            result[CHAT_ID_POSITION + 5] = (byte)(chatId >> 16);
            result[CHAT_ID_POSITION + 6] = (byte)(chatId >> 8);
            result[CHAT_ID_POSITION + 7] = (byte) chatId;
            temp.CopyTo(result, MESSAGE_POSITION);

            return result;
        }

        /// <summary>
        /// ОCheck if null!!!
        /// </summary>
        /// <param name="message">Serialized TelegramMessage</param>
        /// <returns>Gets TelegramMessage or null if smth incorrect</returns>
        public static TelegramMessage Deserialize(byte[] message)
        {
            if(message == null || message.Length< MESSAGE_POSITION+1) return null;

            TelegramMessage telegramMessage = new TelegramMessage();

            telegramMessage.type = (Type)message[TYPE_POSITION];
            //Getting chatID
            if (BitConverter.IsLittleEndian)
                Array.Reverse(message, CHAT_ID_POSITION, 8);
            telegramMessage.chatId= BitConverter.ToInt64(message, CHAT_ID_POSITION);

            switch (telegramMessage.type)
            {
                case TelegramMessage.Type.STRING_OBJECT:
                    Encoding encoder = Encoding.UTF8;
                    telegramMessage.text = encoder.GetString(message, MESSAGE_POSITION, message.Length - MESSAGE_POSITION);
                    break;
                case TelegramMessage.Type.BITMAP_OBJECT:
                    byte[] image = new byte[message.Length - MESSAGE_POSITION];
                    message.CopyTo(image, MESSAGE_POSITION);
                    using (var ms = new MemoryStream(image))
                    {
                        try
                        {
                            telegramMessage.image = new Bitmap(ms);
                        }
                        catch(ArgumentException ex)
                        {
                            logger.Error(ex);
                            return null;
                        }
                    }
                    break;
                default:
                    return null;
            }

            return telegramMessage;
        }

        public override string ToString()
        {
            if (text != "")
                return text;
            else if (image != null)
                return "Photo present";
            return "Incorrect TelegramMessage";
        }
    }
}
