using System.Globalization;
using System.Windows.Media;
using System.Windows;
using System;

namespace SpeechWavePlayer;

/// <summary>
/// 基于DrawingVisual波形绘制
/// </summary>
public class WaveDisplayControl: FrameworkElement
{
    public delegate void UpdateZoomDelete(int zoom);

    /// <summary>
    /// 图像缩放事件
    /// </summary>
    public event UpdateZoomDelete OnUpdateZoomEvent;

    private VisualCollection visualCollections;
    private float[] speechFloatData;
    private int PCM_MAX_AMPLITUDE_VALUE = 32767;         //最大振幅
    private int PCM_MIN_AMPLITUDE_VALUE = -32767;        //最大振幅             
    private int SCALE_PIXEL_INTERVAL = 70;               //时间间隔
    private Rect centerRect;        //中间区域
    private Rect topRect;           //顶部区域
    private Rect rightRect;         //右侧区域
    private Rect bottomRect;        //底部区域
    private Rect scrollerBarRect;   //滚动条
    private int winX = 15;            //上下间距
    private int winY = 15;            //左右间距

    private int defaultSampleRate = 8000;             //默认采样率
    private int totalSampleCount;                     //总采样点数
    private int zoom = 1;                             //缩放倍数     
    private int pageStartSamples = 0;                 //每页开始采样点数
    private int perPageSamples = 0;                   //每页采样点数
    private int perPixelSamples = 0;                  //每个像素采样点数
    private int shiftX;                               //移动X                   
    private int shiftSamples;                         //移动采样点
    private Point currentMousePoint;                  //当前鼠标位置
    private Point lastMousePoint;                     //上一次鼠标位置

    private int currentSampleIndex;                   //当前采样点位置
    private int selectionStartSampleIndex;            //选区开始采样点
    private int selectionEndSampleIndex;              //选区结束采样点

    private int selectionStartPosHorizontal;          //选区开始位置
    private int selectionEndPosHorizontal;            //选区结束位置
    private bool isWaveSelectionClick;                //波形图区域点击
    private bool isScrollerBarClick;                  //滚动条区域点击

    private DrawingVisual drawingFullWavVisual;       //顶部完全波形
    private DrawingVisual drawingBottomVisual;        //底部时间刻度
    private DrawingVisual drawingCenterVisual;        //中间波形
    private DrawingVisual drawingSelectionVisual;     //选区
    private DrawingVisual drawingscrollbarRect;       //绘制滚动条

    private readonly Brush backgourndBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF101E87"));
    private readonly Brush waveBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF0F9DFF"));
    private readonly Brush waveBackgroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF01094f"));
    private readonly Brush selectionBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4230F284"));
    private readonly Brush scrollBarBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0D30F284"));
    public WaveDisplayControl()
    {
        drawingBottomVisual = new DrawingVisual();
        drawingCenterVisual = new DrawingVisual();
        visualCollections = new VisualCollection(this);
        isWaveSelectionClick = false;
        this.SizeChanged += SpeechWaveDisplay_SizeChanged;
        //this.MouseLeftButtonDown += SpeechWaveDisplay_MouseLeftButtonDown;
        //this.MouseMove += SpeechWaveDisplay_MouseMove;
        //this.MouseLeftButtonUp += SpeechWaveDisplay_MouseLeftButtonUp;
        //this.MouseWheel += SpeechWaveDisplay_MouseWheel; ;
    }

    public void SetCurrentSample(double ms)
    {
        currentSampleIndex = (int)(ms * defaultSampleRate / 1000);//8k语音 1秒钟8000采样点;
        int currentPos = (currentSampleIndex - pageStartSamples) / perPixelSamples;
        if (currentPos > centerRect.Right)
        {
            pageStartSamples += perPageSamples;
            if (pageStartSamples > (totalSampleCount - perPageSamples))
                pageStartSamples = totalSampleCount - perPageSamples;
            this.Update();
        }
        else
        {
            DrawWaveSelection();
        }

        if (ms == 0)
        {
            pageStartSamples = 0;
            this.Update();
        }

    }

