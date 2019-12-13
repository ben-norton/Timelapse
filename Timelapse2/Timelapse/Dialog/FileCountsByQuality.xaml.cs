﻿using System;
using System.Collections.Generic;
using System.Windows;
using Timelapse.Enums;
using Timelapse.Util;

namespace Timelapse.Dialog
{
    /// <summary>
    /// Dialog to show the user some statistics about the images
    /// </summary>
    public partial class FileCountsByQuality : Window
    {
        /// <summary>
        /// Show the user some statistics about the images in a dialog box
        /// </summary>
        public FileCountsByQuality(Dictionary<FileSelectionEnum, int> counts, Window owner)
        {
            this.InitializeComponent();
            this.Owner = owner;

            // Check the arguments for null 
            ThrowIf.IsNullArgument(counts, nameof(counts));

            // Fill in the counts
            int ok = counts[FileSelectionEnum.Ok];
            this.Light.Text = String.Format("{0,5}", ok);
            int dark = counts[FileSelectionEnum.Dark];
            this.Dark.Text = String.Format("{0,5}", dark);
            int total = ok + dark;
            this.Total.Text = String.Format("{0,5}", total);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Dialogs.TryPositionAndFitDialogIntoWindow(this);
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
        }
    }
}