﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Timelapse.Controls;
using Timelapse.Database;
using Timelapse.Enums;
using Timelapse.EventArguments;
using Timelapse.Util;
using Xceed.Wpf.Toolkit;

namespace Timelapse.Images
{
    /// <summary>
    /// MarkableCanvas is a canvas that
    /// - contains an image that can be scaled and translated by the user with the mouse 
    /// - can draw and track markers atop the image
    /// - can show a magnified portion of the image in a magnifying glass
    /// - can save and restore a zoom+pan setting
    /// - can display a video 
    /// </summary>
    public partial class MarkableCanvas : Canvas
    {
        #region Public Properties

        /// <summary>
        /// Bounding boxes for detection. Whenever one is set, it is redrawn
        /// </summary>
        public BoundingBoxes BoundingBoxes
        {
            get
            {
                return this.boundingBoxes;
            }
            set
            {
                // update bounding boxes
                this.boundingBoxes = value;
                // render new bounding boxes and update display image
                this.RefreshBoundingBoxes();
            }
        }

        /// <summary>
        /// Gets the grid containing a multitude of zoomed out images
        /// </summary>
        public ThumbnailGrid ThumbnailGrid { get; private set; }

        /// <summary>
        /// We need a reference to the DataEntry Controls so we can enable and disable some of them
        /// </summary>
        private DataEntryControls dataEntryControls;
        public DataEntryControls DataEntryControls
        {
            get
            {
                return this.dataEntryControls;
            }
            set
            {
                this.ThumbnailGrid.DataEntryControls = value;
                this.dataEntryControls = value;
            }
        }

        /// <summary>
        /// The FilePlayer should be set from TimelapseWindow.cs, as we need a handle to it in order to manipulate it.
        /// </summary>
        public FilePlayer FilePlayer { get; set; }

        /// <summary>
        /// Gets the image displayed across the MarkableCanvas for image files
        /// </summary>
        public Image ImageToDisplay { get; set; }

        /// <summary>
        /// Gets the image displayed in the magnifying glass
        /// </summary>
        public Image ImageToMagnify { get; private set; }

