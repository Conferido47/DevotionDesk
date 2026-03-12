using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;

namespace DevotionDesk
{
    public partial class SplashWindow : Window
    {
        private readonly TaskCompletionSource _doneTcs = new();

        public SplashWindow()
        {
            InitializeComponent();
        }

        public Task WaitForDoneAsync() => _doneTcs.Task;

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (Resources["IntroStoryboard"] is not Storyboard sb)
            {
                _doneTcs.TrySetResult();
                return;
            }

            sb.Completed += (_, _) => _doneTcs.TrySetResult();
            sb.Begin();
        }

        protected override void OnClosed(EventArgs e)
        {
            _doneTcs.TrySetResult();
            base.OnClosed(e);
        }
    }
}
