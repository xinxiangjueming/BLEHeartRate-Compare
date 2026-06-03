using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HeartRateMonitor.ViewModels
{
    /// <summary>
    /// ViewModel 基类，提供 INotifyPropertyChanged 实现和辅助方法。
    /// </summary>
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName ?? string.Empty));

        /// <summary>
        /// 设置属性值并在值变化时触发 PropertyChanged。
        /// </summary>
        /// <returns>如果值发生了变化则返回 true</returns>
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