        /// <summary>
        /// Whether the thumbnail grid is visible or not
        /// </summary>
        public bool IsThumbnailGridVisible
        {
            get
            {
                return this.ThumbnailGrid.Visibility == Visibility.Visible;
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the magnifying glass is generally visible or hidden, and returns its state
        /// </summary>
        public bool MagnifiersEnabled
        {
            get
            {
                // both the Offset Lens and the Magnifying Lens share the same enable state
                return this.magnifyingGlass.IsEnabled;
            }
            set
            {
                this.magnifyingGlass.IsEnabled = value;
                this.OffsetLens.IsEnabled = value;
                this.SetMagnifiersAccordingToCurrentState(value, value);
            }
        }

        /// <summary>
        /// Gets or sets the markers on the image
        /// </summary>
        public List<Marker> Markers
        {
            get
            {
                return this.markers;
            }
            set
            {
                // update markers
                this.markers = value;
                // render new markers and update display image
                this.RedrawMarkers();
            }
        }

        /// <summary>
        /// The VideoPlayer displayed by the markable canvasewhen a video is selected
        /// </summary>
        public VideoPlayer VideoPlayer { get; private set; }

        /// <summary>
        /// Gets or sets the maximum zoom of the display image
        /// </summary>
        public double ZoomMaximum { get; set; }
        #endregion

        #region Private variables
        private static readonly SolidColorBrush MarkerFillBrush = new SolidColorBrush(Color.FromArgb(2, 0, 0, 0));

        // A bookmark that saves the pan and zoom setting
        private readonly ZoomBookmark bookmark;

        // the canvas to magnify contains both an image and markers so the magnifying glass view matches the display image
        private readonly Canvas canvasToMagnify;

        // a Popup to show episode information, regardless of the selection or sorting criteria
        private EpisodePopup episodePopup;

        // A canvas used to display the bounding boxes
        private readonly Canvas bboxCanvas = new Canvas();

        // render transforms
        private readonly ScaleTransform imageToDisplayScale;
        private readonly TransformGroup transformGroup;
        private readonly TranslateTransform imageToDisplayTranslation;

        // magnifying glass, including increment for increasing or decreasing magnifying glass zoom
        private readonly MagnifyingGlass magnifyingGlass;
        private readonly double magnifyingGlassZoomStep;

        // Time of the last mousewheel event
        private DateTime lastMouseWheelDateTime = DateTime.Now;

        // Timer for resizing the ThumbnailGrid only after resizing is (likely) completed
        private readonly DispatcherTimer timerResize = new DispatcherTimer();

        // Timer for delaying updates in the midst of rapid navigation with the slider
        private readonly DispatcherTimer timerSlider = new DispatcherTimer();

        // markers
        private List<Marker> markers;

        // bounding boxes for detection
        private BoundingBoxes boundingBoxes;

        // mouse and position states used to discriminate clicks from drags
        private UIElement mouseDownSender;
        private Point mouseDownLocation;
        private Point previousMousePosition;

        // mouse click timing and state used to determine  double from single clicks
        private DateTime mouseDoubleClickTime;
        private bool isDoubleClick = false;
        private bool isPanning = false;
        private bool displayingImage = false;

        private readonly OffsetLens OffsetLens = new OffsetLens();
        #endregion

        #region Events
        public event EventHandler<MarkerEventArgs> MarkerEvent;
        public event Action SwitchedToThumbnailGridViewEventAction;
        public event Action SwitchedToSingleImageViewEventAction;

        private void SendMarkerEvent(MarkerEventArgs e)
        {
            this.MarkerEvent?.Invoke(this, e);
        }
        #endregion

        #region Initialization and Loading
        public MarkableCanvas()
        {
            // configure self
            this.Background = Brushes.Black;
            this.ClipToBounds = true;
            this.Focusable = true;
            this.ResetMaximumZoom();
            this.SizeChanged += this.MarkableImageCanvas_SizeChanged;

            this.markers = new List<Marker>();
            this.BoundingBoxes = new BoundingBoxes();

            // initialize render transforms
            // scale transform's center is set during layout once the image size is known
            // default bookmark is default zoomed out, normal pan state
            this.bookmark = new ZoomBookmark();
            this.imageToDisplayScale = new ScaleTransform(this.bookmark.Scale.X, this.bookmark.Scale.Y);
            this.imageToDisplayTranslation = new TranslateTransform(this.bookmark.Translation.X, this.bookmark.Translation.Y);
            this.transformGroup = new TransformGroup();
            this.transformGroup.Children.Add(this.imageToDisplayScale);
            this.transformGroup.Children.Add(this.imageToDisplayTranslation);

            // set up the canvas
            this.MouseWheel += this.ImageOrCanvas_MouseWheel;

            // set up display image
            this.ImageToDisplay = new Image();
            this.ImageToDisplay.HorizontalAlignment = HorizontalAlignment.Left;

            this.ImageToDisplay.MouseDown += this.ImageVideoOrCanvas_MouseDown;
            this.ImageToDisplay.MouseLeftButtonUp += this.ImageVideoOrCanvas_MouseUp;
            this.ImageToDisplay.RenderTransform = this.transformGroup;
            this.ImageToDisplay.SizeChanged += this.ImageToDisplay_SizeChanged;
            this.ImageToDisplay.VerticalAlignment = VerticalAlignment.Top;
            Canvas.SetLeft(this.ImageToDisplay, 0);
            Canvas.SetTop(this.ImageToDisplay, 0);
            this.Children.Add(this.ImageToDisplay);

            // set up display video
            this.VideoPlayer = new VideoPlayer();
            this.VideoPlayer.HorizontalAlignment = HorizontalAlignment.Left;
            this.VideoPlayer.SizeChanged += this.VideoToDisplay_SizeChanged;
            this.VideoPlayer.VerticalAlignment = VerticalAlignment.Top;
            this.VideoPlayer.MouseWheel += this.ImageOrCanvas_MouseWheel;
            this.VideoPlayer.MouseDown += this.ImageVideoOrCanvas_MouseDown;
            this.VideoPlayer.MouseLeftButtonUp += this.ImageVideoOrCanvas_MouseUp;
            Canvas.SetLeft(this.VideoPlayer, 0);
            Canvas.SetTop(this.VideoPlayer, 0);
            this.Children.Add(this.VideoPlayer);

            // Set up zoomed out grid showing multitude of images
            this.ThumbnailGrid = new ThumbnailGrid();
            this.ThumbnailGrid.Visibility = Visibility.Collapsed;

            Canvas.SetZIndex(this.ThumbnailGrid, 1000); // High Z-index so that it appears above other objects and magnifier
            Canvas.SetLeft(this.ThumbnailGrid, 0);
            Canvas.SetTop(this.ThumbnailGrid, 0);
            this.Children.Add(this.ThumbnailGrid);

            // set up image to magnify
            this.ImageToMagnify = new Image();
            this.ImageToMagnify.HorizontalAlignment = HorizontalAlignment.Left;
            this.ImageToMagnify.SizeChanged += this.ImageToMagnify_SizeChanged;
            this.ImageToMagnify.VerticalAlignment = VerticalAlignment.Top;
            Canvas.SetLeft(this.ImageToMagnify, 0);
            Canvas.SetTop(this.ImageToMagnify, 0);

            this.canvasToMagnify = new Canvas();
            this.canvasToMagnify.SizeChanged += this.CanvasToMagnify_SizeChanged;
            this.canvasToMagnify.Children.Add(this.ImageToMagnify);

            // set up the magnifying glass
            this.magnifyingGlass = new MagnifyingGlass(this);
            this.magnifyingGlassZoomStep = Constant.MarkableCanvas.MagnifyingGlassZoomIncrement;
            this.OffsetLens.ZoomFactor = Constant.MarkableCanvas.OffsetLensDefaultZoom;

            Canvas.SetZIndex(this.magnifyingGlass, 999); // Should always be in front
            this.Children.Add(this.magnifyingGlass);

            // Initialize double click timing
            this.mouseDoubleClickTime = DateTime.Now;

            // event handlers for image/video interaction: keys, mouse handling for markers
            this.MouseLeave += this.ImageOrCanvas_MouseLeave;
            this.MouseMove += this.MarkableCanvas_MouseMove;
            this.VideoPlayer.Video.MouseLeave += this.Video_MouseLeave;
            this.PreviewKeyDown += this.MarkableCanvas_PreviewKeyDown;
            this.PreviewKeyUp += this.MarkableCanvas_PreviewKeyUp;
            this.Loaded += this.MarkableCanvas_Loaded;

            // When started, refreshes the ThumbnailGrid after 100 msecs (unless the timer is reset or stopped)
            this.timerResize.Interval = TimeSpan.FromMilliseconds(200);
            this.timerResize.Tick += this.TimerResize_Tick;

            // When started, refreshes the ThumbnailGrid after 100 msecs (unless the timer is reset or stopped)
            this.timerSlider.Interval = TimeSpan.FromMilliseconds(200);
            this.timerSlider.Tick += this.TimerSlider_Tick;

            // Default to the image view, as it will be all black
            this.ImageToDisplay.Visibility = Visibility.Visible;
            this.VideoPlayer.Visibility = Visibility.Collapsed;

            // Continue with initializations required by the ImageAdjustment partial class
            this.InitializeImageAdjustment();
        }

        private void Video_MouseLeave(object sender, MouseEventArgs e)
        {
            this.SetMagnifiersAccordingToCurrentState(false, false);
        }

        // Hide the magnifiers initially, as the mouse pointer may not be atop the canvas
        private void MarkableCanvas_Loaded(object sender, RoutedEventArgs e)
        {
            MagnifierManager.SetMagnifier(VideoPlayer.Video, OffsetLens);
            this.SetMagnifiersAccordingToCurrentState(false, false);
        }

        #endregion

        #region Public methods - Set Display Image or Video
        /// <summary>
        /// Sets only the display image and leaves markers and the magnifier image unchanged.  Used by the differencing routines to set the difference image.
        /// </summary>
        public void SetDisplayImage(BitmapSource bitmapSource)
        {
            // If its a differenced image, generate an event saying so.
            ImageCache imageCache = Util.GlobalReferences.MainWindow?.DataHandler?.ImageCache;
            if (imageCache != null)
            {
                bool isPrimaryImage = imageCache.CurrentDifferenceState == ImageDifferenceEnum.Unaltered;
                this.GenerateImageStateChangeEvent(true, isPrimaryImage); //  Signal change in image state (consumed by ImageAdjuster)
            }
            this.ImageToDisplay.Source = bitmapSource;
            this.SetMagnifiersAccordingToCurrentState(true, true);
        }

        /// <summary>
        /// Set a wholly new image.  Clears existing markers and syncs the magnifier image to the display image.
        /// </summary>
        public void SetNewImage(BitmapSource bitmapSource, List<Marker> markers)
        {
            // change to new markers
            this.markers = markers;

            this.ImageToDisplay.Source = bitmapSource;
            // initiate render of magnified image
            // The asynchronous chain behind this is not entirely trivial.  The links are
            //   1) ImageToMagnify_SizeChanged fires and updates canvasToMagnify's size to match
            //   2) CanvasToMagnify_SizeChanged fires and redraws the magnified markers since the cavas size is now known and marker positions can update
            //   3) CanvasToMagnify_SizeChanged initiates a render on the magnifying glass to show the new image and marker positions
            //   4) if it's visible the magnifying glass content updates
            // This synchronization to WPF render opertations is necessary as, despite their appearance, properties like Source, Width, and Height are 
            // asynchronous.  Other approaches therefore tend to be subject to race conditions in render order which hide or misplace markers in the 
            // magnified view and also have a proclivity towards leaving incorrect or stale magnifying glass content on screen.
            // 
            // Another race exists as this.Markers can be set during the above rendering, initiating a second, concurrent marker render.  This is unavoidable
            // due to the need to expose a marker property but is mitigated by accepting new markers through this API and performing the set above as 
            // this.markers rather than this.Markers.
            this.ImageToMagnify.Source = bitmapSource;
            this.displayingImage = true;

            this.GenerateImageStateChangeEvent(true, true); //  Signal change in image state (consumed by ImageAdjuster)

            // ensure display image is visible
            if (this.ThumbnailGrid.IsGridActive == false)
            {
                this.SwitchToImageView();
            }
            else
            {
                this.SwitchToThumbnailGridView();
            }
        }

        public void SetNewVideo(FileInfo videoFile, List<Marker> markers)
        {
            // Check the arguments for null 
            if (videoFile == null || videoFile.Exists == false)
            {
                this.SetNewImage(Constant.ImageValues.FileNoLongerAvailable.Value, markers);
                this.displayingImage = true;
                return;
            }

            this.markers = markers;
            this.VideoPlayer.SetSource(new Uri(videoFile.FullName));
            this.displayingImage = false;

            if (this.ThumbnailGrid.IsGridActive == false)
            {
                this.SwitchToVideoView();
            }
            else
            {
                this.SwitchToThumbnailGridView();
            }
        }
        #endregion

        #region Public methods: Scaling and Zooming
        public void ResetMaximumZoom()
        {
            this.ZoomMaximum = Constant.MarkableCanvas.ImageZoomMaximum;
        }

        // Scale the image around the given image location point, where we are zooming in if
        // zoomIn is true, and zooming out if zoomIn is false
        private void ScaleImage(Point location, bool zoomIn)
        {

            // Get out of here if we are already at our maximum or minimum scaling values 
            // while zooming in or out respectively 
            if ((zoomIn && this.imageToDisplayScale.ScaleX >= this.ZoomMaximum) ||
                (!zoomIn && this.imageToDisplayScale.ScaleX <= Constant.MarkableCanvas.ImageZoomMinimum))
            {
                return;
            }

            // We will scale around the current point
            Point beforeZoom = this.PointFromScreen(this.ImageToDisplay.PointToScreen(location));

            // Calculate the scaling factor during zoom ins or out. Ensure that we keep within our
            // maximum and minimum scaling bounds. 
            if (zoomIn)
            {
                // We are zooming in
                // Calculate the scaling factor
                this.imageToDisplayScale.ScaleX *= Constant.MarkableCanvas.ImageZoomStep;   // Calculate the scaling factor
                this.imageToDisplayScale.ScaleY *= Constant.MarkableCanvas.ImageZoomStep;

                // Make sure we don't scale beyond the maximum scaling factor
                this.imageToDisplayScale.ScaleX = Math.Min(this.ZoomMaximum, this.imageToDisplayScale.ScaleX);
                this.imageToDisplayScale.ScaleY = Math.Min(this.ZoomMaximum, this.imageToDisplayScale.ScaleY);
            }
            else
            {
                // We are zooming out. 
                // Calculate the scaling factor
                this.imageToDisplayScale.ScaleX /= Constant.MarkableCanvas.ImageZoomStep;
                this.imageToDisplayScale.ScaleY /= Constant.MarkableCanvas.ImageZoomStep;

                // Make sure we don't scale beyond the minimum scaling factor
                this.imageToDisplayScale.ScaleX = Math.Max(Constant.MarkableCanvas.ImageZoomMinimum, this.imageToDisplayScale.ScaleX);
                this.imageToDisplayScale.ScaleY = Math.Max(Constant.MarkableCanvas.ImageZoomMinimum, this.imageToDisplayScale.ScaleY);

                // if there is no scaling, reset translations
                if (this.imageToDisplayScale.ScaleX == 1.0 && this.imageToDisplayScale.ScaleY == 1.0)
                {
                    this.imageToDisplayTranslation.X = 0.0;
                    this.imageToDisplayTranslation.Y = 0.0;
                    return; // I THINK WE CAN DO THIS - CHECK;
                }
            }

            Point afterZoom = this.PointFromScreen(this.ImageToDisplay.PointToScreen(location));

            // Scale the image, and at the same time translate it so that the 
            // point in the image under the cursor stays there
            lock (this.ImageToDisplay)
            {
                double imageWidth = this.ImageToDisplay.Width * this.imageToDisplayScale.ScaleX;
                double imageHeight = this.ImageToDisplay.Height * this.imageToDisplayScale.ScaleY;

                Point center = this.PointFromScreen(this.ImageToDisplay.PointToScreen(
                    new Point(this.ImageToDisplay.Width / 2.0, this.ImageToDisplay.Height / 2.0)));

                double newX = center.X - (afterZoom.X - beforeZoom.X);
                double newY = center.Y - (afterZoom.Y - beforeZoom.Y);

                if (newX - imageWidth / 2.0 >= 0.0)
                {
                    newX = imageWidth / 2.0;
                }
                else if (newX + imageWidth / 2.0 <= this.ActualWidth)
                {
                    newX = this.ActualWidth - imageWidth / 2.0;
                }

                if (newY - imageHeight / 2.0 >= 0.0)
                {
                    newY = imageHeight / 2.0;
                }
                else if (newY + imageHeight / 2.0 <= this.ActualHeight)
                {
                    newY = this.ActualHeight - imageHeight / 2.0;
                }

                this.imageToDisplayTranslation.X += newX - center.X;
                this.imageToDisplayTranslation.Y += newY - center.Y;
            }
            this.RedrawMarkers();
            this.RefreshBoundingBoxes();
        }


        // Return to the zoomed out level, with no panning
        public void ZoomOutAllTheWay()
        {
            this.imageToDisplayScale.ScaleX = 1.0;
            this.imageToDisplayScale.ScaleY = 1.0;
            this.imageToDisplayTranslation.X = 0.0;
            this.imageToDisplayTranslation.Y = 0.0;
            this.RedrawMarkers();
            this.RefreshBoundingBoxes();
        }
        #endregion

        #region Public methods: Bookmarks
        // Save the current zoom / pan levels as a bookmark
        public void SetBookmark()
        {
            // a user may want to flip between completely zoomed out / normal pan settings and a saved zoom / pan setting that focuses in on a particular region
            // To do this, we save / restore the zoom pan settings of a particular view, or return to the default zoom/pan.
            if (this.imageToDisplayScale.ScaleX == 1 && this.imageToDisplayScale.ScaleY == 1)
            {
                // If the scale is unzoomed, then don't bother saving it as it may just be the result of an unintended key press. 
                return;
            }
            this.bookmark.Set(this.imageToDisplayScale, this.imageToDisplayTranslation);
        }

        // This version sets the bookmark with the provided points (retrieved from the registry) indicating scale and translation saved from a previous session
        public void SetBookmark(Point scale, Point translation)
        {
            this.bookmark.Set(scale, translation);
        }

        // return the current Bookmark scale point
        public Point GetBookmarkScale()
        {
            return this.bookmark.GetScale();
        }

        // return the current Bookmark Translation as a point
        public Point GetBookmarkTranslation()
        {
            return this.bookmark.GetTranslation();
        }

        // Return to the zoom / pan levels saved as a bookmark
        public void ApplyBookmark()
        {
            this.bookmark.Apply(this.imageToDisplayScale, this.imageToDisplayTranslation);
            this.RedrawMarkers();
            this.RefreshBoundingBoxes();
        }
        #endregion

        #region Public methods: Window shuffling
        public void SwitchToImageView()
        {
            // Just to make sure we are displaying the correct things
            this.ImageToDisplay.Visibility = Visibility.Visible;
            this.VideoPlayer.Visibility = Visibility.Collapsed;
            this.VideoPlayer.Pause();
            this.SetMagnifiersAccordingToCurrentState(false, true);
            // this.OffsetLens.Show = false;

            this.GenerateImageStateChangeEvent(false, true); //  Signal change in image state (consumed by ImageAdjuster)

            if (this.IsThumbnailGridVisible == false)
            {
                return;
            }
            // These operations are only needed if we weren't in the single image view
            this.ThumbnailGrid.Visibility = Visibility.Collapsed;
            this.SwitchedToSingleImageViewEventAction();
            this.DataEntryControls.SetEnableState(ControlsEnableStateEnum.SingleImageView, -1);
        }
        public void SwitchToVideoView()
        {
            this.ImageToDisplay.Visibility = Visibility.Collapsed;
            this.SetMagnifiersAccordingToCurrentState(false, true);
            //this.OffsetLens.Show = this.MagnifiersEnabled && this.VideoToDisplay.IsUnScaled;
            this.VideoPlayer.Visibility = Visibility.Visible;
            this.RedrawMarkers(); // Clears the markers as none should be associated with the video

            this.GenerateImageStateChangeEvent(false, false); //  Signal change in image state (consumed by ImageAdjuster)

            if (this.IsThumbnailGridVisible == false)
            {
                return;
            }
            // These operations are only needed if we weren't in the single image view
            this.ThumbnailGrid.Visibility = Visibility.Collapsed;
            this.SwitchedToSingleImageViewEventAction();
            this.DataEntryControls.SetEnableState(ControlsEnableStateEnum.SingleImageView, -1);
        }

        public void SwitchToThumbnailGridView()
        {
            // No need to switch as we are already in it
            if (this.IsThumbnailGridVisible == true)
            {
                return;
            }
            this.GenerateImageStateChangeEvent(false, false); //  Signal change in image state (consumed by ImageAdjuster)

            this.ThumbnailGrid.Visibility = Visibility.Visible;
            this.SwitchedToThumbnailGridViewEventAction();

            this.ImageToDisplay.Visibility = Visibility.Collapsed;
            this.SetMagnifiersAccordingToCurrentState(false, false);
            this.VideoPlayer.Visibility = Visibility.Collapsed;
            this.VideoPlayer.Pause();
        }
        #endregion

        #region Public / Private methods: Draw Bounding Box
        // Draw bounding boxes into a boundingbox canvas that overlays the MarkableCanvas 
        public void DrawBoundingBox(Size canvasRenderSize)
        {
            // Remove existing bounding boxes, if any.
            // Note that we do this even if detections may not exist, as we need to clear things if the user had just toggled
            // detections off
            this.bboxCanvas.Children.Clear();
            this.Children.Remove(this.bboxCanvas);

            if (GlobalReferences.DetectionsExists == false || Keyboard.IsKeyDown(Key.H))
            {
                // As detection don't exist, there won't be any bounding boxes to draw.
                return;
            }

            int stroke_thickness = 5;
            // Max Confidence is over all bounding boxes, regardless of the categories.
            // So we just use it as a short cut, i.e., if none of the bounding boxes are above the threshold, we can abort.
            // Also, add a slight correction value to the MaxConfidence, so confidences near the threshold will still appear.
            double correction = 0.005;
            if (this.BoundingBoxes.MaxConfidence + correction < Util.GlobalReferences.TimelapseState.BoundingBoxDisplayThreshold && this.BoundingBoxes.MaxConfidence + correction < Util.GlobalReferences.TimelapseState.BoundingBoxThresholdOveride)
            {
                // Ignore any bounding box that is below the desired confidence threshold for displaying it.
                // Note that the BoundingBoxDisplayThreshold is the user-defined default set in preferences, while the BoundingBoxThresholdOveride is the threshold
                // determined in the select dialog. For example, if (say) the preference setting is .6 but the selection is at .4 confidence, then we should 
                // show bounding boxes when the confidence is .4 or more.
                return;
            }

            this.bboxCanvas.Width = canvasRenderSize.Width;
            this.bboxCanvas.Height = canvasRenderSize.Height;
            foreach (BoundingBox bbox in this.BoundingBoxes.Boxes)
            {
                if (bbox.Confidence + correction < Util.GlobalReferences.TimelapseState.BoundingBoxDisplayThreshold && bbox.Confidence + correction < Util.GlobalReferences.TimelapseState.BoundingBoxThresholdOveride)
                {
                    // Ignore any bounding box that is below the desired confidence threshold for displaying it.
                    // Note that the BoundingBoxDisplayThreshold is the user-defined default set in preferences, while the BoundingBoxThresholdOveride is the threshold
                    // determined in the select dialog. For example, if (say) the preference setting is .6 but the selection is at .4 confidence, then we should 
                    // show bounding boxes when the confidence is .4 or more.
                    continue;
                }

                // Create a bounding box 
                Rectangle rect = new Rectangle();
                byte transparency = (byte)Math.Round(255 * bbox.Confidence);

                // The color of the bounding box depends upon its category
                SolidColorBrush brush;
                switch (bbox.DetectionCategory)
                {
                    case "0":
                        brush = new SolidColorBrush(Color.FromArgb(transparency, 0, 255, 0)); // Green
                        break;
                    case "1":
                        brush = new SolidColorBrush(Color.FromArgb(transparency, 255, 0, 0)); // Red
                        break;
                    case "2":
                        brush = new SolidColorBrush(Color.FromArgb(transparency, 0, 0, 255)); // Blue
                        break;
                    case "3":
                        brush = new SolidColorBrush(Color.FromArgb(transparency, 0, 255, 255)); // Peacock green/blue
                        break;
                    default:
                        brush = new SolidColorBrush(Color.FromArgb(transparency, 255, 255, 255)); // White
                        break;
                }
                rect.Stroke = brush;
                rect.StrokeThickness = stroke_thickness;
                rect.ToolTip = bbox.DetectionLabel + " detected, confidence=" + bbox.Confidence.ToString();
                foreach (KeyValuePair<string, string> classification in bbox.Classifications)
                {
                    rect.ToolTip += Environment.NewLine + classification.Key + " " + classification.Value;
                }

                //// Get the corners from the bounding box, and convert it into a rectangle that will be in the right place (including scaling / panning as needed)
                Point screenPositionTopLeft = this.transformGroup.Transform(BoundingBox.ConvertRatioToPoint(bbox.Rectangle.Left, bbox.Rectangle.Top, canvasRenderSize.Width, canvasRenderSize.Height));
                Point screenPositionBottomRight = this.transformGroup.Transform(BoundingBox.ConvertRatioToPoint(bbox.Rectangle.Left + bbox.Rectangle.Width, bbox.Rectangle.Top + bbox.Rectangle.Height, canvasRenderSize.Width, canvasRenderSize.Height));
                Point screenPostionWidthHeight = new Point(screenPositionBottomRight.X - screenPositionTopLeft.X, screenPositionBottomRight.Y - screenPositionTopLeft.Y);

                // We also adjust the rect width and height to take into account the stroke thickness, which
                // gives the effect the at inside part of the border defines the bounding box (otherwise the border thickness would overlap with the 
                // entity in the bounding box)
                rect.Width = screenPostionWidthHeight.X + (2 * stroke_thickness);
                rect.Height = screenPostionWidthHeight.Y + (2 * stroke_thickness);

                // Now add the rectangle to the canvas, also adjusting for the stroke thickness.
                Canvas.SetLeft(rect, screenPositionTopLeft.X - stroke_thickness);
                Canvas.SetTop(rect, screenPositionTopLeft.Y - stroke_thickness);
                this.bboxCanvas.Children.Add(rect);
                this.bboxCanvas.Tag = Constant.MarkableCanvas.BoundingBoxCanvasTag;
            }
            Canvas.SetLeft(this.bboxCanvas, 0);
            Canvas.SetTop(this.bboxCanvas, 0);
            Canvas.SetZIndex(this.bboxCanvas, 1);
            this.Children.Add(this.bboxCanvas);
        }

        /// <summary>
        /// Remove all and then draw all the bounding boxes
        /// </summary>
        private void RefreshBoundingBoxes()
        {
            if (this.ImageToDisplay != null)
            {
                this.DrawBoundingBox(this.ImageToDisplay.RenderSize);
            }
        }
        #endregion

        #region Public / Private methods: Magnifier Drawing and Zooming
        /// <summary>
        /// Zoom in the magnifying glass image  by the amount defined by the property MagnifierZoomDelta
        /// </summary>
        public void MagnifierZoomIn()
        {
            // Process zoom requests only if the magnifiers are visible, and only when the particular image/video magnifier is being displayed
            if (this.IsThumbnailGridVisible)
            {
                return;
            }
            if (this.magnifyingGlass.IsVisible)
            {
                this.SetMagnifyingGlassZoom(this.GetMagnifyingGlassZoom() - this.magnifyingGlassZoomStep);
            }
            else if (this.OffsetLens.Show)
            {
                // Adjust the new zoom level for the offset lens, making sure its not below the minimum
                double newZoomLevel = this.OffsetLens.ZoomFactor - Constant.MarkableCanvas.OffsetLensZoomIncrement;
                this.OffsetLens.ZoomFactor = (newZoomLevel <= Constant.MarkableCanvas.OffsetLensMinimumZoom) ? Constant.MarkableCanvas.OffsetLensMinimumZoom : newZoomLevel;
            }
        }

        /// <summary>
        /// Zoom out the magnifying glass image  by the amount defined by the property MagnifierZoomDelta
        /// </summary>
        public void MagnifierZoomOut()
        {
            // Process zoom requests only if the magnifiers are visible, and only when the particular image/video magnifier is being displayed
            if (this.IsThumbnailGridVisible)
            {
                return;
            }
            if (this.magnifyingGlass.IsVisible)
            {
                this.SetMagnifyingGlassZoom(this.GetMagnifyingGlassZoom() + this.magnifyingGlassZoomStep);
            }
            else if (this.OffsetLens.Show)
            {
                // Adjust the new zoom level for the offset lens, making sure its not below the minimum
                double newZoomLevel = this.OffsetLens.ZoomFactor + Constant.MarkableCanvas.OffsetLensZoomIncrement;
                this.OffsetLens.ZoomFactor = (newZoomLevel > Constant.MarkableCanvas.OffsetLensMaximumZoom) ? Constant.MarkableCanvas.OffsetLensMaximumZoom : newZoomLevel;
            }
        }

        /// <summary>
        /// Gets or sets the amount we should zoom (scale) the image in the magnifying glass
        /// </summary>
        private void SetMagnifyingGlassZoom(double value)
        {
            // clamp the value
            if (value < Constant.MarkableCanvas.MagnifyingGlassMaximumZoom)
            {
                value = Constant.MarkableCanvas.MagnifyingGlassMaximumZoom;
            }
            else if (value > Constant.MarkableCanvas.MagnifyingGlassMinimumZoom)
            {
                value = Constant.MarkableCanvas.MagnifyingGlassMinimumZoom;
            }
            this.magnifyingGlass.Zoom = value;

            // update magnifier content if there is something to magnify
            if (this.ImageToMagnify.Source != null && this.ImageToDisplay.ActualWidth > 0)
            {
                this.RedrawMagnifyingGlassIfVisible();
            }
        }

        /// <summary>
        /// Gets or sets the amount we should zoom (scale) the image in the magnifying glass
        /// </summary>
        private double GetMagnifyingGlassZoom()
        {
            return this.magnifyingGlass.Zoom;
        }

        private void RedrawMagnifyingGlassIfVisible()
        {
            this.magnifyingGlass.RedrawIfVisible(NativeMethods.GetCursorPos(this), this.canvasToMagnify);
        }

        private void SetMagnifiersAccordingToCurrentState(bool showMagnifier, bool showOffset)
        {
            this.magnifyingGlass.Show = showMagnifier ? this.MagnifiersEnabled && this.displayingImage : false;

            if (showOffset & this.MagnifiersEnabled && this.displayingImage == false && this.VideoPlayer.IsUnScaled & this.IsThumbnailGridVisible == false)
            {
                // Check if the cursor is over the video
                Point mousePositionOnVideo = Mouse.GetPosition(this.VideoPlayer.Video);
                Point transformedSize = this.transformGroup.Transform(new Point(this.VideoPlayer.Video.ActualWidth, this.VideoPlayer.Video.ActualHeight));

                bool mouseOverVideo = (mousePositionOnVideo.X <= transformedSize.X) && (mousePositionOnVideo.Y <= transformedSize.Y);
                this.OffsetLens.Show = mouseOverVideo;
                //this.OffsetLens.Show = showOffset ? this.MagnifiersEnabled && this.displayingImage == false && this.VideoToDisplay.IsUnScaled : false;
            }
            else
            {
                this.OffsetLens.Show = false;
            }
        }
        #endregion

        #region Public / Private methods: ThumbnailGrid
        // Zoom in (or out) of single image and/or overview 
        public void TryZoomInOrOut(bool zoomIn, Point imageMousePosition, Point videoMousePosition)
        {
            // Manage videos first
            if (this.IsThumbnailGridVisible == false && this.ImageToDisplay.IsVisible == false)
            {
                lock (this.VideoPlayer)
                {
                    // Request Zoom out on a zoomed-in Video
                    if (zoomIn || (zoomIn == false && this.VideoPlayer.IsUnScaled == false))
                    {
                        this.VideoPlayer.ScaleVideo(videoMousePosition, zoomIn);
                        this.SetMagnifiersAccordingToCurrentState(false, true);
                        return;
                    }
                }
            }
            lock (this)
            {
                // Request Zoom out on either an unscaled image or the thumbnail grid. 
                // Note on why this is ambiguous: if the thumbnail grid is visible, it means the (hidden) image is also unscaled
                if (zoomIn == false && this.imageToDisplayScale.ScaleX == Constant.MarkableCanvas.ImageZoomMinimum)
                {
                    // Option 1. Request zoom out on Thumbnail Grid,
                    //           Aborted as we are already at the maximum allowable steps on ThumbnailGrid
                    //if (this.ThumbnailGridState >= Constant.ThumbnailGrid.MaxRows)
                    //{
                    //    return;
                    //}

                    // Option 2. Request zoom out on either the ThumbnailGrid an unscaled image. 
                    bool isInitialSwitchToThumbnailGrid = (this.ThumbnailGrid.IsGridActive) ? true : false;
                    // this.ThumbnailGridState++;
                    this.SwitchToThumbnailGridView();

                    // Option 2a. We tried to refresh, but there isn't enough space available on the thumbnail grid.
                    //            Thus try to zoom out again at the next zoom-out level
                    ThumbnailGridRefreshStatus status = this.RefreshThumbnailGrid(zoomIn);
                    if (status == ThumbnailGridRefreshStatus.NotEnoughSpaceForEvenOneCell)
                    {
                        this.TryZoomInOrOut(zoomIn, imageMousePosition, videoMousePosition); // STOPPING CONDITION AT MINIMUM???
                        return;
                    }
                    // Option 2b: Zoom out request denied.
                    else if (status == ThumbnailGridRefreshStatus.Aborted || status == ThumbnailGridRefreshStatus.AtMaximumZoomLevel)
                    {
                        return;
                    }

                    // Option 2c. We've gone from the single image to the multi-image view.
                    // By default, select the first item (as we want the data for the first item to remain displayed)
                    if (isInitialSwitchToThumbnailGrid)
                    {
                        this.ThumbnailGrid.SelectInitialCellOnly();
                        this.DataEntryControls.SetEnableState(ControlsEnableStateEnum.MultipleImageView, this.ThumbnailGrid.SelectedCount());
                    }
                }
                //else if (this.IsThumbnailGridVisible == true && this.ThumbnailGridState > 1)
                else if (this.IsThumbnailGridVisible == true)
                    {
                    // State: currently zoomed in on ThumbnailGrid, but not at the minimum step
                    // Zoom in another step
                    //this.ThumbnailGridState--;
                    ThumbnailGridRefreshStatus status = this.RefreshThumbnailGrid(zoomIn);
                    if (status == ThumbnailGridRefreshStatus.NotEnoughSpaceForEvenOneCell)
                    {
                        // we couldn't refresh the grid, likely because there is not enough space available to show even a single image at this image state
                        // So try again by zooming in another step
                        this.TryZoomInOrOut(zoomIn, imageMousePosition, videoMousePosition);
                    }
                    else if (status == ThumbnailGridRefreshStatus.AtMaximumZoomLevel 
                        || status == ThumbnailGridRefreshStatus.Aborted)

                    {
                        return;
                    }
                    else if (status == ThumbnailGridRefreshStatus.AtZeroZoomLevel)
                    {
                        if (this.displayingImage)
                        {
                            this.SwitchToImageView();
                        }
                        else
                        {
                            this.SwitchToVideoView();
                        }
                    }
                }
                else if (this.IsThumbnailGridVisible == true )
                {
                    // State: zoomed in on ThumbnailGrid, but at the minimum step
                    // Switch to the image or video, depending on what was last displayed
                    // update the magnifying glass

                    if (this.displayingImage)
                    {
                        this.SwitchToImageView();
                    }
                    else
                    {
                        this.SwitchToVideoView();
                    }
                }
                else
                {
                    if (this.displayingImage)
                    {
                        // If we are zooming in off the image, then correct the mouse position to the edge of the image
                        if (imageMousePosition.X > this.ImageToDisplay.ActualWidth)
                        {
                            imageMousePosition.X = this.ImageToDisplay.ActualWidth;
                        }
                        if (imageMousePosition.Y > this.ImageToDisplay.ActualHeight)
                        {
                            imageMousePosition.Y = this.ImageToDisplay.ActualHeight;
                        }
                        this.ScaleImage(imageMousePosition, zoomIn);
                    }
                }
            }
        }

        // Refresh only the episode information in the thumbnail grid
        public void DisplayEpisodeTextInThumbnailGridIfWarranted()
        {
            this.ThumbnailGrid.DisplayEpisodeTextIfWarranted();
        }

        // If the ThumbnailGrid is displayed, refresh it. Use a timer if the we are navigating via a slider (to avoid excessive refreshes)
        public void RefreshIfMultipleImagesAreDisplayed(bool isInSliderNavigation)
        {
            if (this.IsThumbnailGridVisible == true)
            {
                // State: zoomed in on ThumbnailGrid.
                // Updating it ensures that the correct image is shown as the first cell
                // However, if we are navigating with the slider, delay update as otherwise it can't keep up
                if (isInSliderNavigation)
                {
                    // Refresh the ThumbnailGrid only via the timer, where it will 
                    // try to refresh only when the user pauses (or ends) navigation via the slider
                    this.timerSlider.Stop();
                    this.timerSlider.Start();
                }
                else
                {
                    this.RefreshThumbnailGrid(null); // null signals a refresh at the current zoom level
                }
            }
        }

        // Refresh the ThumbnailGrid
        private ThumbnailGridRefreshStatus RefreshThumbnailGrid(bool? zoomIn)
        {
            if (this.ThumbnailGrid == null)
            {
                return ThumbnailGridRefreshStatus.Aborted;
            }
            // Find the current height of the available space and split it the number of rows defined by the state. i.e. state 1 is 2 rows, 2 is 3 rows, etc.
            // However, if the resulting image is less than a minimum height, then ignore it.
            //if (!resizing && cellHeight < Constant.ThumbnailGrid.MinumumThumbnailHeight) return ThumbnailGridRefreshStatus.AtMaximumZoomLevel;
            return this.ThumbnailGrid.Refresh(this.ThumbnailGrid.Width, this.ThumbnailGrid.Height, zoomIn);
        }

        private void TimerSlider_Tick(object sender, EventArgs e)
        {
            this.timerSlider.Stop();
            this.RefreshThumbnailGrid(null); // null signals a refresh at the current zoom level
        }
        #endregion

        #region Mouse Event Handlers
        // On Mouse down, record the location, and who sent it.
        // We will use this information on move and up events to discriminate between 
        // panning/zooming vs. marking. 
        private void ImageVideoOrCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            this.previousMousePosition = e.GetPosition(this);
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                this.mouseDownLocation = (this.displayingImage)
                    ? e.GetPosition(this.ImageToDisplay)
                    : e.GetPosition(this.VideoPlayer.Video);
                this.mouseDownSender = (UIElement)sender;

                // If its more than the given time interval since the last click, then we are on the 2nd click of a double click
                // If we aren't then we are on the first click and thus we want to reset the time.
                TimeSpan timeSinceLastClick = DateTime.Now - this.mouseDoubleClickTime;
                if (timeSinceLastClick.TotalMilliseconds < Constant.MarkableCanvas.DoubleClickTimeThreshold.TotalMilliseconds)
                {
                    this.isDoubleClick = true;
                }
                else
                {
                    this.isDoubleClick = false;
                    this.mouseDoubleClickTime = DateTime.Now;
                }
                // Panning: ensure we are reset to false at the beginning of a mouse down
                this.isPanning = false;
            }
        }

