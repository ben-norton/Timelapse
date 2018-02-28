﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Timelapse.Database;
using Timelapse.Util;

namespace Timelapse.Dialog
{
    public partial class DateTimeRereadFromFiles : Window
    {
        private FileDatabase database;

        public DateTimeRereadFromFiles(FileDatabase database, Window owner)
        {
            this.InitializeComponent();
            this.database = database;
            this.Owner = owner;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Utilities.SetDefaultDialogPosition(this);
            Utilities.TryFitDialogWindowInWorkingArea(this);
        }

        // Used to label the datagrid feedback columns with the appropriate headers
        private void DatagridFeedback_AutoGeneratedColumns(object sender, EventArgs e)
        {
            this.FeedbackGrid.Columns[0].Header = "File name";
            this.FeedbackGrid.Columns[0].Width = new DataGridLength(1, DataGridLengthUnitType.Star);
            this.FeedbackGrid.Columns[1].Header = "Date / time adjustment";
            this.FeedbackGrid.Columns[1].Width = new DataGridLength(2, DataGridLengthUnitType.Star);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
        }

        private void DoneButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
        }

        private void StartDoneButton_Click(object sender, RoutedEventArgs e)
        {
            // This list will hold key / value pairs that will be bound to the datagrid feedback, 
            // which is the way to make those pairs appear in the data grid during background worker progress updates
            ObservableCollection<DateTimeRereadFeedbackTuple> feedbackRows = new ObservableCollection<DateTimeRereadFeedbackTuple>();
            this.FeedbackGrid.ItemsSource = feedbackRows;
            this.cancelButton.IsEnabled = false;
            this.StartDoneButton.Content = "_Done";
            this.StartDoneButton.Click -= this.StartDoneButton_Click;
            this.StartDoneButton.Click += this.DoneButton_Click;
            this.StartDoneButton.IsEnabled = false;

            BackgroundWorker backgroundWorker = new BackgroundWorker()
            {
                WorkerReportsProgress = true
            };

            backgroundWorker.DoWork += (ow, ea) =>
            {
                // this runs on the background thread; its written as an anonymous delegate
                // We need to invoke this to allow updates on the UI
                this.Dispatcher.Invoke(new Action(() =>
                {
                    // First, change the UIprovide some feedback
                    backgroundWorker.ReportProgress(0, new DateTimeRereadFeedbackTuple("Pass 1: Examining image and video files...", "Checking if dates/time differ..."));
                }));

                // Pass 1. Check to see what dates/times need updating.
                List<ImageRow> filesToAdjust = new List<ImageRow>();
                int count = this.database.CurrentlySelectedFileCount;
                TimeZoneInfo imageSetTimeZone = this.database.ImageSet.GetTimeZone();
                for (int fileIndex = 0; fileIndex < count; ++fileIndex)
                {
                    // We will store the various times here
                    ImageRow file = this.database.Files[fileIndex];
                    DateTimeOffset originalDateTime = file.GetDateTime();
                    string feedbackMessage = String.Empty;
                    try
                    {
                        // Get the image (if its there), get the new dates/times, and add it to the list of images to be updated 
                        // Note that if the image can't be created, we will just to the catch.
                        bool usingMetadataTimestamp = true;
                        DateTimeAdjustment dateTimeAdjustment = file.TryReadDateTimeOriginalFromMetadata(this.database.FolderPath, imageSetTimeZone);
                        if (dateTimeAdjustment == DateTimeAdjustment.MetadataNotUsed)
                        {
                            file.SetDateTimeOffsetFromFileInfo(this.database.FolderPath, imageSetTimeZone);  // We couldn't read the metadata, so get a candidate date/time from the file
                            usingMetadataTimestamp = false;
                        }
                        DateTimeOffset rescannedDateTime = file.GetDateTime();
                        bool sameDate = (rescannedDateTime.Date == originalDateTime.Date) ? true : false;
                        bool sameTime = (rescannedDateTime.TimeOfDay == originalDateTime.TimeOfDay) ? true : false;
                        bool sameUTCOffset = (rescannedDateTime.Offset == originalDateTime.Offset) ? true : false;
 
                        if (sameDate && sameTime && sameUTCOffset)
                        {
                            // Nothing needs changing
                            feedbackMessage = "\x2713"; // Checkmark
                        }
                        else
                        {
                            filesToAdjust.Add(file);
                            feedbackMessage = "\x2716 "; // X mark
                            feedbackMessage += DateTimeHandler.ToDisplayDateTimeString(originalDateTime) + " \x2192 " + DateTimeHandler.ToDisplayDateTimeString(rescannedDateTime);
                            feedbackMessage += usingMetadataTimestamp ? " (read from metadata)" : " (read from file)";
                        }
                    }
                    catch (Exception exception)
                    {
                        Utilities.PrintFailure(String.Format("Unexpected exception processing '{0}' in DateReread - StartDoneButton_Click. {1}", file.FileName, exception.ToString()));
                        feedbackMessage += String.Format("\x2716 skipping: {0}", exception.Message);
                    }

                    backgroundWorker.ReportProgress(0, new DateTimeRereadFeedbackTuple(file.FileName, feedbackMessage));
                    if (fileIndex % Constant.ThrottleValues.SleepForImageRenderInterval == 0)
                    {
                        Thread.Sleep(Constant.ThrottleValues.RenderingBackoffTime); // Put in a delay every now and then, as otherwise the UI won't update.
                    }
                }

                // Pass 2. Update each date as needed 
                string message = String.Empty;
                backgroundWorker.ReportProgress(0, new DateTimeRereadFeedbackTuple(String.Empty, String.Empty)); // A blank separator
                backgroundWorker.ReportProgress(0, new DateTimeRereadFeedbackTuple("Pass 2: Updating dates and times", String.Format("Updating {0} images and videos...", filesToAdjust.Count)));
                Thread.Yield(); // Allow the UI to update.

                List<ColumnTuplesWithWhere> imagesToUpdate = new List<ColumnTuplesWithWhere>();
                foreach (ImageRow image in filesToAdjust)
                {
                    imagesToUpdate.Add(image.GetDateTimeColumnTuples());
                }
                database.UpdateFiles(imagesToUpdate);  // Write the updates to the database
                backgroundWorker.ReportProgress(0, new DateTimeRereadFeedbackTuple(null, "Done."));
            };
            backgroundWorker.ProgressChanged += (o, ea) =>
            {
                feedbackRows.Add((DateTimeRereadFeedbackTuple)ea.UserState);
                this.FeedbackGrid.ScrollIntoView(FeedbackGrid.Items[FeedbackGrid.Items.Count - 1]);
            };
            backgroundWorker.RunWorkerCompleted += (o, ea) =>
            {
                this.StartDoneButton.IsEnabled = true;
            };
            backgroundWorker.RunWorkerAsync();
        }
    }
}
