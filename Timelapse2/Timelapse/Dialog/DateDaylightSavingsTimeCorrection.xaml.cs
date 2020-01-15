﻿using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Timelapse.Controls;
using Timelapse.Database;
using Timelapse.Util;

namespace Timelapse.Dialog
{
    /// <summary>
    /// This dialog lets a user enter a time change correction of +/-1 hour, which is propagated backwards/forwards.
    /// The current image as set by the user in the radio buttons.
    /// </summary>
    public partial class DateDaylightSavingsTimeCorrection : DialogWindow
    {
        private readonly int currentImageRow;
        private readonly FileDatabase fileDatabase;
        private readonly FileTableEnumerator fileEnumerator;

        // Tracks whether any changes to the data or database are made
        private bool IsAnyDataUpdated = false;

        #region Initialization
        public DateDaylightSavingsTimeCorrection(Window owner, FileDatabase database, FileTableEnumerator fileEnumerator) : base(owner)
        {
            // Check the arguments for null 
            ThrowIf.IsNullArgument(database, nameof(database));
            ThrowIf.IsNullArgument(fileEnumerator, nameof(fileEnumerator));

            this.InitializeComponent();
            this.fileDatabase = database;
            this.fileEnumerator = fileEnumerator;
            this.currentImageRow = fileEnumerator.CurrentRow;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Set up the initial UI and values

            // Get the original date and display it
            this.OriginalDate.Content = fileEnumerator.Current.DateTimeAsDisplayable;
            this.NewDate.Content = this.OriginalDate.Content;

            // Display the image. While we should be on a valid image (our assumption), we can still show a missing or corrupted image if needed
            this.Image.Source = fileEnumerator.Current.LoadBitmap(this.fileDatabase.FolderPath, out _);
            this.FileName.Content = fileEnumerator.Current.File;
            this.FileName.ToolTip = this.FileName.Content;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            this.DialogResult = this.Token.IsCancellationRequested || this.IsAnyDataUpdated;
        }

        // Label and size the datagrid column headers
        private void DatagridFeedback_AutoGeneratedColumns(object sender, EventArgs e)
        {
            this.FeedbackGrid.Columns[0].Header = "File name (only for files whose date was changed)";
            this.FeedbackGrid.Columns[0].Width = new DataGridLength(1, DataGridLengthUnitType.Auto);
            this.FeedbackGrid.Columns[1].Header = "Old date  \x2192  New Date \x2192 Delta";
            this.FeedbackGrid.Columns[1].Width = new DataGridLength(2, DataGridLengthUnitType.Star);
        }
        #endregion

        #region Calculate times and Update files
        // Set up all the Linear Corrections as an asynchronous task which updates the progress bar as needed
        private async Task<ObservableCollection<DateTimeFeedbackTuple>> TaskDaylightSavingsCorrectionAsync(TimeSpan adjustment, int startRow, int endRow)
        {
            // Set up a progress handler that will update the progress bar
            Progress<ProgressBarArguments> progressHandler = new Progress<ProgressBarArguments>(value =>
            {
                // Update the progress bar
                DialogWindow.UpdateProgressBar(this.BusyCancelIndicator, value.PercentDone, value.Message, value.IsCancelEnabled, value.IsIndeterminate);
            });
            IProgress<ProgressBarArguments> progress = progressHandler as IProgress<ProgressBarArguments>;

            // A side effect of running this task is that the FileTable will be updated, which means that,
            // at the very least, the calling function will need to run FilesSelectAndShow to either
            // reload the FileTable with the updated data, or to reset the FileTable back to its original form
            // if the operation was cancelled.
            this.IsAnyDataUpdated = true;

            // Reread the Date/Times from each file 
            return await Task.Run(() =>
            {
                // Collects feedback to display in a datagrid after the operation is done
                ObservableCollection<DateTimeFeedbackTuple> feedbackRows = new ObservableCollection<DateTimeFeedbackTuple>();

                int count = this.fileDatabase.FileTable.RowCount;
                this.DatabaseUpdateFileDates(progress, adjustment, startRow, endRow, feedbackRows);

                // Provide feedback if the operation was cancelled during the database update
                if (Token.IsCancellationRequested == true)
                {
                    feedbackRows.Clear();
                    feedbackRows.Add(new DateTimeFeedbackTuple("Cancelled", "No changes were made"));
                    return feedbackRows;
                }
                return feedbackRows;
            }, this.Token).ConfigureAwait(continueOnCapturedContext: true); // Set to true as we need to continue in the UI context
        }

