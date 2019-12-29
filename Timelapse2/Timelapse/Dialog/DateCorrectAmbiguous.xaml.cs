﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Timelapse.Controls;
using Timelapse.Database;
using Timelapse.Util;

namespace Timelapse.Dialog
{
    /// <summary>
    /// Detects and displays ambiguous dates, and allows the user to select which ones (if any) should be swapped.
    /// </summary>
    public partial class DateCorrectAmbiguous : DialogWindow
    {
        // Remember passed in arguments
        private readonly FileDatabase fileDatabase;

        private readonly List<AmbiguousDateRange> ambiguousDatesList; // Will contain a list of all initial images containing ambiguous dates and their state

        // Tracks whether any changes to the data or database are made
        private bool IsAnyDataUpdated = false;

        #region Initialization
        public DateCorrectAmbiguous(Window owner, FileDatabase fileDatabase) : base(owner)
        {
            // Check the arguments for null 
            ThrowIf.IsNullArgument(fileDatabase, nameof(fileDatabase));

            this.InitializeComponent();
            this.fileDatabase = fileDatabase;
            this.ambiguousDatesList = new List<AmbiguousDateRange>();
            this.DateChangeFeedback.FolderPath = fileDatabase.FolderPath;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;

            // Find and display the ambiguous dates in the current selected set
            // This is a fast operation, so we don't bother to show a progress bar here
            if (this.FindAllAmbiguousDatesInSelectedImageSet() == true)
            {
                this.PopulateDateChangeFeedback();
                this.StartDoneButton.IsEnabled = this.DateChangeFeedback.AreAnySelected();
            }
            else
            {
                // Since there are no ambiguous dates, we are pretty well done!
                this.DoneMessagePanel.Visibility = Visibility.Visible;
                this.StartDoneButton.Visibility = Visibility.Collapsed;
                this.CancelButton.Content = "Done";
                this.Height = this.MinHeight;
            }
            Mouse.OverrideCursor = null;
        }

        // Update the display
        private void PopulateDateChangeFeedback()
        {
            this.DateChangeFeedback.ShowDifferenceColumn = false;
            this.FeedbackPanel.Visibility = Visibility.Visible;
            foreach (AmbiguousDateRange ambiguousDateRange in this.ambiguousDatesList)
            {
                ImageRow image;
                image = this.fileDatabase.FileTable[ambiguousDateRange.StartIndex];
                string newDate;
                DateTimeHandler.TrySwapDayMonth(image.DateTime, out DateTimeOffset swappedDate);
                newDate = DateTimeHandler.ToDisplayDateString(swappedDate.Date);
                string numFilesWithThatDate = ambiguousDateRange.Count.ToString();
                this.DateChangeFeedback.AddFeedbackRow(image.File, DateTimeHandler.ToDisplayDateString(image.DateTimeIncorporatingOffset.Date), newDate, numFilesWithThatDate, image, ambiguousDateRange);
            }
        }
        #endregion

        #region Create the ambiguous date list
        // Create a list of all initial images containing ambiguous dates.
        // This includes calculating the start and end rows of all images matching an ambiguous date
        private bool FindAllAmbiguousDatesInSelectedImageSet()
        {
            int start = this.SearchForNextAmbiguousDateInSelectedImageSet(0);
            while (start != -1)
            {
                int end = this.GetLastImageOnSameDay(start, out int count);
                this.ambiguousDatesList.Add(new AmbiguousDateRange(start, end, count, false));
                start = this.SearchForNextAmbiguousDateInSelectedImageSet(end + 1);
            }
            return (this.ambiguousDatesList.Count > 0) ? true : false;
        }