        // If we move the mouse with the left mouse button press, translate the image
        private void MarkableCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            Point mousePosition = (this.displayingImage)
                    ? e.GetPosition(this.ImageToDisplay)
                    : e.GetPosition(this.VideoPlayer.Video);

            // If we are not yet in panning mode, switch into it if the user has moved at least the threshold distance from mouse down position
            if (e.LeftButton == MouseButtonState.Pressed && this.isPanning == false && (this.mouseDownLocation - mousePosition).Length > Constant.MarkableCanvas.MarkingVsPanningDistanceThreshold)
            {
                this.isPanning = true;
            }

            // The magnifying glass is visible only if the current mouse position is over the image. 
            // Note that it uses the actual (transformed) bounds of the image            
            if (this.magnifyingGlass.IsEnabled && this.displayingImage)
            {
                Point transformedSize = this.transformGroup.Transform(new Point(this.ImageToDisplay.ActualWidth, this.ImageToDisplay.ActualHeight));
                bool mouseOverImage = (mousePosition.X <= transformedSize.X) && (mousePosition.Y <= transformedSize.Y);
                this.SetMagnifiersAccordingToCurrentState(mouseOverImage, false);
            }
            else if (this.OffsetLens.IsEnabled && this.displayingImage == false)
            {
                Point transformedSize = this.transformGroup.Transform(new Point(this.VideoPlayer.Video.ActualWidth, this.VideoPlayer.Video.ActualHeight));
                bool mouseOverVideo = (mousePosition.X <= transformedSize.X) && (mousePosition.Y <= transformedSize.Y);
                this.SetMagnifiersAccordingToCurrentState(false, mouseOverVideo);
            }