        private void DatabaseUpdateFileDates(IProgress<ProgressBarArguments> progress, TimeSpan adjustment, int startRow, int endRow, ObservableCollection<DateTimeFeedbackTuple> feedbackRows)
        {
            // Note that this passes a function which is invoked by the fileDatabase method. 
            // This not only calculates the new times, but updates the progress bar as the fileDatabase method iterates through the files.
            // this.fileDatabase.AdjustFileTimes(daylightSavingsAdjustment, startRow, endRow); // For all rows...
            this.fileDatabase.UpdateAdjustedFileTimes(
               (string fileName, int fileIndex, int count, DateTimeOffset imageDateTime) =>
               {
                   if (adjustment.Duration() >= TimeSpan.FromSeconds(1))
                   {
                       // We only add to the feedback row if the change duration is > 1 second, as otherwise we don't change it.
                       string oldDT = DateTimeHandler.ToDisplayDateTimeString(imageDateTime);
                       string newDT = DateTimeHandler.ToDisplayDateTimeString(imageDateTime + adjustment);
                       feedbackRows.Add(new DateTimeFeedbackTuple(fileName, oldDT + " \x2192 " + newDT + " \x2192 " + PrettyPrintTimeAdjustment(adjustment)));
                   }

                   // Update the progress bar every time interval to indicate what file we are working on
                   if (this.ReadyToRefresh())
                   {
                       int percentDone = Convert.ToInt32(fileIndex / Convert.ToDouble(count) * 100.0);
                       progress.Report(new ProgressBarArguments(percentDone, String.Format("Pass 1: Calculating new date/times for {0} / {1} files", fileIndex, count), true, false));
                       Thread.Sleep(Constant.ThrottleValues.RenderingBackoffTime);  // Allows the UI thread to update every now and then
                   }

                   if (fileIndex >= count)
                   {
                       // After all files are processed, the next step would be updating the database. Disable the cancel button too.
                       // This really should be somehow signalled from the invoking method (ideally ExecuteNonQueryWrappedInBeginEnd every update interval), but this is a reasonable workaround.
                       progress.Report(new ProgressBarArguments(100, String.Format("Pass 2: Updating {0} files. Please wait...", feedbackRows.Count), false, true));
                       Thread.Sleep(Constant.ThrottleValues.RenderingBackoffTime);  // Allows the UI thread to update every now and then
                   }
                   return imageDateTime + adjustment; // Returns the new time
               },
               startRow,
               endRow, //this.fileDatabase.CurrentlySelectedFileCount - 1,
               this.Token);
        }

        #endregion

        // When the user clicks ok, add/subtract an hour propagated forwards/backwards as specified
        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            // We have a valide new time that differs by at least one second.
            // Configure the UI's initial state
            this.CancelButton.IsEnabled = false;
            this.CancelButton.Visibility = Visibility.Hidden;
            this.StartDoneButton.Content = "_Done";
            this.StartDoneButton.Click -= this.StartButton_Click;
            this.StartDoneButton.Click += this.DoneButton_Click;
            this.StartDoneButton.IsEnabled = false;
            this.BusyCancelIndicator.IsBusy = true;
            this.CloseButtonIsEnabled(false);

            // Calculate the required adjustment
            bool forward = (bool)this.rbForward.IsChecked;
            int startRow;
            int endRow;
            if (forward)
            {
                startRow = this.currentImageRow;
                endRow = this.fileDatabase.CountAllCurrentlySelectedFiles - 1;
            }
            else
            {
                startRow = 0;
                endRow = this.currentImageRow;
            }

            // Update the database
            int hours = (bool)this.rbAddHour.IsChecked ? 1 : -1;
            TimeSpan daylightSavingsAdjustment = new TimeSpan(hours, 0, 0);