        // Starting from the index, navigate successive image rows until an ambiguous date is found
        // If it can't find an ambiguous date, it will return -1.
        private int SearchForNextAmbiguousDateInSelectedImageSet(int startIndex)
        {
            for (int index = startIndex; index < this.fileDatabase.CurrentlySelectedFileCount; index++)
            {
                ImageRow image = this.fileDatabase.FileTable[index];
                DateTimeOffset imageDateTime = image.DateTimeIncorporatingOffset;
                if (imageDateTime.Day <= Constant.Time.MonthsInYear)
                {
                    return index; // If the date is ambiguous, return the row index. 
                }
            }
            return -1; // -1 means all dates are unambiguous
        }

        // Given a starting index, find its date and then go through the successive images until the date differs.
        // Return the final image that is dated the same date as this image
        // Assumption is that the index is valid and is pointing to an image with a valid date.
        // However, it still tests for problems and returns -1 if there was a problem.
        private int GetLastImageOnSameDay(int startIndex, out int count)
        {
            count = 1; // We start at 1 as we have at least one image (the starting image) with this date
            int lastMatchingDate;

            // Check if index is in range
            if (startIndex >= this.fileDatabase.CurrentlySelectedFileCount || startIndex < 0)
            {
                return -1;   // The index is out of range.
            }

            // Parse the provided starting date. Return -1 if it cannot.
            ImageRow image = this.fileDatabase.FileTable[startIndex];
            DateTimeOffset desiredDateTime = image.DateTimeIncorporatingOffset;

            lastMatchingDate = startIndex;
            for (int index = startIndex + 1; index < this.fileDatabase.CurrentlySelectedFileCount; index++)
            {
                // Parse the date for the given row.
                image = this.fileDatabase.FileTable[index];
                DateTimeOffset imageDateTime = image.DateTimeIncorporatingOffset;

                if (desiredDateTime.Date == imageDateTime.Date)
                {
                    lastMatchingDate = index;
                    count++;
                    continue;
                }
                return lastMatchingDate; // This statement is reached only when the date differs, which means the last valid image is the one before it.
            }
            return lastMatchingDate; // if we got here, it means that we arrived at the end of the records
        }
        #endregion

        #region Update files with the new date time
        // Actually update the dates as needed
        private async Task<int> ApplyDateTimeChangesAsync()
        {
            // Set up a progress handler that will update the progress bar
            Progress<ProgressBarArguments> progressHandler = new Progress<ProgressBarArguments>(value =>
            {
                // Update the progress bar
                this.UpdateProgressBar(value.PercentDone, value.Message, value.CancelEnabled);
            });
            IProgress<ProgressBarArguments> progress = progressHandler as IProgress<ProgressBarArguments>;

            return await Task.Run(() =>
            {
                int totalFileCount = 0;
                int count = this.ambiguousDatesList.Count;
                int dateIndex = 0;
                foreach (AmbiguousDateRange ambDate in this.ambiguousDatesList)
                {
                    // Provide progress bar feedback
                    if (ambDate.SwapDates)
                    {
                        this.IsAnyDataUpdated = true;
                        this.fileDatabase.ExchangeDayAndMonthInFileDates(ambDate.StartIndex, ambDate.EndIndex);
                        totalFileCount += ambDate.Count;
                    }
                    // Provide feedback if the operation was cancelled during the database update
                    // Update the progress bar every time interval to indicate what file we are working on
                    if (this.ReadyToRefresh())
                    {
                        dateIndex++;
                        int percentDone = Convert.ToInt32(dateIndex / Convert.ToDouble(count) * 100.0);
                        progress.Report(new ProgressBarArguments(percentDone, String.Format("Swapping day with month for {0} / {1} ambiguous dates", dateIndex, count), false, false));
                        Thread.Sleep(Constant.ThrottleValues.RenderingBackoffTime);  // Allows the UI thread to update every now and then
                    }
                    // The cancellation pattern is shown commented out. We don't do anything with the cancellation token, as we are actually updating the database at this point
                    // and don't want a partially done update.
                    //if (Token.IsCancellationRequested == true)
                    //{
                    //    return;
                    //}
                }
                return totalFileCount;
            }, this.Token).ConfigureAwait(continueOnCapturedContext: true); // Set to true as we need to continue in the UI context
           
        }
        #endregion 