            if (this.isPanning)
            {
                // If the left button is pressed, translate (pan) across the scaled image or video
                // We hide the magnifying glass during panning so it won't be distracting.
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    // Don't show magnifiers when panning
                    this.SetMagnifiersAccordingToCurrentState(false, false);
                    if (this.displayingImage)
                    {
                        // Translation is possible only if the image isn't already scaled
                        if (this.imageToDisplayScale.ScaleX != 1.0 || this.imageToDisplayScale.ScaleY != 1.0)
                        {
                            this.Cursor = Cursors.ScrollAll;    // Change the cursor to a panning cursor
                            this.TranslateImage(mousePosition);
                        }
                    }
                    else
                    {
                        // Translation is possible only if the video isn't already scaled
                        if (this.VideoPlayer.IsUnScaled == false)
                        {
                            this.Cursor = Cursors.ScrollAll;    // Change the cursor to a panning cursor
                            this.VideoPlayer.TranslateVideo(mousePosition, previousMousePosition);
                        }
                    }
                }
            }
            else
            {
                // Ensure the cursor is a normal arrow cursor
                this.Cursor = Cursors.Arrow;
            }
            this.canvasToMagnify.Width = this.ImageToMagnify.ActualWidth;      // Make sure that the canvas is the same size as the image
            this.canvasToMagnify.Height = this.ImageToMagnify.ActualHeight;

