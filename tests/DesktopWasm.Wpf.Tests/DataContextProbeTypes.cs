using System.ComponentModel;
using DesktopWasm.Wpf;

namespace DataContextDupeA
{
    internal sealed class DupeViewModel
    {
    }
}

namespace DataContextDupeB
{
    internal sealed class DupeViewModel
    {
    }
}

namespace BootProbe
{
    /// <summary>View-model stand-in (plain CLR object, like the converted MainWindowViewModel).</summary>
    public sealed class ProbeViewModel : INotifyPropertyChanged
    {
        private string _name = "Ada";

        public string Name
        {
            get => _name;
            set
            {
                if (_name == value) return;
                _name = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>Stand-in whose XAML declares its DataContext as a local view-model.</summary>
    public sealed class DataContextMain : Window
    {
        public DataContextMain() => InitializeComponent();

        public void InitializeComponent() =>
            Application.LoadComponent(this, new Uri("Boot/DataContextWindow.xaml", UriKind.Relative));
    }

    /// <summary>Gallery MainWindow shape: codebehind assigns DataContext
    /// before InitializeComponent and the XAML declares none.</summary>
    public sealed class PreSetDataContextMain : Window
    {
        public PreSetDataContextMain()
        {
            DataContext = this;
            InitializeComponent();
        }

        public void InitializeComponent() =>
            Application.LoadComponent(this, new Uri("Boot/PreSetWindow.xaml", UriKind.Relative));
    }
}
