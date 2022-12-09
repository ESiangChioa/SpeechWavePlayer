using System;
using System.Windows;
using System.Windows.Controls;
using NAudio.Wave;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System.Reflection;
using PropertyChanged;


namespace SpeechWavePlayer
{
    /// <summary>
    /// SpeechWavePlayer.xaml 的交互逻辑
    /// </summary>
    [AddINotifyPropertyChangedInterface]
    public partial class WavePlayer : UserControl
    {
        public string FileName { get; set; }
        public string CurrentTime { get; set; }

        public string TotalTime { get; set; }

        private AudioFileReader audioFileReader;
        private WaveOut waveOutDevice;

        private double currentMillseconds;

        private string currentFilePath;


        private byte[] speechbBytes;

        public int[] ZoomValues { get; set; }= { 100, 200, 400, 800, 1600,3200};
        public int ZoomIndex { get; set; } = 0;
        private System.Timers.Timer playTimer;
        public WavePlayer()
        {
            InitializeComponent();
            this.DataContext = this;
            playTimer = new System.Timers.Timer(100);
            playTimer.Elapsed += PlayTimer_Elapsed;
        }

        /// <summary>
        /// 载入原始音频
        /// </summary>
        /// <param name="speech"></param>
        public async void LoadSpeech(string speechPath)
        {
            CurrentTime = "00:00:00";
            currentFilePath = speechPath;
            FileName = Path.GetFileName(currentFilePath);
            audioFileReader = new AudioFileReader(currentFilePath);
            speechbBytes = new byte[audioFileReader.Length];
            audioFileReader.Read(speechbBytes, 0, speechbBytes.Length);
            TimeSpan ts = TimeSpan.FromMilliseconds(audioFileReader.TotalTime.TotalMilliseconds);
            TotalTime = ts.Hours>0?$"{ts.Hours:00}:{ts.Minutes:00}:{ts.Seconds:00}":$"{ts.Minutes:00}:{ts.Seconds:00}";
            this.SpeechWaveDisplay.SetSpeechData(speechbBytes);
            this.SpeechWaveDisplay.OnUpdateZoomEvent += SpeechWaveDisplay_OnUpdateZoomEvent;                      
        }

        private void SpeechWaveDisplay_OnUpdateZoomEvent(int zoom)
        {
            int zoomValue = zoom * 100;
            ZoomIndex = ZoomValues.ToList().IndexOf(zoomValue);
        }

        /// <summary>
        /// 获取当前选区采样点位置
        /// </summary>
        /// <returns></returns>
        public Tuple<int,int> GetSelectionSampleSegment()
        {
            return SpeechWaveDisplay.GetSelectionSegment();
        }

        /// <summary>
        /// 获取当前采样点位置
        /// </summary>
        /// <returns></returns>
        public int GetCurrentSampleIndex()
        {
            return SpeechWaveDisplay.GetCurrentSampleIndex();
        }

        public void Release()
        {
            this.StopPlay();
            playTimer.Close();
            audioFileReader?.Close();
        }
        private void BtnPlay_OnClick(object sender, RoutedEventArgs e)
        {
            var sampleIndex = SpeechWaveDisplay.GetCurrentSampleIndex();
            currentMillseconds = sampleIndex>8?sampleIndex / 8:0;
            StartPlay();

        }

        public void StartPlay()
        {
            this.StopPlay();
            waveOutDevice = new WaveOut();
            audioFileReader.CurrentTime = TimeSpan.FromMilliseconds(currentMillseconds);
            waveOutDevice.Init(audioFileReader);
            waveOutDevice.Volume = 1;
            waveOutDevice.PlaybackStopped += WaveOutDevice_PlaybackStopped;
            waveOutDevice.Play();
            playTimer.Start();
        }

        private void WaveOutDevice_PlaybackStopped(object? sender, StoppedEventArgs e)
        {
            this.SpeechWaveDisplay.SetCurrentSample(0);
            this.StopPlay();
        }
        private void PlayTimer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            if (waveOutDevice.PlaybackState == PlaybackState.Playing)
            {
                var currentTime = audioFileReader?.CurrentTime ?? TimeSpan.Zero; // 当前时间
                CurrentTime = currentTime.Hours > 0 ? $"{currentTime.Hours:00}:{currentTime.Minutes:00}:{currentTime.Seconds:00}" : $"{currentTime.Minutes:00}:{currentTime.Seconds:00}";
                this.Dispatcher.BeginInvoke(() =>
                {
                    this.SpeechWaveDisplay.SetCurrentSample(currentTime.TotalMilliseconds);

                });
            }
        }


        private void StopPlay()
        {
            playTimer.Stop();
            waveOutDevice?.Stop();
            waveOutDevice?.Dispose();

        }

        private void PausePlay()
        {
            waveOutDevice?.Pause();

        }
        private void ResumePlay()
        {
            waveOutDevice?.Play();

        }

        private void BtnStop_OnClick(object sender, RoutedEventArgs e)
        {
           this.StopPlay();
        }

        private void Selector_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int value = ZoomValues[ZoomIndex]/100;
            this.SpeechWaveDisplay.SetZoom(value);
        }
    }
}
