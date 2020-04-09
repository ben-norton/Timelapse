﻿using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Timelapse.Controls;
using Timelapse.Dialog;
using Timelapse.Enums;
using Timelapse.EventArguments;
using Timelapse.Util;

namespace Timelapse.Images
{
    // This portion of the Markable Canvas 
    // - handles image procesing adjustments as requested by events sent via the ImageAdjuster.
    // - generates events indicating image state to be consumed by the Image Adjuster to adjust its own state (e.g., disabled, reset, etc).
    public partial class MarkableCanvas : Canvas
    {
        #region EventHandler definitions
        // Whenever an image state is changed, raise an event (to be consumed by ImageAdjuster)
        public event EventHandler<ImageStateEventArgs> ImageStateChanged; // raise when an image state is changed (to be consumed by ImageAdjuster)
        #endregion

        #region Private variables
        // State information - whether the current image is being processed
        private bool Processing = false;

        // When started, the timer tries to updates image processing to ensure that the last image processing values are applied
        private readonly DispatcherTimer timerImageProcessingUpdate = new DispatcherTimer();

        // image processing parameters
        private int contrast;
        private int brightness;
        private bool detectEdges;
        private bool sharpen;
        private bool useGamma;
        private float gammaValue;

        // We track the last parameters used, as if they haven't changed we won't update the image
        private int lastContrast = 0;
        private int lastBrightness = 0;
        private bool lastDetectEdges = false;
        private bool lastSharpen = false;
        private bool lastUseGamma = false;
        private float lastGammaValue = 1;
        #endregion

        #region Consume and handle image processing events
        // This should be invoked by the MarkableCanvas Constructor to initialize aspects of this partial class
        private void InitializeImageAdjustment()
        {
            // When started, ensures that the final image processing parameters are applied to the image
            this.timerImageProcessingUpdate.Interval = TimeSpan.FromSeconds(0.1);
            this.timerImageProcessingUpdate.Tick += this.TimerImageProcessingUpdate_Tick;
        }

        // Receive an event containing new image processing parameters.
        // Store these parameters and then try to update the image
        public async void AdjustImage_EventHandler(object sender, ImageAdjusterEventArgs e)
        {
            if (e == null)
            {
                // Shouldn't happen, but...
                return;
            }

            string path = DataEntryHandler.TryGetFilePathFromGlobalDataHandler();
            if (String.IsNullOrEmpty(path))
            {
                // The file cannot be opened or is not displayable. 
                // Signal change in image state, which essentially says there is no displayable image to adjust (consumed by ImageAdjuster)
                this.OnImageStateChanged(new ImageStateEventArgs(false, false)); //  Signal change in image state (consumed by ImageAdjuster)
                return;
            }

            if (e.OpenExternalViewer)
            {
                // The event says to open an external photo viewer. Try to do so.
                // Note that we don't do any image processing on this event if if this is the case.
                if (ProcessExecution.TryProcessStart(path) == false)
                {
                    string extension = Path.GetExtension(path);
                    // Can't open the image file. Note that file must exist at this pint as we checked for that above.
                    MessageBox messageBox = new MessageBox("Can't open a photo viewer.", Util.GlobalReferences.MainWindow);
                    messageBox.Message.Icon = System.Windows.MessageBoxImage.Error;
                    messageBox.Message.Reason = "You probably don't have a default program set up to display a photo viewer for " + extension + " files";
                    messageBox.Message.Solution = "Set up a photo viewer in your Windows Settings." + Environment.NewLine;
                    messageBox.Message.Solution += "\u2022 go to 'Default apps', select 'Photo Viewer' and choose a desired photo viewer." + Environment.NewLine;
                    messageBox.Message.Solution += "\u2022 or right click on an " + extension + " file and set the default viewer that way";
                    messageBox.ShowDialog();
                }
                return;
            }

            // Process the image based on the current image processing arguments. 
            if (e.Contrast == this.lastContrast && e.Brightness == this.lastBrightness && e.DetectEdges == this.lastDetectEdges && e.Sharpen == this.lastSharpen && e.UseGamma == this.lastUseGamma && e.GammaValue == this.lastGammaValue)
            {
                // If there is no change from the last time we processed an image, abort as it would not make any difference to what the user sees
                return;
            }
            this.contrast = e.Contrast;
            this.brightness = e.Brightness;
            this.detectEdges = e.DetectEdges;
            this.sharpen = e.Sharpen;
            this.useGamma = e.UseGamma;
            this.gammaValue = e.GammaValue;
            this.timerImageProcessingUpdate.Start();
            await UpdateAndProcessImage().ConfigureAwait(true);
        }

