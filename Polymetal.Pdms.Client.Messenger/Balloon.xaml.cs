using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace Polymetal.Pdms.Client.Messenger
{
    /// <summary>
    /// Логика взаимодействия для Balloon.xaml
    /// </summary>
    public partial class Balloon : INotifyPropertyChanged
    {
        int left;
        int top;
        DependencyProperty prop;
        int end;
        public Balloon()
        {
            InitializeComponent();
            var tpos = new TrayPosition();
            tpos.GetXy((int)Width, (int)Height, out top, out left, out prop, out end);
            Top = top;
            Left = left;
        }

        private string _balloonTitle = string.Empty;
        private string _mdbName = string.Empty;
        private string _notificationText = string.Empty;
        private string _projectNameText = string.Empty;
        private BitmapImage _imageSource;

        public string BalloonText
        {
            get { return _balloonTitle; }
            set { _balloonTitle = value; }
        }

        public string MdbName
        {
            get { return _mdbName; }
            set { _mdbName = value; }
        }

        public string NotificationText
        {
            get { return _notificationText; }
            set { _notificationText = value; }
        }

        public string ProjectName
        {
            get { return _projectNameText; }
            set { _projectNameText = value; }
        }

        public BitmapImage ImageSource
        {
            get { return _imageSource; }
            set { _imageSource =value; }
        }

        private void imgClose_MouseDown(object sender, MouseButtonEventArgs e)
        {
            Close();
        }

        public void HideWindow()
        {
            var anim = new DoubleAnimation(end + Height, TimeSpan.FromMilliseconds(300));
            anim.Completed += anim_Completed;
            ApplyAnimationClock(prop, anim.CreateClock());
        }

        void anim_Completed(object sender, EventArgs e)
        {
            Close();
        }

        private void Balloon_OnLoaded(object sender, RoutedEventArgs e)
        {
            ApplyAnimationClock(prop, new DoubleAnimation(end, TimeSpan.FromMilliseconds(1000)).CreateClock());
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public void RaisePropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    internal class TrayPosition
    {
        public enum Tpos
        {
            Top,
            Left,
            Bottom,
            Right
        }

        public Tpos Pos;
        private int _top;
        private int _left;
        private DependencyProperty _prop;
        private int _end;

        readonly int _screenWidth = (int)SystemParameters.PrimaryScreenWidth;
        readonly int _screenHeight = (int)SystemParameters.PrimaryScreenHeight;
        readonly int _workAreaWidth = (int)SystemParameters.WorkArea.Width;
        readonly int _workAreaHeight = (int)SystemParameters.WorkArea.Height;

        public void GetXy(int width, int height, out int top, out int left, out DependencyProperty prop, out int end)
        {

            switch (Pos)
            {
                case Tpos.Top:
                    _left = _screenWidth - width;
                    _top = (int)SystemParameters.WorkArea.Top - height;
                    _prop = Window.TopProperty;
                    _end = (int)SystemParameters.WorkArea.Top;
                    break;
                case Tpos.Left:
                    _left = (int)SystemParameters.WorkArea.Left - width;
                    _top = _screenHeight - height;
                    _prop = Window.LeftProperty;
                    _end = (int)SystemParameters.WorkArea.Left;
                    break;
                case Tpos.Right:
                    _left = _workAreaWidth;
                    _top = _screenHeight - height;
                    _prop = Window.LeftProperty;
                    _end = _workAreaWidth - width;
                    break;
                default:
                    _left = _screenWidth - width;
                    _top = _workAreaHeight;
                    _prop = Window.TopProperty;
                    _end = _workAreaHeight - height;
                    break;
            }

            left = _left;
            top = _top;
            prop = _prop;
            end = _end;
        }

        public TrayPosition()
        {
            if (SystemParameters.WorkArea.Top > 0)
            {
                Pos = Tpos.Top;
            }
            else if (SystemParameters.WorkArea.Left > 0)
            {
                Pos = Tpos.Left;
            }
            else if (_screenWidth > _workAreaWidth)
            {
                Pos = Tpos.Right;
            }
            else
            {
                Pos = Tpos.Bottom;
            }
        }
    }
}
