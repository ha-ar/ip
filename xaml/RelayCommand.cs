using System.Windows.Input;

namespace ip.xaml
{
    public class RelayCommand(Action<object> action) : ICommand
    {
        private readonly Action<object> action = action;

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            action(parameter!);
        }
    }


}