            // update the magnifying glass
            this.RedrawMagnifyingGlassIfVisible();
            this.previousMousePosition = mousePosition;
        }

        private void ImageVideoOrCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            // Make sure the cursor reverts to the normal arrow cursor
            this.Cursor = Cursors.Arrow;
            this.mouseDoubleClickTime = DateTime.Now;

            // Is this the end of a translate operation, or of placing a marker?
            // We decide by checking if the left button has been released, the mouse location is
            // smaller than a given threshold, and less than 200 ms have passed since the original
            // mouse down. i.e., the use has done a rapid click and release on a small location
            if ((e.LeftButton == MouseButtonState.Released) &&
                (sender == this.mouseDownSender) &&
                this.isPanning == false &&
                this.isDoubleClick == false)
            {
                if (this.displayingImage)
                {
                    // Get the current point, and create a marker on it.
                    Point position = e.GetPosition(this.ImageToDisplay);
                    position = Marker.ConvertPointToRatio(position, this.ImageToDisplay.ActualWidth, this.ImageToDisplay.ActualHeight);
                    if (Marker.IsPointValidRatio(position))
                    {
                        // Add the marker if its between 0,0 and 1,1. This should always be the case, but there was one case
                        // where it was recorded in the database as Ininity, INfinity, so this should guard against that.
                        Marker marker = new Marker(null, position);

                        // don't add marker to the marker list
                        // Main window is responsible for filling in remaining properties and adding it.
                        this.SendMarkerEvent(new MarkerEventArgs(marker, true));
                        e.Handled = true;
                    }
                }
                else
                {
                    // The video player is displayed and we are not panning)
                    // Toggle Play or Pause 
                    this.VideoPlayer.TryTogglePlayOrPause();
                }
            }
            // Show the magnifying glass if its enables, as it may have been hidden during other mouseDown operations
            // this.ShowMagnifierIfEnabledOtherwiseHide();
            this.SetMagnifiersAccordingToCurrentState(true, true);
            this.RedrawMagnifyingGlassIfVisible();
        }

        // Remove a marker on a right mouse button up event
        private void Marker_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            Canvas canvas = (Canvas)sender;
            Marker marker = (Marker)canvas.Tag;
            this.Markers.Remove(marker);
            this.SendMarkerEvent(new MarkerEventArgs(marker, false));
            this.RedrawMarkers();
        }


        private void ImageOrCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            bool zoomIn = e.Delta > 0; // Zooming in if delta is positive, else zooming out

            // Eliminate overly exuberant mouse wheel events
            // Check the time interval between mouse wheel events. If below a threshold, ignore the event.
            // 1. This manages rapid turns of the wheel that would otherwise cause over-shooting of desired zoom.
            // 2. It introduces a longer time threshold to switch from the image to the ThumbnailGrid, in order to give a natural 'break point' between the two.
            // 3. A windows 10 bug (so it seems) generates 2 mouse wheel events for every mouse wheel click
            //    This tries to catch that and eliminate the second click. 

            TimeSpan timeDifference = DateTime.Now - lastMouseWheelDateTime;
            if (timeDifference < TimeSpan.FromMilliseconds(500)) // At least a 500 msecs delay in use of the scroll wheel is needed between transitions
            {
                if (zoomIn == true &&
                    ((this.ImageToDisplay.Visibility == Visibility.Visible && this.imageToDisplayScale.ScaleX == Constant.MarkableCanvas.ImageZoomMinimum)
                     || (this.VideoPlayer.Visibility == Visibility.Visible && this.VideoPlayer.IsUnScaled == true)))
                {
                    // Pause on the transition from unzoomed image/video to zoomed image/video
                    return;
                }

                if (zoomIn == false &&
                    ((this.ImageToDisplay.Visibility == Visibility.Visible && this.imageToDisplayScale.ScaleX == Constant.MarkableCanvas.ImageZoomMinimum)
                      || (this.VideoPlayer.Visibility == Visibility.Visible && this.VideoPlayer.IsUnScaled == true)))
                {
                    // Pause on the transition from unscaled image/video to thumbnail Grid
                    return;
                }
            }
            lastMouseWheelDateTime = DateTime.Now;

            // Zoom in or out
            Point imageMousePosition = e.GetPosition(this.ImageToDisplay);
            Point videoMousePosition = e.GetPosition(this.VideoPlayer.Video);
            this.TryZoomInOrOut(zoomIn, imageMousePosition, videoMousePosition);
        }

        // Hide the magnifying glass when the mouse cursor leaves the image
        private void ImageOrCanvas_MouseLeave(object sender, MouseEventArgs e)
        {
            this.SetMagnifiersAccordingToCurrentState(false, false);
        }
        #endregion

        #region SizeChanged Event Handlers
        private void ImageToMagnify_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // keep the magnifying glass canvas in sync with the magnified image size
            // this update triggers a call to CanvasToMagnify_SizeChanged
            this.canvasToMagnify.Width = this.ImageToMagnify.ActualWidth;
            this.canvasToMagnify.Height = this.ImageToMagnify.ActualHeight;
        }

        // resize content and update transforms when canvas size changes
        private void MarkableImageCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            this.ImageToDisplay.Width = this.ActualWidth;
            this.ImageToDisplay.Height = this.ActualHeight;

            this.VideoPlayer.Width = this.ActualWidth;
            this.VideoPlayer.Height = this.ActualHeight;

            this.ThumbnailGrid.Width = this.ActualWidth;
            this.ThumbnailGrid.Height = this.ActualHeight;
            if (this.ThumbnailGrid.Visibility == Visibility.Visible)
            {
                // Refresh the ThumbnailGrid only via the timer, where it will 
                // try to refresh only if the SizeChanged event doesn't refire after the given interval i.e.,
                // when the user pauses or completes the manual resizing action
                this.timerResize.Stop();
                this.timerResize.Start();
            }

            this.imageToDisplayScale.CenterX = 0.5 * this.ActualWidth;
            this.imageToDisplayScale.CenterY = 0.5 * this.ActualHeight;

            // clear the bookmark (if any) as it will no longer be correct
            // if needed, the bookmark could be rescaled instead
            // this.bookmark.Reset();
        }

        // Refresh the ThumbnailGrid when the timer fires 
        private void TimerResize_Tick(object sender, EventArgs e)
        {
            this.timerResize.Stop();
            if (ThumbnailGridRefreshStatus.NotEnoughSpaceForEvenOneCell == this.RefreshThumbnailGrid(null)) // null signals a refresh at the current zoom level
            {
                // We couldn't show at least one image in the overview, so go back to the normal view
                this.SwitchToImageView();
            }
        }

        private void CanvasToMagnify_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // redraw markers so they're in the right place to appear in the magnifying glass
            this.RedrawMarkers();
            this.RefreshBoundingBoxes();
            // update the magnifying glass's contents
            this.RedrawMagnifyingGlassIfVisible();
        }

        // Whenever the image size changes, refresh the markers so they appear in the correct place
        private void ImageToDisplay_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            this.RedrawMarkers();
            this.RefreshBoundingBoxes();
        }

        // Whenever the image size changes, refresh the markers so they appear in the correct place
        private void VideoToDisplay_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            this.RedrawMarkers();
            this.RefreshBoundingBoxes();
        }
        #endregion

        #region Key Event Handlers
        // if it's < or > key zoom out or in around the mouse point
        // If its an H, RedrawBoundingBoxes will hide ow the detection boxes
        private void MarkableCanvas_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.OemPeriod:
                    // '>' : zoom in
                    Point imageMousePosition = Mouse.GetPosition(this.ImageToDisplay);
                    Point videoMousePosition = Mouse.GetPosition(this.VideoPlayer.Video);
                    this.TryZoomInOrOut(true, imageMousePosition, videoMousePosition);
                    break;
                // zoom out
                case Key.OemComma:
                    // '<' : zoom out
                    Point imageMousePosition2 = Mouse.GetPosition(this.ImageToDisplay);
                    Point videoMousePosition2 = Mouse.GetPosition(this.VideoPlayer.Video);
                    this.TryZoomInOrOut(false, imageMousePosition2, videoMousePosition2); ;
                    break;
                // if the current file's a video allow the user to hit the space bar to start or stop playing the video
                case Key.Space:
                    // This is desirable as the play or pause button doesn't necessarily have focus and it saves the user having to click the button with
                    // the mouse.
                    if (this.VideoPlayer.TryTogglePlayOrPause() == false)
                    {
                        return;
                    }
                    break;
                case Key.H:
                    // Will hide detection boxes, if any
                    if (!e.IsRepeat)
                    {
                        if (IsThumbnailGridVisible == false)
                        {
                            this.RefreshBoundingBoxes();
                        }
                        else
                        {
                            this.ThumbnailGrid.RefreshBoundingBoxesAndEpisodeInfo();
                        }
                    }
                    break;
                case Key.P:
                    // Show previous/next image in epesode in a popup, regardless of the current selection
                    if (!this.IsThumbnailGridVisible && !e.IsRepeat)
                    {
                        this.EpisodePopupIsVisible(true);
                    }
                    break;
                //case Key.X:
                //  Used for testing changes of the OffsetLens direction, which is currently turned off
                //    if (this.OffsetLens.Direction == OffsetLensDirection.NorthEast)
                //    {
                //        this.OffsetLens.SetDirection(OffsetLensDirection.NorthWest);
                //    }
                //    else if (this.OffsetLens.Direction == OffsetLensDirection.NorthWest)
                //    {
                //        this.OffsetLens.SetDirection(OffsetLensDirection.SouthWest);
                //    }
                //    else if (this.OffsetLens.Direction == OffsetLensDirection.SouthWest)
                //    {
                //        this.OffsetLens.SetDirection(OffsetLensDirection.SouthEast);
                //    }
                //    else if (this.OffsetLens.Direction == OffsetLensDirection.SouthEast)
                //    {
                //        this.OffsetLens.SetDirection(OffsetLensDirection.NorthEast);
                //    }
                //    break;
                default:
                    return;
            }
            e.Handled = true;
        }

        // If its an H, RedrawBoundingBoxes will show the detection boxes
        private void MarkableCanvas_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.H:
                    // Will show detection boxes, if any
                    if (!e.IsRepeat)
                    {
                        if (IsThumbnailGridVisible == false)
                        {
                            this.RefreshBoundingBoxes();
                        }
                        else
                        {
                            this.ThumbnailGrid.RefreshBoundingBoxesAndEpisodeInfo();
                        }
                    }
                    break;
                case Key.P:
                    // Show previous/next image regardless of selection
                    if (!e.IsRepeat)
                    {
                        this.EpisodePopupIsVisible(false);
                    }
                    break;
                default:
                    return;
            }
            e.Handled = true;
        }
        #endregion

        #region Private methods: Translate Image
        // Given the mouse location on the image, translate the image
        // This is normally called from a left mouse move event
        private void TranslateImage(Point mousePosition)
        {
            // Get the center point on the image
            Point center = this.PointFromScreen(this.ImageToDisplay.PointToScreen(new Point(this.ImageToDisplay.Width / 2.0, this.ImageToDisplay.Height / 2.0)));

            // Calculate the delta position from the last location relative to the center
            double newX = center.X + mousePosition.X - this.previousMousePosition.X;
            double newY = center.Y + mousePosition.Y - this.previousMousePosition.Y;

            // get the translated image width
            double imageWidth = this.ImageToDisplay.Width * this.imageToDisplayScale.ScaleX;
            double imageHeight = this.ImageToDisplay.Height * this.imageToDisplayScale.ScaleY;

            // Limit the delta position so that the image stays on the screen
            if (newX - imageWidth / 2.0 >= 0.0)
            {
                newX = imageWidth / 2.0;
            }
            else if (newX + imageWidth / 2.0 <= this.ActualWidth)
            {
                newX = this.ActualWidth - imageWidth / 2.0;
            }

            if (newY - imageHeight / 2.0 >= 0.0)
            {
                newY = imageHeight / 2.0;
            }
            else if (newY + imageHeight / 2.0 <= this.ActualHeight)
            {
                newY = this.ActualHeight - imageHeight / 2.0;
            }

            // Translate the canvas and redraw the markers
            this.imageToDisplayTranslation.X += newX - center.X;
            this.imageToDisplayTranslation.Y += newY - center.Y;

            this.RedrawMarkers();
            this.RefreshBoundingBoxes();
        }
        #endregion

        #region Private methods: Episodes
        // Display or hide the episode popup
        private void EpisodePopupIsVisible(bool isVisible)
        {
            FileDatabase fileDatabase = Util.GlobalReferences.MainWindow?.DataHandler?.FileDatabase;
            if (fileDatabase == null)
            {
                return;
            }
            if (this.episodePopup == null)
            {
                episodePopup = new EpisodePopup(this, fileDatabase, 160);
            }
            else
            {
                // reset the filedatabase just in case it has been reloaded
                // to a new image set since the last time we used it
                episodePopup.FileDatabase = fileDatabase;
            }
            episodePopup.Show(isVisible, 6);
        }
        #endregion

        #region Private methods: Draw Marker Methods
        private Canvas DrawMarker(Marker marker, Size canvasRenderSize, bool doTransform)
        {
            Canvas markerCanvas = new Canvas();
            markerCanvas.MouseRightButtonUp += new MouseButtonEventHandler(this.Marker_MouseRightButtonUp);
            markerCanvas.MouseWheel += new MouseWheelEventHandler(this.ImageOrCanvas_MouseWheel); // Make the mouse wheel work over marks as well as the image

            if (String.IsNullOrEmpty(marker.Tooltip.Trim()))
            {
                markerCanvas.ToolTip = null;
            }
            else
            {
                markerCanvas.ToolTip = marker.Tooltip;
            }
            markerCanvas.Tag = marker;

            // Create a marker
            Ellipse mark = new Ellipse();
            mark.Width = Constant.MarkableCanvas.MarkerDiameter;
            mark.Height = Constant.MarkableCanvas.MarkerDiameter;
            mark.Stroke = marker.Brush;
            mark.StrokeThickness = Constant.MarkableCanvas.MarkerStrokeThickness;
            mark.Fill = MarkableCanvas.MarkerFillBrush;
            markerCanvas.Children.Add(mark);

            // Draw another Ellipse as a black outline around it
            Ellipse blackOutline = new Ellipse();
            blackOutline.Stroke = Brushes.Black;
            blackOutline.Width = mark.Width + 1;
            blackOutline.Height = mark.Height + 1;
            blackOutline.StrokeThickness = 1;
            markerCanvas.Children.Add(blackOutline);

            // And another Ellipse as a white outline around it
            Ellipse whiteOutline = new Ellipse();
            whiteOutline.Stroke = Brushes.White;
            whiteOutline.Width = blackOutline.Width + 1;
            whiteOutline.Height = blackOutline.Height + 1;
            whiteOutline.StrokeThickness = 1;
            markerCanvas.Children.Add(whiteOutline);

            // maybe add emphasis
            double outerDiameter = whiteOutline.Width;
            Ellipse glow = null;
            if (marker.Emphasise)
            {
                glow = new Ellipse();
                glow.Width = whiteOutline.Width + Constant.MarkableCanvas.MarkerGlowDiameterIncrease;
                glow.Height = whiteOutline.Height + Constant.MarkableCanvas.MarkerGlowDiameterIncrease;
                glow.StrokeThickness = Constant.MarkableCanvas.MarkerGlowStrokeThickness;
                glow.Stroke = mark.Stroke;
                glow.Opacity = Constant.MarkableCanvas.MarkerGlowOpacity;
                markerCanvas.Children.Add(glow);

                outerDiameter = glow.Width;
            }

            markerCanvas.Width = outerDiameter;
            markerCanvas.Height = outerDiameter;

            double position = (markerCanvas.Width - mark.Width) / 2.0;
            Canvas.SetLeft(mark, position);
            Canvas.SetTop(mark, position);

            position = (markerCanvas.Width - blackOutline.Width) / 2.0;
            Canvas.SetLeft(blackOutline, position);
            Canvas.SetTop(blackOutline, position);

            position = (markerCanvas.Width - whiteOutline.Width) / 2.0;
            Canvas.SetLeft(whiteOutline, position);
            Canvas.SetTop(whiteOutline, position);

            if (marker.Emphasise)
            {
                position = (markerCanvas.Width - glow.Width) / 2.0;
                Canvas.SetLeft(glow, position);
                Canvas.SetTop(glow, position);
            }

            if (marker.ShowLabel)
            {
                Label label = new Label();
                label.Content = marker.Tooltip;
                label.Opacity = 0.6;
                label.Background = Brushes.White;
                label.Padding = new Thickness(0, 0, 0, 0);
                label.Margin = new Thickness(0, 0, 0, 0);
                markerCanvas.Children.Add(label);

                position = (markerCanvas.Width / 2.0) + (whiteOutline.Width / 2.0);
                Canvas.SetLeft(label, position);
                Canvas.SetTop(label, markerCanvas.Height / 2);
            }

            // Get the point from the marker, and convert it so that the marker will be in the right place
            if (Marker.IsPointValidRatio(marker.Position) == false)
            {
                // We had one case where the marker point was recorded as Infinity,Infinity. Not sure why.
                // As a workaround, we just make sure the marker is a valid ration. If it isn't we just put the marker in the middle
                // Yup, a hack, but its a very rare bug and thus this is good enough. 
                // While we can instead repair the database, its not really worth the bother of coding that.
                marker.Position = new Point(.5, .5);
            }
            Point screenPosition = Marker.ConvertRatioToPoint(marker.Position, canvasRenderSize.Width, canvasRenderSize.Height);
            if (doTransform)
            {
                screenPosition = this.transformGroup.Transform(screenPosition);
            }

            Canvas.SetLeft(markerCanvas, screenPosition.X - markerCanvas.Width / 2.0);
            Canvas.SetTop(markerCanvas, screenPosition.Y - markerCanvas.Height / 2.0);
            Canvas.SetZIndex(markerCanvas, 0);
            markerCanvas.MouseDown += this.ImageVideoOrCanvas_MouseDown;
            markerCanvas.MouseMove += this.MarkableCanvas_MouseMove;
            markerCanvas.MouseLeftButtonUp += this.ImageVideoOrCanvas_MouseUp;
            return markerCanvas;
        }

        private void DrawMarkers(Canvas canvas, Size canvasRenderSize, bool doTransform)
        {
            if (this.Markers != null)
            {
                foreach (Marker marker in this.Markers)
                {
                    Canvas markerCanvas = this.DrawMarker(marker, canvasRenderSize, doTransform);
                    canvas.Children.Add(markerCanvas);
                }
            }
        }

        /// <summary>
        /// Remove all and then draw all the markers
        /// </summary>
        private void RedrawMarkers()
        {
            this.RemoveMarkers(this);
            this.RemoveMarkers(this.canvasToMagnify);
            if (this.ImageToDisplay != null)
            {
                this.DrawMarkers(this, this.ImageToDisplay.RenderSize, true);
                this.DrawMarkers(this.canvasToMagnify, this.canvasToMagnify.RenderSize, false);
            }
        }

        // remove all markers from the canvas
        private void RemoveMarkers(Canvas canvas)
        {
            for (int index = canvas.Children.Count - 1; index >= 0; index--)
            {
                if (canvas.Children[index] is Canvas && canvas.Children[index] != this.magnifyingGlass)
                {
                    // Its either a marker or a bounding box, so we have to figure out which one.
                    if (canvas.Children[index] is Canvas tempCanvas && (tempCanvas.Tag != null && tempCanvas.Tag.ToString() != Constant.MarkableCanvas.BoundingBoxCanvasTag))
                    {
                        canvas.Children.RemoveAt(index);
                    }
                }
            }
        }
        #endregion
    }
}
