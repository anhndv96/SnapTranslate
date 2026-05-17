using System;
using System.Windows;
using SnapTranslate.Models;

namespace SnapTranslate
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            DeepSeekApiKeyTextBox.Text = AppSettings.Current.DeepSeekApiKey;
            LocalLlmUrlTextBox.Text = AppSettings.Current.LocalLlmUrl;
            StartWithWindowsCheckBox.IsChecked = AppSettings.Current.StartWithWindows;
            this.MouseLeftButtonDown += OnHeaderDrag;
        }

        private void OnHeaderDrag(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
                    this.DragMove();
            }
            catch (InvalidOperationException)
            {
                // Prevent crash if drag state is invalid
            }
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            AppSettings.Current.DeepSeekApiKey = DeepSeekApiKeyTextBox.Text.Trim();
            AppSettings.Current.LocalLlmUrl = LocalLlmUrlTextBox.Text.Trim();
            AppSettings.Current.StartWithWindows = StartWithWindowsCheckBox.IsChecked == true;
            
            AppSettings.Save();
            AppSettings.ApplyStartupSetting();
            
            this.Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
