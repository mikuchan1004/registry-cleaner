using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Registry_Cleaner
{
    public partial class MainWindow : Window
    {
        private List<RegistryItem> _masterList = [];

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void BtnScan_Click(object sender, RoutedEventArgs e)
        {
            PbScanning.Visibility = Visibility.Visible;
            BtnScan.IsEnabled = false;
            TbStatusMessage.Text = "스캔 중...";
            TbStatusMessage.Foreground = Brushes.DarkBlue;

            _masterList = [];

            await Task.Run(() =>
            {
                _masterList.AddRange(Scanner.ScanStartupItems());
                _masterList.AddRange(Scanner.ScanUnusedSoftware());
                _masterList.AddRange(Scanner.ScanMuiCache());
                _masterList.AddRange(Scanner.ScanFileExtensions());
            });

            ApplyFilter();

            TbTotalCount.Text = _masterList.Count.ToString();
            TbStatusMessage.Text = _masterList.Count > 0 ? $"스캔 완료" : "깨끗합니다!";
            TbStatusMessage.Foreground = _masterList.Count > 0 ? Brushes.Crimson : Brushes.Green;

            PbScanning.Visibility = Visibility.Collapsed;
            BtnScan.IsEnabled = true;
        }

        private void BtnClean_Click(object sender, RoutedEventArgs e)
        {
            if (DgResults.ItemsSource is not List<RegistryItem> currentItems) return;

            var targetItems = currentItems.Where(x => x.IsChecked).ToList();
            if (targetItems.Count == 0)
            {
                TbStatusMessage.Text = "정리할 항목을 선택하세요.";
                return;
            }

            if (MessageBox.Show($"{targetItems.Count}개를 삭제하시겠습니까?", "확인",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                foreach (var item in targetItems)
                {
                    Scanner.DeleteRegistryItem(item);
                }
                TbStatusMessage.Text = $"{targetItems.Count}개 정리 완료";
                BtnScan_Click(null!, null!);
            }
        }

        private void CbFilter_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            if (_masterList == null || DgResults == null) return;
            string filter = (CbFilter.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content.ToString() ?? "전체 리스트";

            DgResults.ItemsSource = filter == "전체 리스트"
                ? [.. _masterList]
                : _masterList.Where(x => x.Category == filter).ToList();

            UpdateSelectedCount();
        }

        private void UpdateSelectedCount()
        {
            if (DgResults.ItemsSource is List<RegistryItem> items)
                TbSelectedCount.Text = items.Count(x => x.IsChecked).ToString();
        }
    }

    // [수정 핵심] 컨버터를 MainWindow 클래스 밖으로 뺐습니다. 그래야 XAML에서 인식이 됩니다.
    public class CategoryToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string category = value?.ToString() ?? "";
            return category switch
            {
                "시작 프로그램" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E3F2FD")), // 연파랑
                "소프트웨어 잔상" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1F8E9")), // 연초록
                "MUI 캐시" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF3E0")), // 연주황
                "확장자 연결" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F3E5F5")), // 연보라
                _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F0F0F0")) // 기본 회색
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}