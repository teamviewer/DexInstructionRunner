using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DexInstructionRunner.Models
{
    public sealed class RunResultFilterRow : INotifyPropertyChanged
    {
        private string _column = string.Empty;
        private string _operatorText = string.Empty;
        private string _value = string.Empty;
        private string _dataType = "string";

        public ObservableCollection<string> AvailableOperators { get; } = new ObservableCollection<string>();

        public string Column
        {
            get => _column;
            set
            {
                if (_column == value) return;
                _column = value ?? string.Empty;
                OnPropertyChanged();
            }
        }

        public string Operator
        {
            get => _operatorText;
            set
            {
                if (_operatorText == value) return;
                _operatorText = value ?? string.Empty;
                OnPropertyChanged();
            }
        }

        public string Value
        {
            get => _value;
            set
            {
                if (_value == value) return;
                _value = value ?? string.Empty;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Normalized data type for ResultsFilter (e.g. "string" or "number").
        /// </summary>
        public string DataType
        {
            get => _dataType;
            set
            {
                if (_dataType == value) return;
                _dataType = string.IsNullOrWhiteSpace(value) ? "string" : value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
