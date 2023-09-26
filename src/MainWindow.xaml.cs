using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using Path = System.IO.Path;
using System.Diagnostics;
using System.Threading;

namespace SpeechWavePlayer
{

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// 临时存储音频编辑文件
        /// </summary>
        private Dictionary<int, string> speechDictionary;

        /// <summary>
        /// 当前操作音频序号
        /// </summary>
        private int currentSpeechBaseIndex;

        /// <summary>
        /// 剪辑文件存储目录
        /// </summary>
        private string saveSpeechTempPath;

        /// <summary>
        /// 剪切板数据
        /// </summary>
        private byte[] clipboardData;

        /// <summary>
        /// 最大支持存储
        /// </summary>
        private readonly int maxMemoryNum = 10;

        /// <summary>
        /// 当前操作语音数据
        /// </summary>
        private byte[] currentSpeechOriginData;

        /// <summary>
        /// 当前文件名称
        /// </summary>
        private string fileName;

        private string ffmpegPath;

        public MainWindow()
        {
            InitializeComponent();
            speechDictionary = new Dictionary<int, string>();
            saveSpeechTempPath = Path.Combine(AppContext.BaseDirectory, "temp");
            ffmpegPath = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
            if (!Directory.Exists(saveSpeechTempPath))
            {
                Directory.CreateDirectory(saveSpeechTempPath);
            }

            this.Closed += MainWindow_Closed;
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            speechDictionary.Clear();
            this.Player.Release();
            Directory.Delete(saveSpeechTempPath, true); //删除临时数据
        }

        public void LoadSpeech()
        {
            string originSpeech = speechDictionary[currentSpeechBaseIndex];
            this.Player.LoadSpeech(originSpeech);
            using var reader = new WaveFileReader(originSpeech);
            currentSpeechOriginData = new byte[reader.Length];
            reader.Read(currentSpeechOriginData,0,(int)reader.Length);
            reader.Close();
        }

        private void BtnOpen_OnClick(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Filter = "处理文件(wav,amr,mp3,wma,aac,m4a) |*.wav;*.amr;*.mp3;*.wma;*.aac;*.m4a";
            dialog.ShowDialog();
            string path = dialog.FileName;
            if (File.Exists(path))
            {
                fileName = Path.GetFileName(path);
                string waveFile = Get8kWavSpeech(path);
                AddSpeechToDictionary(waveFile);
                LoadSpeech();
            }
        }

        private void BtnSave_OnClick(object sender, RoutedEventArgs e)
        {
            if(speechDictionary.Count == 0) return;
            string originSpeech = speechDictionary[currentSpeechBaseIndex];
            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Filter = "处理文件(wav) |*.wav";
            dialog.ShowDialog();
            string path = dialog.FileName;
            File.Copy(originSpeech, path);
        }

        private void BtnClip_OnClick(object sender, RoutedEventArgs e)
        {
            var seg = this.Player.GetSelectionSampleSegment();
            if (Math.Abs(seg.Item1 - seg.Item2) == 0)
            {
                MessageBox.Show("请选取语音段");
                return;
            }
            int startPos = seg.Item1 * 2;
            int endPos = seg.Item2 * 2;
            int length = endPos - startPos;
            var clipboardData = currentSpeechOriginData.Skip(startPos).Take(length).ToArray();
            Clipboard.SetAudio(clipboardData);

            var remainder = new byte[currentSpeechOriginData.Length - length];
            Buffer.BlockCopy(currentSpeechOriginData, 0, remainder, 0, startPos);
            Buffer.BlockCopy(currentSpeechOriginData, endPos, remainder, startPos, currentSpeechOriginData.Length - endPos);
            string outPath = System.IO.Path.Combine(saveSpeechTempPath, $"{fileName}_{DateTime.Now:yyyyMMddssfff}.wav");
            SaveSpeech(outPath, remainder);
            AddSpeechToDictionary(outPath);
            this.LoadSpeech();
        }

        private void BtnCopy_OnClick(object sender, RoutedEventArgs e)
        {
            var seg = this.Player.GetSelectionSampleSegment();
            if (Math.Abs(seg.Item1 - seg.Item2) == 0)
            {
                MessageBox.Show("请选取语音段");
                return;
            }
            int startPos = seg.Item1 * 2;
            int endPos = seg.Item2 * 2;
            int length = Math.Abs(endPos - startPos);
            var clipboardData = currentSpeechOriginData.Skip(startPos).Take(length).ToArray();
            Clipboard.SetAudio(clipboardData);
        }

        private async void BtnPaste_OnClick(object sender, RoutedEventArgs e)
        {
            if (!Clipboard.ContainsAudio()) return;
            Stream replacementAudioStream = Clipboard.GetAudioStream();
            var clipData = new byte[replacementAudioStream.Length];
            //this.BtnPaste.IsEnabled = false;
            replacementAudioStream.Read(clipData, 0, (int)replacementAudioStream.Length);
            int pos = Player.GetCurrentSampleIndex() * 2;
            var newData = new byte[currentSpeechOriginData.Length + clipData.Length];
            Buffer.BlockCopy(currentSpeechOriginData, 0, newData, 0, pos);
            Buffer.BlockCopy(clipData, 0, newData, pos, clipData.Length);
            Buffer.BlockCopy(currentSpeechOriginData, pos, newData, pos + clipData.Length, currentSpeechOriginData.Length - pos);
            string outPath = System.IO.Path.Combine(saveSpeechTempPath, $"{fileName}_{DateTime.Now:yyyyMMddssfff}.wav");
            SaveSpeech(outPath, newData);
            AddSpeechToDictionary(outPath);
            //this.BtnPaste.IsEnabled = true;
            this.LoadSpeech();
        }