    public void SetZoom(int value)
    {
        zoom = value;
        this.AdjustMonitor(); //修正中间位置
        Update();
    }
    private void SpeechWaveDisplay_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        topRect = new Rect(winX, winY, ActualWidth - 2 * winX, 0);
        rightRect = new Rect(winX, this.topRect.Bottom, ActualWidth - topRect.Width, centerRect.Height);
        centerRect = new Rect(rightRect.Right, topRect.Bottom + 5, topRect.Width, ActualHeight - 2 * winY - topRect.Height - 30);
        bottomRect = new Rect(winX, centerRect.Bottom, this.centerRect.Width, 30);
        this.Init();
    }
    private void Init()
    {
        DrawingVisual drawingVisual = new DrawingVisual();

        DrawingContext drawingContext = drawingVisual.RenderOpen();
        Rect backgroundRect = new Rect(0, 0, ActualWidth, ActualHeight);
        drawingContext.DrawRectangle(backgourndBrush, new Pen(Brushes.Gray, 0), backgroundRect);
        drawingContext.DrawRectangle(waveBackgroundBrush, new Pen(Brushes.DarkGray, 0), centerRect);
        //drawingContext.DrawRectangle(waveBackgroundBrush, new Pen(Brushes.DarkGray, 0), topRect);
        //drawingContext.DrawRectangle(Brushes.LightBlue, new Pen(Brushes.DarkGray, 1), rightRect);
        //drawingContext.DrawRectangle(Brushes.LightBlue, new Pen(Brushes.DarkGray, 1), bottomRect);

        drawingContext.Close();
        visualCollections.Add(drawingVisual); //绘制基本背景
        DrawCenterGrid();                     //绘制网格
        DrawRightFreqScale(PCM_MIN_AMPLITUDE_VALUE, PCM_MAX_AMPLITUDE_VALUE);//绘制右侧频率刻度
    }

    private void Update()
    {
        if (totalSampleCount == 0) return;
        double width = centerRect.Width;
        perPixelSamples = totalSampleCount / (int)(width * zoom);
        perPageSamples = totalSampleCount / zoom;
        int scrollerWidth = (int)(width / zoom);
        if (zoom == 1) pageStartSamples = 0;
        shiftX = (int)(width * ((double)pageStartSamples / totalSampleCount));
        scrollerBarRect = new Rect(shiftX + winX, topRect.Top + 1, scrollerWidth, topRect.Height - 2);
        DrawScrollBar();
        DrawBottomTimeScale(pageStartSamples / 8, (pageStartSamples + perPageSamples) / 8);      //绘制底部时间刻度
        DrawCenterWaveR1();
        //DrawWaveSelection();
    }

    public void SetSpeechData(byte[] speechData)
    {
        int sampleCount = speechData.Length / 2;
        speechFloatData = new float[speechData.Length / sizeof(float)];
        Buffer.BlockCopy(speechData, 0, speechFloatData, 0, speechData.Length);

        totalSampleCount = speechFloatData.Length;
        selectionStartPosHorizontal = 0;
        selectionEndPosHorizontal = 0;
        selectionStartSampleIndex = 0;
        selectionEndSampleIndex = 0;

        //DrawTopWave(); //画顶部波形

        Update();

    }

    public int GetCurrentSampleIndex()
    {
        return currentSampleIndex;
    }
    public Tuple<int, int> GetSelectionSegment()
    {
        int start = selectionStartSampleIndex;
        int end = selectionEndSampleIndex;
        return Tuple.Create(start, end);
    }
    private void SpeechWaveDisplay_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if(totalSampleCount==0) return;
        lastMousePoint = currentMousePoint;
        currentMousePoint = e.GetPosition(this);
        if (centerRect.Contains(currentMousePoint))
        {
            isWaveSelectionClick = true;
            selectionStartPosHorizontal = (int)currentMousePoint.X - winX;
            selectionEndPosHorizontal = selectionStartPosHorizontal;
            currentSampleIndex = selectionStartPosHorizontal * perPixelSamples + pageStartSamples;
            DrawWaveSelection();
            DrawScrollBar();
        }

        if (scrollerBarRect.Contains(currentMousePoint))
        {
            isScrollerBarClick = true;
        }
    }


    private void SpeechWaveDisplay_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (totalSampleCount == 0) return;
        currentMousePoint = e.GetPosition(this);
        if (centerRect.Contains(currentMousePoint) && isWaveSelectionClick)
        {
            selectionEndPosHorizontal = (int)currentMousePoint.X - winX;
            if (selectionEndPosHorizontal > selectionStartPosHorizontal)
            {
                selectionStartSampleIndex = currentSampleIndex;
                selectionEndSampleIndex = selectionEndPosHorizontal * perPixelSamples + pageStartSamples;
            }
            else
            {
                selectionStartSampleIndex = selectionEndPosHorizontal * perPixelSamples + pageStartSamples;
                selectionEndSampleIndex = currentSampleIndex;
            }
            DrawWaveSelection();
            DrawScrollBar();
        }

        if (topRect.Contains(currentMousePoint) && isScrollerBarClick && zoom != 1)
        {
            double shiftPos = currentMousePoint.X - lastMousePoint.X;
            lastMousePoint = currentMousePoint;
            double scrollX = shiftX + shiftPos;
            pageStartSamples = (int)(totalSampleCount * (scrollX / topRect.Width));
            if (pageStartSamples > (totalSampleCount - perPageSamples))
            {
                pageStartSamples = (int)(totalSampleCount - perPageSamples);
            }

            if (pageStartSamples < 0) pageStartSamples = 0;
            this.Update();

        }
    }

    private void SpeechWaveDisplay_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (totalSampleCount == 0) return;
        currentMousePoint = e.GetPosition(this);
        if (centerRect.Contains(currentMousePoint) && isWaveSelectionClick)
        {
            selectionEndPosHorizontal = (int)currentMousePoint.X - winX;
            if (selectionEndPosHorizontal > selectionStartPosHorizontal)
            {
                selectionStartSampleIndex = currentSampleIndex;
                selectionEndSampleIndex = selectionEndPosHorizontal * perPixelSamples + pageStartSamples;
            }
            else
            {
                selectionStartSampleIndex = selectionEndPosHorizontal * perPixelSamples + pageStartSamples;
                selectionEndSampleIndex = currentSampleIndex;
            }

            DrawWaveSelection();
            DrawScrollBar();
        }

        isWaveSelectionClick = false;
        isScrollerBarClick = false;
    }

    private void SpeechWaveDisplay_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (totalSampleCount == 0) return;
        if (e.Delta == 0) return;
        if (e.Delta > 0)
        {
            zoom *= 2;
        }
        else
        {
            zoom /= 2;
        }

        if (zoom < 1) zoom = 1;
        if (zoom > 32) zoom = 32;
        this.AdjustMonitor(); //修正位置
        OnUpdateZoomEvent?.Invoke(zoom);
    }
    /// <summary>
    /// 缩放后调整位置
    /// </summary>
    private void AdjustMonitor()
    {
        if (selectionStartSampleIndex > selectionEndSampleIndex || perPixelSamples == 0)
            return;
        double centerPos = centerRect.Width / 2;
        perPixelSamples = totalSampleCount / (int)(centerRect.Width * zoom);
        perPageSamples = totalSampleCount / zoom;
        int centerSamples = (int)centerPos * perPixelSamples + pageStartSamples;
        int selectionCenterSamples = (selectionEndSampleIndex - selectionStartSampleIndex) / 2 + selectionStartSampleIndex;
        int offsetSample = centerSamples - selectionCenterSamples;
        int targetSample = pageStartSamples - offsetSample;
        if (targetSample <= 0) targetSample = 0;
        pageStartSamples = targetSample;
        if (zoom <= 1)
        {
            pageStartSamples = 0;
        }
        else if (pageStartSamples > (totalSampleCount - perPageSamples))
        {
            pageStartSamples = totalSampleCount - perPageSamples;
        }

        selectionStartPosHorizontal = (selectionStartSampleIndex - pageStartSamples) / perPixelSamples;
        selectionEndPosHorizontal = selectionStartPosHorizontal + (selectionEndSampleIndex - selectionStartSampleIndex) / perPixelSamples;
        currentSampleIndex = pageStartSamples + selectionStartPosHorizontal * perPixelSamples;
        if (selectionStartPosHorizontal < centerRect.Left) selectionStartPosHorizontal = (int)centerRect.Left;
        if (selectionEndPosHorizontal > centerRect.Right) selectionEndPosHorizontal = (int)centerRect.Right;

    }


    //画顶部波形图
    private void DrawTopWave()
    {
        visualCollections.Remove(drawingFullWavVisual);
        var ww = topRect.Width;
        var yScale = topRect.Height / 2;
        double x, y1, y2;
        int index = speechFloatData.Length / (int)ww;

        drawingFullWavVisual = new DrawingVisual();

        DrawingContext drawingContext = drawingFullWavVisual.RenderOpen();
        drawingContext.DrawLine(new Pen(waveBrush, 1), new Point(topRect.Left, yScale + winY), new Point(topRect.Right, yScale + winY));
        for (int i = 0; i < ww; i++)
        {
            x = topRect.Left + i;
            y1 = yScale - speechFloatData[i * index] * yScale + winY;
            y2 = yScale + speechFloatData[i * index] * yScale + winY;
            drawingContext.DrawLine(new Pen(waveBrush, 1), new Point(x, y1), new Point(x, y2));

        }
        drawingContext.Close();
        visualCollections.Add(drawingFullWavVisual); //绘制全段波形图
    }

    private void DrawCenterGrid()
    {
        int scaleCount = (int)centerRect.Width / SCALE_PIXEL_INTERVAL;
        double scaleItemWidth = centerRect.Width / scaleCount;
        double scaleItemHeight = centerRect.Height / scaleCount * 2;
        int baseX = (int)centerRect.Left;
        int baseY = (int)centerRect.Top;
        var drawingGridVisual = new DrawingVisual();
        DrawingContext drawingContext = drawingGridVisual.RenderOpen();
        for (int i = 0; i < scaleCount - 1; i++)
        {
            baseX += (int)scaleItemWidth;
            drawingContext.DrawLine(new Pen(backgourndBrush, 1), new Point(baseX, centerRect.Top), new Point(baseX, centerRect.Bottom));
            if (i % 2 == 0)
            {
                baseY += (int)scaleItemHeight;
                drawingContext.DrawLine(new Pen(backgourndBrush, 1), new Point(centerRect.Left, baseY), new Point(centerRect.Right, baseY));

            }
        }
        drawingContext.Close();
        visualCollections.Add(drawingGridVisual);
    }
    /// <summary>
    /// 画右侧频率刻度
    /// </summary>
    private void DrawRightFreqScale(double minValue, double maxValue)
    {
        int scaleCount = 20; //波形图一般20刻度

        double baseY = rightRect.Bottom;

        DrawingVisual drawingVisual = new DrawingVisual();

        DrawingContext drawingContext = drawingVisual.RenderOpen();

        for (int i = 0; i < scaleCount - 1; i++)
        {
            baseY = rightRect.Bottom - (i + 1) * rightRect.Height / scaleCount;

            string positiveLabel = ((int)((i + 1) * (maxValue - minValue) / scaleCount + minValue)).ToString(CultureInfo.InvariantCulture);
            drawingContext.DrawLine(new Pen(Brushes.Black, 1), new Point(rightRect.X, baseY), new Point(rightRect.X + 5, baseY));
            FormattedText formattedText = new FormattedText(positiveLabel, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Verdana"), 10, Brushes.Black, 1.25);
            drawingContext.DrawText(formattedText, new Point(rightRect.X + 8, baseY - formattedText.Height / 2));

        }
        drawingContext.Close();
        visualCollections.Add(drawingVisual);
    }
    /// <summary>
    /// 画底部时间刻度
    /// </summary>
    private void DrawBottomTimeScale(double minValue, double maxValue)
    {
        //Console.WriteLine($" DrawBottomTimeScale,minValue:{minValue},maxValue:{maxValue}");
        visualCollections.Remove(drawingBottomVisual);
        int scaleCount = (int)bottomRect.Width / SCALE_PIXEL_INTERVAL;
        double valueInterval = (maxValue - minValue) / scaleCount;
        double scaleItemHeight = bottomRect.Width / scaleCount;
        int baseX = (int)bottomRect.Left;

        drawingBottomVisual = new DrawingVisual();

        DrawingContext drawingContext = drawingBottomVisual.RenderOpen();
        for (int i = 0; i < scaleCount - 1; i++)
        {
            baseX += (int)scaleItemHeight;
            drawingContext.DrawLine(new Pen(backgourndBrush, 1), new Point(baseX, bottomRect.Y), new Point(baseX, bottomRect.Y + 5));
            string positiveLabel = GetMillisecondsToString((int)minValue + (i + 1) * (int)(valueInterval));
            FormattedText formattedText = new FormattedText(positiveLabel, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Verdana"), 10, Brushes.White, 1.25);
            drawingContext.DrawText(formattedText, new Point(baseX - formattedText.Width / 2, bottomRect.Y + 10));
        }
        drawingContext.Close();
        visualCollections.Add(drawingBottomVisual);

    }
    /// <summary>
    /// 画中间波形
    /// </summary>
    private void DrawCenterWaveR1()
    {
        visualCollections.Remove(drawingCenterVisual);
        var ww = centerRect.Width;
        var yCenter = centerRect.Height / 2 + topRect.Height + winY;
        var yScale = centerRect.Height / 2;
        double x, y1, y2;
        int index = speechFloatData.Length / (int)ww;
        drawingCenterVisual = new DrawingVisual();

        DrawingContext drawingContext = drawingCenterVisual.RenderOpen();

        drawingContext.DrawLine(new Pen(waveBrush, 1), new Point(centerRect.Left, yCenter), new Point(centerRect.Right, yCenter));
        float maxValue = 0, minValue = 0;
        for (int i = 0; i < ww; i++)
        {
            int startNum = (int)(i * perPixelSamples) + pageStartSamples;
            int endNum = (int)((i + 1) * perPixelSamples) + pageStartSamples;
            if (startNum == endNum)
            {
                endNum++;
            }
            for (int ind = startNum; ind < endNum; ind++)
            {
                if (ind >= speechFloatData.Length)
                {
                    break;
                }
                maxValue = Math.Max(maxValue, speechFloatData[ind]);
                minValue = Math.Min(minValue, speechFloatData[ind]);
            }

            y1 = yCenter - maxValue * yScale;
            y2 = yCenter - minValue * yScale;
            x = centerRect.Left + i;
            drawingContext.DrawLine(new Pen(waveBrush, 1), new Point(x, y1), new Point(x, y2));
            minValue = 0;
            maxValue = 0;
        }
        drawingContext.Close();
        visualCollections.Add(drawingCenterVisual); //绘制全段波形图
    }
    /// <summary>
    /// 画波形选区
    /// </summary>
    /// <param name="minValue"></param>
    /// <param name="maxValue"></param>
    private void DrawWaveSelection()
    {
        visualCollections.Remove(drawingSelectionVisual);
        drawingSelectionVisual = new DrawingVisual();
        DrawingContext drawingContext = drawingSelectionVisual.RenderOpen();
        double startX, startY, endX, endY, width, height;
        if (selectionStartSampleIndex != selectionEndSampleIndex) //画选区
        {
            double startPos = winX + (selectionStartSampleIndex - pageStartSamples) / perPixelSamples;
            double endPos = winX + (selectionEndSampleIndex - pageStartSamples) / perPixelSamples;
            if (startPos < centerRect.Left) startPos = centerRect.Left;
            if (endPos < centerRect.Left) endPos = centerRect.Left;
            if (startPos > centerRect.Right) startPos = centerRect.Right;
            if (endPos > centerRect.Right) endPos = centerRect.Right;
            startX = startPos < endPos ? startPos : endPos;
            startY = centerRect.Top;
            width = Math.Abs(endPos - startPos);
            height = centerRect.Height;
            Rect selectRect = new Rect(startX, startY, width, height);
            drawingContext.DrawRectangle(selectionBrush, new Pen(Brushes.Red, 0), selectRect);

        }
        if (currentSampleIndex != 0)
        {
            startX = winX + (currentSampleIndex - pageStartSamples) / perPixelSamples;
            if (startX < centerRect.Left) startX = centerRect.Left;
            if (startX > centerRect.Right) startX = centerRect.Right;
            startY = centerRect.Top;
            endX = startX;
            endY = centerRect.Bottom;
            drawingContext.DrawLine(new Pen(Brushes.Red, 1), new Point(startX, startY), new Point(endX, endY));
        }

        drawingContext.Close();
        visualCollections.Add(drawingSelectionVisual);

    }
    /// <summary>
    /// 绘制滚动条
    /// </summary>
    private void DrawScrollBar()
    {
        visualCollections.Remove(drawingscrollbarRect);
        drawingscrollbarRect = new DrawingVisual();
        DrawingContext drawingContext = drawingscrollbarRect.RenderOpen();
        drawingContext.DrawRectangle(scrollBarBrush, new Pen(Brushes.White, 1), scrollerBarRect);
        if (selectionStartPosHorizontal != 0)
        {
            int perPixSamples = totalSampleCount / (int)topRect.Width;
            int selectionScrollStartPos = (int)topRect.Left + selectionStartSampleIndex / perPixSamples;
            int selectionScrollEndPos = (int)topRect.Left + selectionEndSampleIndex / perPixSamples;
            //Console.WriteLine($";scrollerBarRect.Left：{scrollerBarRect.Left},selectionScrollStartPos:{selectionScrollStartPos}");
            int width = Math.Abs(selectionScrollEndPos - selectionScrollStartPos);
            Rect selectionScrollRect = new Rect(selectionScrollStartPos, scrollerBarRect.Top, width, scrollerBarRect.Height);
            drawingContext.DrawRectangle(selectionBrush, new Pen(Brushes.Red, 1), selectionScrollRect);
        }
        drawingContext.Close();
        visualCollections.Add(drawingscrollbarRect);

    }
    public string GetMillisecondsToString(int ms)
    {
        TimeSpan dt = TimeSpan.FromMilliseconds(ms);
        return dt.Hours > 0 ? $"{dt.Hours:00}:{dt.Minutes:00}:{dt.Seconds:00}" : $"{dt.Minutes:00}:{dt.Seconds:00}.{dt.Milliseconds:0}";
    }
    protected override int VisualChildrenCount => visualCollections.Count;

    protected override Visual GetVisualChild(int index)
    {
        if (index < 0 || index >= visualCollections.Count)
        {
            throw new ArgumentOutOfRangeException();
        }

        return visualCollections[index];
    }
}