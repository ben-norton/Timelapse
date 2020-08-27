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
    /// This dialog lets the user specify a corrected date and time of an file. All other dates and times are then corrected by the same amount.
    /// This is useful if (say) the camera was not initialized to the correct date and time.
    /// </summary>
    public partial class DateTimeFixedCorrection : BusyableDialogWindow
    {
        #region Private Variables
        // Remember passed in arguments
        private readonly FileDatabase fileDatabase;
        private readonly ImageRow ImageToCorrect;

        // The initial unaltered date
        private DateTimeOffset initialDate;

        // Tracks whether any changes to the data or database are made
        private bool IsAnyDataUpdated = false;
        #endregion

        #region Consructror, Loaded, Closing and Autogenerated
        public DateTimeFixedCorrection(Window owner, FileDatabase fileDatabase, ImageRow imageToCorrect) : base(owner)
        {
            // Check the arguments for null 
            ThrowIf.IsNullArgument(fileDatabase, nameof(fileDatabase));
            ThrowIf.IsNullArgument(imageToCorrect, nameof(imageToCorrect));

            this.InitializeComponent();
            this.fileDatabase = fileDatabase;
            this.ImageToCorrect = imageToCorrect;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Set up a progress handler that will update the progress bar
            this.InitalizeProgressHandler(this.BusyCancelIndicator);

            // Set up the initial UI and values
            // Get the image filename and image and display them
            this.FileName.Content = this.ImageToCorrect.File;
            this.FileName.ToolTip = this.ImageToCorrect.File;
            this.SampleImage.Source = this.ImageToCorrect.LoadBitmap(this.fileDatabase.FolderPath, out bool isCorruptOrMissing);

            // Configure datetime picker to the initial date on the images plus callbacks
            this.initialDate = this.ImageToCorrect.DateTimeIncorporatingOffset;
            this.OriginalDate.Content = DateTimeHandler.ToStringDisplayDateTime(this.initialDate);
            DataEntryHandler.Configure(this.DateTimePicker, this.initialDate.DateTime);
            this.DateTimePicker.ValueChanged += this.DateTimePicker_ValueChanged;
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
        private async Task<ObservableCollection<DateTimeFeedbackTuple>> TaskFixedCorrectionAsync(TimeSpan adjustment)
        {
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
                this.DatabaseUpdateFileDates(this.Progress, adjustment, feedbackRows);

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

        private void DatabaseUpdateFileDates(IProgress<ProgressBarArguments> progress, TimeSpan adjustment, ObservableCollection<DateTimeFeedbackTuple> feedbackRows)
        {
            // Note that this passes a function which is invoked by the fileDatabase method. 
            // This not only calculates the new times, but updates the progress bar as the fileDatabase method iterates through the files.
            this.fileDatabase.UpdateAdjustedFileTimes(
               (string fileName, int fileIndex, int count, DateTimeOffset imageDateTime) =>
               {
                   if (adjustment.Duration() >= TimeSpan.FromSeconds(1))
                   {
                       // We only add to the feedback row if the change duration is > 1 second, as otherwise we don't change it.
                       string oldDT = DateTimeHandler.ToStringDisplayDateTime(imageDateTime);
                       string newDT = DateTimeHandler.ToStringDisplayDateTime(imageDateTime + adjustment);
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
               0,
               this.fileDatabase.CountAllCurrentlySelectedFiles - 1,
               this.Token);
        }
        #endregion

        #region Button callbacks
        private async void Start_Click(object sender, RoutedEventArgs e)
        {
            if (this.DateTimePicker.Value.HasValue == false || DateTimeHandler.TryParseDisplayDateTimeString((string)this.OriginalDate.Content, out DateTime originalDateTime) == false)
            {
                // This should not happen
                System.Windows.MessageBox.Show("Could not change the date/time, as it date is not in a format recognized by Timelapse: " + (string)this.OriginalDate.Content);
                return;
            }
            TimeSpan adjustment = this.DateTimePicker.Value.Value - originalDateTime;

            // Need at least a 1 second difference to do anything.
            if (Math.Abs(adjustment.TotalSeconds) < 1)
            {
                // This should not happen
                System.Windows.MessageBox.Show("At least a 1 second difference is required to do anything " + (string)this.OriginalDate.Content);
                return;
            }

            // We have a valide new time that differs by at least one second.
            // Configure the UI's initial state
            this.CancelButton.IsEnabled = false;
            this.CancelButton.Visibility = Visibility.Hidden;
            this.StartDoneButton.Content = "_Done";
            this.StartDoneButton.Click -= this.Start_Click;
            this.StartDoneButton.Click += this.Done_Click;
            this.StartDoneButton.IsEnabled = false;
            this.BusyCancelIndicator.IsBusy = true;
            this.WindowCloseButtonIsEnabled(false);

            // This call does all the actual updating...
            ObservableCollection<DateTimeFeedbackTuple> feedbackRows = await this.TaskFixedCorrectionAsync(adjustment).ConfigureAwait(true);

            // Hide the busy indicator and update the UI, e.g., to show which files have changed dates
            // Provide summary feedback 
            if (this.IsAnyDataUpdated && this.Token.IsCancellationRequested == false)
            {
                string message = string.Format("Updated {0}/{1} files whose dates have changed.", feedbackRows.Count, this.fileDatabase.CountAllCurrentlySelectedFiles);
                feedbackRows.Insert(0, (new DateTimeFeedbackTuple("---", message)));
            }

            this.BusyCancelIndicator.IsBusy = false;
            this.PrimaryPanel.Visibility = Visibility.Collapsed;
            this.FeedbackPanel.Visibility = Visibility.Visible;
            this.FeedbackGrid.ItemsSource = feedbackRows;
            this.StartDoneButton.IsEnabled = true;
            this.WindowCloseButtonIsEnabled(true);
        }

        private void Done_Click(object sender, RoutedEventArgs e)
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
        #endregion

        #region DateTimePicker callbacks
        private void DateTimePicker_ValueChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            // Because of the bug in the DateTimePicker, we have to get the changed value from the string
            // as DateTimePicker.Value.Value can have the old date rather than the new one.
            TimeSpan difference = TimeSpan.Zero;
            if (DateTimeHandler.TryParseDisplayDateTimeString(this.DateTimePicker.Text, out DateTime newDateTime))
            {
                difference = newDateTime - this.initialDate;
                this.StartDoneButton.IsEnabled = (difference == TimeSpan.Zero) ? false : true;
            }
            this.StartDoneButton.IsEnabled = (difference == TimeSpan.Zero) ? false : true;
        }

        // Mitigates a bug where ValueChanged is not triggered when the date/time is changed
        private void DateTimePicker_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            this.DateTimePicker_ValueChanged(null, null);
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
