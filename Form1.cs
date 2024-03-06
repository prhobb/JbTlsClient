namespace JbTlsClientWinForms
{
    public partial class Form1 : Form
    {
        private static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();
        Controller controller;
        public Form1()
        {
            InitializeComponent();
            controller = new Controller();
        }
    }
}