        // Because an event may come in while an image is being processed, the timer
        // will try to continue the processing the image with the latest image processing parameters (if any) 
        private async void TimerImageProcessingUpdate_Tick(object sender, EventArgs e)
        {
            if (this.Processing)
            {
                return;
            }
            if (this.contrast != this.lastContrast || this.brightness != this.lastBrightness || this.detectEdges != this.lastDetectEdges || this.sharpen != this.lastSharpen || this.lastUseGamma != this.useGamma || this.lastGammaValue != this.gammaValue)
            {
                // Update the image as at least one parameter has changed (which will affect the image's appearance)
                await this.UpdateAndProcessImage().ConfigureAwait(true);
            }
            this.timerImageProcessingUpdate.Stop();
        }

        // Update the image according to the image processing parameters.
        private async Task UpdateAndProcessImage()
        {
            // If its processing the image, try again later (via the time),
            if (this.Processing)
            {
                return;
            }
            try
            {
                string path = DataEntryHandler.TryGetFilePathFromGlobalDataHandler(); ;
                if (String.IsNullOrEmpty(path))
                {
                    // If we cannot get a valid file, there is no image to manipulate. 
                    // So abort and signal a change in image state that says there is no displayable image to adjust (consumed by ImageAdjuster)
                    this.OnImageStateChanged(new ImageStateEventArgs(false, false));
                }

                // Set the state to Processing is used to indicate that other attempts to process the image should be aborted util this is done.
                this.Processing = true;
                using (MemoryStream imageStream = new MemoryStream(File.ReadAllBytes(path)))
                {
                    // Remember the currently selected image processing states, so we can compare them later for changes
                    this.lastBrightness = this.brightness;
                    this.lastContrast = this.contrast;
                    this.lastSharpen = this.sharpen;
                    this.lastDetectEdges = this.detectEdges;
                    this.lastUseGamma = this.useGamma;
                    this.lastGammaValue = this.gammaValue;
                    BitmapFrame bf = await ImageProcess.StreamToImageProcessedBitmap(imageStream, this.brightness, this.contrast, this.sharpen, this.detectEdges, this.useGamma, this.gammaValue).ConfigureAwait(true);
                    if (bf != null)
                    {
                        this.ImageToDisplay.Source = await ImageProcess.StreamToImageProcessedBitmap(imageStream, this.brightness, this.contrast, this.sharpen, this.detectEdges, this.useGamma, this.gammaValue).ConfigureAwait(true);
                    }
                }
            }
            catch
            {
                // We failed on this image. To avoid this happening again,
                // Signal change in image state, which essentially says there is no adjustable image (consumed by ImageAdjuster)
                this.OnImageStateChanged(new ImageStateEventArgs(false, false));
            }
            this.Processing = false;
        }
        #endregion

        #region Generate ImageStateChange event

        // An explicit check the current status of the image state and generate an event to reflect that.
        // Typically used when the image adjustment window is opened for the first time, as the markable canvas needs to signal its state to it.
        public void GenerateImageStateChangeEventToReflectCurrentStatus()
        {
            if (this.ClickableImagesState != 0)
            {
                // In the overview
                this.GenerateImageStateChangeEvent(false, false); //  Signal change in image state (consumed by ImageAdjuser)
                return;
            }
            ImageCache imageCache = Util.GlobalReferences.MainWindow?.DataHandler?.ImageCache;
            if (imageCache != null)
            {
                if (imageCache.Current?.IsVideo == true)
                {
                    // Its a video
                    this.GenerateImageStateChangeEvent(false, false); //  Signal change in image state (consumed by ImageAdjuser)
                    return;
                }
                // Its a primary image, but also check the differencing state
                bool isPrimaryImage = imageCache.CurrentDifferenceState == ImageDifferenceEnum.Unaltered;
                this.GenerateImageStateChangeEvent(true, isPrimaryImage); //  Signal change in image state (consumed by ImageAdjuser)
            }
        }

        // Generate an event indicating the image state. To be consumed by the Image Adjuster to adjust its own state (e.g., disabled, reset, etc).
        private void GenerateImageStateChangeEvent(bool isNewImage, bool isPrimaryImage)
        {
            this.OnImageStateChanged(new ImageStateEventArgs(isNewImage, isPrimaryImage)); //  Signal change in image state (consumed by ImageAdjuster)
        }

        protected virtual void OnImageStateChanged(ImageStateEventArgs e)
        {
            ImageStateChanged?.Invoke(this, e);
        }
        #endregion
    }
}