        private void BtnDelSeg_OnClick(object sender, RoutedEventArgs e)
        {
            var seg = this.Player.GetSelectionSampleSegment();
            if (Math.Abs(seg.Item1 - seg.Item2) == 0)
            {
                MessageBox.Show("请选取语音段");
                return;
            }

            int startPos = seg.Item1 * 2;
            int endPos = seg.Item2 * 2;
            int length = endPos - startPos;
            var remainder = new byte[currentSpeechOriginData.Length - length];
            Buffer.BlockCopy(currentSpeechOriginData, 0, remainder, 0, startPos);
            Buffer.BlockCopy(currentSpeechOriginData, endPos, remainder, startPos,
                currentSpeechOriginData.Length - endPos);
            string outPath = System.IO.Path.Combine(saveSpeechTempPath,
                $"{fileName}_{DateTime.Now:yyyyMMddssfff}.wav");
            SaveSpeech(outPath, remainder);
            AddSpeechToDictionary(outPath);
            this.LoadSpeech();
        }

        private void BtnCleanSeg_OnClick(object sender, RoutedEventArgs e)
        {
            var seg = this.Player.GetSelectionSampleSegment();
            if (Math.Abs(seg.Item1 - seg.Item2) == 0)
            {
                MessageBox.Show("请选取语音段");
                return;
            }

            int startPos = (int)(seg.Item1 * 2);
            int endPos = (int)(seg.Item2 * 2);
            for (int i = 0; i < currentSpeechOriginData.Length; i++)
            {
                if (i >= startPos && i <= endPos)
                {
                    currentSpeechOriginData[i] = 0x00;
                }
            }

            string outPath = Path.Combine(saveSpeechTempPath, $"{fileName}_{DateTime.Now:yyyyMMddssfff}.wav");
            SaveSpeech(outPath, currentSpeechOriginData);
            AddSpeechToDictionary(outPath);
            this.LoadSpeech();
        }

        private void BtnUndo_OnClick(object sender, RoutedEventArgs e)
        {
            if (speechDictionary.Count == 0) return;
            currentSpeechBaseIndex--;
            if (currentSpeechBaseIndex < 0)
                currentSpeechBaseIndex = 0;
            this.LoadSpeech();
        }

        private void BtnRecover_OnClick(object sender, RoutedEventArgs e)
        {
            if (speechDictionary.Count == 0) return;
            currentSpeechBaseIndex++;
            if (currentSpeechBaseIndex > speechDictionary.Count - 1)
                currentSpeechBaseIndex = speechDictionary.Count - 1;
            this.LoadSpeech();
        }

        private void AddSpeechToDictionary(string speech)
        {
        
            currentSpeechBaseIndex = speechDictionary.Count;
            if (currentSpeechBaseIndex > maxMemoryNum)
            {

                for (int i = 1; i < maxMemoryNum; i++)
                {
                    speechDictionary[i] = speechDictionary[i + 1];
                }

                currentSpeechBaseIndex = maxMemoryNum;
                speechDictionary[currentSpeechBaseIndex] = speech;
            }
            else
            {
                speechDictionary.Add(currentSpeechBaseIndex, speech);
            }

        }

        private void SaveSpeech(string outPath, byte[] speeBytes)
        {
            string outDirectory = Path.GetDirectoryName(outPath);
            if (!Directory.Exists(outDirectory))
            {
                Directory.CreateDirectory(outDirectory);
            }

            WaveFileWriter fileWriter = new WaveFileWriter(outPath, new WaveFormat(8000, 16, 1));
            fileWriter.Write(speeBytes);
            fileWriter.Flush();
            fileWriter.Dispose();
        }

        private string Get8kWavSpeech(string filePath)
        {
            if (!File.Exists(ffmpegPath))
            {
                MessageBox.Show("请拷贝ffmpeg.exe文件到根目录!");
                return filePath;
            }

            string outPath = Path.Combine(saveSpeechTempPath, $"{fileName}_{DateTime.Now:yyyyMMddssfff}.wav");
            string arguments = $"-i \"{filePath}\" -y -f wav -acodec pcm_s16le -ar 8000 -ac 1  \"{outPath}\"";
            try
            {
                using (Process p = new Process())
                {
                    p.StartInfo.FileName = ffmpegPath;
                    p.StartInfo.Arguments = arguments;
                    p.StartInfo.UseShellExecute = false; ////不使用系统外壳程序启动进程
                    p.StartInfo.CreateNoWindow = true; //不显示dos程序窗口
                    p.Start();
                    p.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                    p.WaitForExit(2000); //阻塞等待进程结束
                    while (!p.HasExited)
                    {
                        Thread.Sleep(50);
                    }

                    p.Close(); //关闭进程
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("ex");
            }

            return File.Exists(outPath) ? outPath : filePath;
        }

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {

        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {

        }

        private void BtnLook_Click(object sender, RoutedEventArgs e)
        {

        }
    }
}