            // This call does all the actual updating...
            ObservableCollection<DateTimeFeedbackTuple> feedbackRows = await this.TaskDaylightSavingsCorrectionAsync(daylightSavingsAdjustment, startRow, endRow).ConfigureAwait(true);

            // Hide the busy indicator and update the UI, e.g., to show which files have changed dates
            // Provide summary feedback 
            if (this.IsAnyDataUpdated && this.Token.IsCancellationRequested == false)
            {
                string message = string.Format("Updated {0}/{1} files whose dates have changed.", feedbackRows.Count, this.fileDatabase.CountAllCurrentlySelectedFiles);
                feedbackRows.Insert(0, (new DateTimeFeedbackTuple("---", message)));
            }

            this.BusyCancelIndicator.IsBusy = false;
            this.PrimaryPanel.Visibility = Visibility.Collapsed;
            this.Image.Visibility = Visibility.Collapsed;
            this.FeedbackGrid.ItemsSource = feedbackRows;
            this.FeedbackPanel.Visibility = Visibility.Visible;
            this.StartDoneButton.IsEnabled = true;
            this.CloseButtonIsEnabled(true);
        }

        // Examine the checkboxes to see what state our selection is in, and provide feedback as appropriate
        private void RadioButton_Checked(object sender, RoutedEventArgs e)
        {
            if ((bool)this.rbAddHour.IsChecked || (bool)this.rbSubtractHour.IsChecked)
            {
                if (DateTimeHandler.TryParseDisplayDateTime((string)this.OriginalDate.Content, out DateTime dateTime) == false)
                {
                    this.NewDate.Content = "Problem with this date...";
                    this.StartDoneButton.IsEnabled = false;
                    return;
                }
                int hours = ((bool)this.rbAddHour.IsChecked) ? 1 : -1;
                TimeSpan daylightSavingsAdjustment = new TimeSpan(hours, 0, 0);
                dateTime = dateTime.Add(daylightSavingsAdjustment);
                this.NewDate.Content = DateTimeHandler.ToDisplayDateTimeString(dateTime);
            }
            if (((bool)this.rbAddHour.IsChecked || (bool)this.rbSubtractHour.IsChecked) && ((bool)this.rbBackwards.IsChecked || (bool)this.rbForward.IsChecked))
            {
                this.StartDoneButton.IsEnabled = true;
            }
            else
            {
                this.StartDoneButton.IsEnabled = false;
            }
        }

        #region Button callbacks
        private void DoneButton_Click(object sender, RoutedEventArgs e)
        {
            // We return true if the database was altered but also if there was a cancellation, as a cancelled operation
            // will likely have changed the FileTable (but not database) date entries. Returning true will reset them, as a FileSelectAndShow will be done.
            // Kinda hacky as it expects a certain behaviour of the caller, but it works.
            this.DialogResult = this.Token.IsCancellationRequested || this.IsAnyDataUpdated;
        }

        // Cancel - do nothing
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
        }

        private void CancelAsyncOperationButton_Click(object sender, RoutedEventArgs e)
        {
            // Set this so that it will be caught in the above await task
            this.TokenSource.Cancel();
        }
        #endregion

        #region Utility methods
        // Given the time adjustment to the date, generate a pretty-printed string taht we can use in our feedback
        private static string PrettyPrintTimeAdjustment(TimeSpan adjustment)
        {
            string sign = (adjustment < TimeSpan.Zero) ? "-" : "+";

            // Pretty print the adjustment time, depending upon how many day(s) were included 
            string format;
            if (adjustment.Days == 0)
            {
                format = "{0:s}{1:D2}:{2:D2}:{3:D2}"; // Don't show the days field
            }
            else
            {
                // includes singular or plural form of days
                format = (adjustment.Duration().Days == 1) ? "{0:s}{1:D2}:{2:D2}:{3:D2} {0:s} {4:D} day" : "{0:s}{1:D2}:{2:D2}:{3:D2} {0:s} {4:D} days";
            }
            return string.Format(format, sign, adjustment.Duration().Hours, adjustment.Duration().Minutes, adjustment.Duration().Seconds, adjustment.Duration().Days);
        }
        #endregion
    }
}