        #region ProgressBar helper
        // Show progress information in the progress bar, and to enable or disable its cancel button
        private void UpdateProgressBar(int percent, string message, bool cancelEnabled)
        {
            ProgressBar bar = Utilities.GetVisualChild<ProgressBar>(this.BusyIndicator);
            Label textMessage = Utilities.GetVisualChild<Label>(this.BusyIndicator);
            Button cancelButton = Utilities.GetVisualChild<Button>(this.BusyIndicator);

            if (bar != null && !cancelEnabled)
            {
                // Treat it as a progressive progress bar
                bar.Value = percent;
                bar.IsIndeterminate = false;
            }
            else if (cancelEnabled)
            {
                // If its at 100%, treat it as a random bar
                bar.IsIndeterminate = true;
            }

            // Update the text message
            if (textMessage != null)
            {
                textMessage.Content = message;
            }

            // We don't want the cancel button enabled
            if (cancelButton != null)
            {
                cancelButton.IsEnabled = cancelEnabled;
                cancelButton.Content = cancelEnabled ? "Cancel": "Writing data...";
            }
        }
        #endregion

        #region Button Callbackes
        // Select all / none of the checkboxes in the datechangedfeedback panel.
        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            this.ButtonSelectAll.Content = this.ButtonSelectAll.IsChecked == true ? "Select none" : "Select all";
            this.DateChangeFeedback.SelectAll(this.ButtonSelectAll.IsChecked == true);
            this.StartDoneButton.IsEnabled = this.DateChangeFeedback.AreAnySelected();
        }

        // When the start button is clicked, 
        // - apply the date change
        // - change the UI so that the start button (and its event handler) becomes a 'Done' button, 
        //   temporarily disable the window's close button, and show the progress bar.
        private async void Start_Click(object sender, RoutedEventArgs e)
        {
            // We have a valide new time that differs by at least one second.
            // Configure the UI's initial state
            this.CancelButton.IsEnabled = false;
            this.CancelButton.Visibility = Visibility.Hidden;
            this.StartDoneButton.Content = "_Done";
            this.StartDoneButton.Click -= this.Start_Click;
            this.StartDoneButton.Click += this.Done_Click;
            this.StartDoneButton.IsEnabled = false;
            this.BusyIndicator.IsBusy = true;
            this.CloseButtonIsEnabled(false);

            int totalFileCount = await this.ApplyDateTimeChangesAsync().ConfigureAwait(true);

            // Update the UI final state
            this.BusyIndicator.IsBusy = false;
            this.StartDoneButton.IsEnabled = true;
            this.CloseButtonIsEnabled(true);
            // Show the final message
            if (totalFileCount > 0)
            { 
                this.DoneMessagePanel.Content = "Dates for " + totalFileCount.ToString() + " files were swapped";
            }
            else
            {
                this.DoneMessagePanel.Content = "Nothing changed as no dates were selected.";
            }
            this.DoneMessagePanel.Visibility = Visibility.Visible;
            this.FeedbackPanel.Visibility = Visibility.Collapsed;
            this.Height = this.MinHeight;
        }

        private void Done_Click(object sender, RoutedEventArgs e)
        {

            // We return true if the database was altered. Returning true will reset the FileTable, as a FileSelectAndShow will be done.
            // Kinda hacky as it expects a certain behaviour of the caller, but it works.
            this.DialogResult = this.IsAnyDataUpdated;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
        }

        private void CancelAsyncOperationButton_Click(object sender, RoutedEventArgs e)
        {
            // A no-op. We don't make use of the cancellation token callback (see comment above)
            // Set this so that it will be caught in the above await task
            // this.TokenSource.Cancel();
        }
        #endregion

        private void DateChangeFeedback_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            this.StartDoneButton.IsEnabled = this.DateChangeFeedback.AreAnySelected();
        }
    }
}
