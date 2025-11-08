using System.Text;
using System.Windows;
using System.Windows.Input;
using influx2Exporter.ViewModels; // added

namespace influx2Exporter.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            // Set DataContext so bindings (Host, Token, ConnectCommand, etc.) work
            DataContext = new MainViewModel();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        }
    }
}